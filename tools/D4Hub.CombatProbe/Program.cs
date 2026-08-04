using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using D4Hub.App.Services;
using D4Hub.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

if (!ProbeOptions.TryParse(args, out var options, out var argumentError))
{
    Console.Error.WriteLine(argumentError);
    ProbeOptions.WriteUsage();
    return 2;
}

try
{
    var report = await RunProbeAsync(options!);
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    var json = JsonSerializer.Serialize(report, jsonOptions);
    if (string.IsNullOrWhiteSpace(options!.OutputPath))
    {
        Console.WriteLine(json);
    }
    else
    {
        WriteAtomically(options.OutputPath, json);
        Console.WriteLine(options.OutputPath);
    }

    return report.Capture.ProcessedFrames == 0 ? 4 : 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Combat video probe failed: {exception.Message}");
    return 3;
}

static async Task<CombatVideoProbeReport> RunProbeAsync(ProbeOptions options)
{
    var calibration = D4VisionCalibrationProfiles.All.SingleOrDefault(profile =>
        string.Equals(profile.Id, options.CalibrationProfileId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"Unknown vision calibration profile '{options.CalibrationProfileId}'.");
    var ocrWidth = checked((int)Math.Round(options.CropWidth * options.OcrScale));
    var ocrHeight = checked((int)Math.Round(options.CropHeight * options.OcrScale));
    if (ocrWidth > OcrEngine.MaxImageDimension || ocrHeight > OcrEngine.MaxImageDimension)
    {
        throw new InvalidOperationException(
            $"OCR bitmap {ocrWidth}x{ocrHeight} exceeds the Windows OCR limit {OcrEngine.MaxImageDimension}.");
    }

    OcrEngine? windowsEngine = null;
    PaddleCombatTextSpottingEngine? paddleEngine = null;
    var runtimeExceptions = new List<ProbeRuntimeException>();
    string? fallbackReason = null;
    int? fallbackAtFrame = null;
    CombatTextModelAvailability modelAvailability;
    if (options.Pipeline == ProbePipelineKind.Paddle)
    {
        try
        {
            paddleEngine = new PaddleCombatTextSpottingEngine();
            modelAvailability = paddleEngine.Availability;
        }
        catch (Exception exception)
        {
            fallbackReason = FormatRuntimeFailure("initialization", exception);
            runtimeExceptions.Add(new ProbeRuntimeException(
                "initialization",
                null,
                exception.GetType().Name,
                exception.Message));
            modelAvailability = new CombatTextModelAvailability(
                false,
                "runtime-initialization-failed",
                fallbackReason,
                "nuget:Sdcb.PaddleOCR.Models.Local/3.3.1",
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }
    else
    {
        modelAvailability = CombatTextModelBundleValidator.Validate(
            options.ModelDirectory,
            calibration);
    }
    var totalStopwatch = Stopwatch.StartNew();
    var process = CreateFfmpegProcess(options);
    if (!process.Start())
    {
        throw new InvalidOperationException("FFmpeg did not start.");
    }

    var diagnosticsTask = process.StandardError.ReadToEndAsync();
    var tracker = new CombatDamageTracker(
        trackLifetimeSeconds: Math.Max(0.3, (1 / options.FramesPerSecond) * 2.25),
        maximumTrackDistance: 96,
        minimumObservationConfidence: calibration.MinimumOcrConfidence);
    var frameSize = checked(ocrWidth * ocrHeight * 4);
    var frameBytes = new byte[frameSize];
    var processedFrames = 0;
    var framesWithDamage = 0;
    var detectedTextInstanceCount = 0;
    var candidateObservationCount = 0;
    var engineRejectedInstanceCount = 0;
    var engineRejectionReasons = new Dictionary<string, int>(StringComparer.Ordinal);
    var ocrSamples = new List<ProbeOcrSample>();
    var frameProcessingMilliseconds = new List<double>();
    var output = process.StandardOutput.BaseStream;
    var frameLimit = GetFrameLimit(options);
    var stoppedAtFrameLimit = false;
    while (await TryReadFrameAsync(output, frameBytes))
    {
        var frameStartedAt = Stopwatch.GetTimestamp();
        var timeSeconds = options.StartSeconds + processedFrames / options.FramesPerSecond;
        IReadOnlyList<CombatTextObservation> observations;
        if (options.Pipeline == ProbePipelineKind.Paddle && paddleEngine is not null)
        {
            try
            {
                var extracted = new ExtractedPixelRegion(
                    new PixelRect(options.CropX, options.CropY, options.CropWidth, options.CropHeight),
                    ocrWidth,
                    ocrHeight,
                    1 / options.OcrScale,
                    1 / options.OcrScale,
                    frameBytes);
                var result = await paddleEngine.ReadExtractedRegionAsync(
                    extracted,
                    calibration,
                    timeSeconds);
                observations = result.Observations;
                detectedTextInstanceCount += result.DetectedInstanceCount;
                engineRejectedInstanceCount += result.RejectedInstanceCount;
                MergeReasons(engineRejectionReasons, result.RejectionReasons);
                CollectPaddleSamples(observations, ocrSamples);
            }
            catch (Exception exception)
            {
                fallbackAtFrame = processedFrames;
                fallbackReason = FormatRuntimeFailure("inference", exception);
                runtimeExceptions.Add(new ProbeRuntimeException(
                    "inference",
                    processedFrames,
                    exception.GetType().Name,
                    exception.Message));
                paddleEngine.Dispose();
                paddleEngine = null;
                (observations, var textInstances) = await ReadWindowsFrameAsync(
                    frameBytes,
                    ocrWidth,
                    ocrHeight,
                    options,
                    calibration,
                    timeSeconds,
                    () => windowsEngine ??= CreateWindowsEngine(calibration.LanguageTag),
                    ocrSamples);
                detectedTextInstanceCount += textInstances;
            }
        }
        else
        {
            (observations, var textInstances) = await ReadWindowsFrameAsync(
                frameBytes,
                ocrWidth,
                ocrHeight,
                options,
                calibration,
                timeSeconds,
                () => windowsEngine ??= CreateWindowsEngine(calibration.LanguageTag),
                ocrSamples);
            detectedTextInstanceCount += textInstances;
        }

        if (observations.Count > 0)
        {
            framesWithDamage++;
            candidateObservationCount += observations.Count;
        }

        tracker.AddFrame(timeSeconds, observations);
        processedFrames++;
        frameProcessingMilliseconds.Add(
            Stopwatch.GetElapsedTime(frameStartedAt).TotalMilliseconds);
        if (frameLimit is > 0 && processedFrames >= frameLimit)
        {
            stoppedAtFrameLimit = true;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            break;
        }
    }

    if (!process.HasExited)
    {
        await process.WaitForExitAsync();
    }

    var diagnostics = await diagnosticsTask;
    paddleEngine?.Dispose();
    if (process.ExitCode != 0 && !stoppedAtFrameLimit)
    {
        throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {diagnostics.Trim()}");
    }

    var damage = tracker.BuildReport();
    if (damage.ReceivedObservationCount != candidateObservationCount)
    {
        throw new InvalidOperationException("The OCR observation count does not match the damage tracker receipt.");
    }

    totalStopwatch.Stop();
    var currentSession = damage.Sessions.LastOrDefault();
    var combinedRejectionReasons = new Dictionary<string, int>(
        damage.RejectionReasons,
        StringComparer.Ordinal);
    MergeReasons(combinedRejectionReasons, engineRejectionReasons);
    var totalRejectedObservationCount = checked(
        damage.RejectedObservationCount + engineRejectedInstanceCount);
    var suspiciousSmallEvents = damage.Events
        .Where(item => item.Damage < 100_000)
        .ToArray();
    var medianConfirmedDamage = GetMedianDamage(damage.Events);
    var cohortMagnitudeRatios = damage.Events
        .Select(item => GetMagnitudeRatio(item.Damage, medianConfirmedDamage))
        .ToArray();
    var catastrophicFormatRejections = new[]
        {
            "mixed-grouped-decimal-risk",
            "implausible-mantissa-shape",
            "candidate-magnitude-conflict"
        }
        .Sum(reason => combinedRejectionReasons.GetValueOrDefault(reason));
    var activePipeline = options.Pipeline == ProbePipelineKind.Paddle && fallbackReason is null
        ? "paddleocr-v5-experimental"
        : "windows-ocr-baseline";
    var orderedProcessing = frameProcessingMilliseconds.Order().ToArray();
    var p95Index = orderedProcessing.Length == 0
        ? 0
        : Math.Min(orderedProcessing.Length - 1, (int)Math.Ceiling(orderedProcessing.Length * 0.95) - 1);
    var quality = damage.Evidence.State switch
    {
        CombatEvidenceState.ConfirmedScreenEstimate => new RealtimeVisionQuality(
            activePipeline == "paddleocr-v5-experimental"
                ? RealtimeVisionQualityLevel.ExperimentalVisualEstimate
                : RealtimeVisionQualityLevel.BaselineScreenEstimate,
            activePipeline,
            damage.Evidence.ConfirmedObservationCoverage,
            damage.Evidence.Detail),
        CombatEvidenceState.InsufficientEvidence => new RealtimeVisionQuality(
            RealtimeVisionQualityLevel.InsufficientEvidence,
            activePipeline,
            damage.Evidence.ConfirmedObservationCoverage,
            damage.Evidence.Detail),
        _ => new RealtimeVisionQuality(
            RealtimeVisionQualityLevel.Unavailable,
            activePipeline,
            null,
            damage.Evidence.Detail)
    };

    return new CombatVideoProbeReport(
        SchemaVersion: 4,
        CreatedAt: DateTimeOffset.UtcNow,
        Source: new ProbeSource(
            options.VideoPath,
            new FileInfo(options.VideoPath).Length,
            await ComputeSha256Async(options.VideoPath)),
        Capture: new ProbeCapture(
            options.StartSeconds,
            options.DurationSeconds,
            options.FramesPerSecond,
            options.CropX,
            options.CropY,
            options.CropWidth,
            options.CropHeight,
            options.OcrScale,
            ocrWidth,
            ocrHeight,
            processedFrames,
            calibration.Id,
            calibration.LanguageTag,
            calibration.DisplayMode,
            calibration.BrightnessThreshold),
        Pipeline: new ProbePipeline(
            options.Pipeline,
            activePipeline,
            fallbackReason is not null,
            fallbackReason,
            fallbackAtFrame,
            modelAvailability,
            quality,
            runtimeExceptions),
        Ocr: new ProbeOcr(
            calibration.LanguageTag,
            detectedTextInstanceCount,
            framesWithDamage,
            candidateObservationCount,
            damage.ParsedObservationCount,
            totalRejectedObservationCount,
            ocrSamples),
        Damage: damage,
        Metrics: new ProbeMetrics(
            candidateObservationCount,
            damage.UniqueEventCount,
            totalRejectedObservationCount,
            combinedRejectionReasons,
            damage.DuplicateObservationCount,
            damage.Evidence.PendingObservationCount,
            damage.Evidence.ConfirmedObservationCoverage,
            damage.Evidence.State,
            suspiciousSmallEvents.Length,
            suspiciousSmallEvents.Aggregate(0L, (total, item) => checked(total + item.Damage)),
            catastrophicFormatRejections,
            cohortMagnitudeRatios.Count(ratio => ratio >= 100),
            cohortMagnitudeRatios.Length == 0 ? 0 : cohortMagnitudeRatios.Max(),
            damage.TotalDamage,
            damage.CurrentOneSecondDamage,
            currentSession?.AverageDps ?? 0,
            currentSession?.PeakOneSecondDamage ?? 0),
        Processing: new ProbeProcessing(
            totalStopwatch.Elapsed.TotalMilliseconds,
            frameProcessingMilliseconds.Count == 0 ? 0 : frameProcessingMilliseconds.Average(),
            orderedProcessing.Length == 0 ? 0 : orderedProcessing[p95Index],
            orderedProcessing.Length == 0 ? 0 : orderedProcessing[^1]),
        FfmpegDiagnostics: diagnostics
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(20)
            .ToArray());
}

static async Task<(IReadOnlyList<CombatTextObservation> Observations, int TextInstanceCount)>
    ReadWindowsFrameAsync(
        byte[] frameBytes,
        int ocrWidth,
        int ocrHeight,
        ProbeOptions options,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        Func<OcrEngine> engineFactory,
        List<ProbeOcrSample> samples)
{
    var thresholdedPixels = VisionRegionPixels.ApplyBrightnessThreshold(
        frameBytes,
        calibration.BrightnessThreshold);
    using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
        thresholdedPixels.AsBuffer(),
        BitmapPixelFormat.Bgra8,
        ocrWidth,
        ocrHeight,
        BitmapAlphaMode.Ignore);
    var ocrResult = await engineFactory().RecognizeAsync(bitmap);
    CollectOcrSamples(ocrResult, timeSeconds, samples);
    return (
        ReadDamageObservations(
            ocrResult,
            timeSeconds,
            options.CropX,
            options.CropY,
            1 / options.OcrScale,
            1 / options.OcrScale),
        ocrResult.Lines.Count);
}

static OcrEngine CreateWindowsEngine(string languageTag)
{
    var language = new Language(languageTag);
    return OcrEngine.TryCreateFromLanguage(language)
        ?? throw new InvalidOperationException(
            $"The Windows {languageTag} OCR engine is unavailable.");
}

static void CollectPaddleSamples(
    IReadOnlyList<CombatTextObservation> observations,
    List<ProbeOcrSample> samples)
{
    const int maximumSamples = 100;
    foreach (var observation in observations)
    {
        if (samples.Count >= maximumSamples)
        {
            return;
        }

        samples.Add(new ProbeOcrSample(
            observation.TimeSeconds,
            observation.RawText,
            [observation.RawText]));
    }
}

static void MergeReasons(
    Dictionary<string, int> destination,
    IReadOnlyDictionary<string, int> source)
{
    foreach (var (reason, count) in source)
    {
        destination[reason] = checked(destination.GetValueOrDefault(reason) + count);
    }
}

static string FormatRuntimeFailure(string stage, Exception exception) =>
    $"{stage}: {exception.GetType().Name}: {exception.Message}";

static double GetMedianDamage(IReadOnlyList<CombatDamageEvent> events)
{
    if (events.Count == 0)
    {
        return 0;
    }

    var ordered = events.Select(item => item.Damage).Order().ToArray();
    var middle = ordered.Length / 2;
    return ordered.Length % 2 == 1
        ? ordered[middle]
        : (ordered[middle - 1] / 2d) + (ordered[middle] / 2d);
}

static double GetMagnitudeRatio(long damage, double medianDamage)
{
    if (damage <= 0 || medianDamage <= 0)
    {
        return 0;
    }

    return Math.Max(damage / medianDamage, medianDamage / damage);
}

static void CollectOcrSamples(OcrResult result, double timeSeconds, List<ProbeOcrSample> samples)
{
    const int maximumSamples = 100;
    if (samples.Count >= maximumSamples)
    {
        return;
    }

    foreach (var line in result.Lines)
    {
        var text = string.Concat(line.Words.Select(word => word.Text));
        if ((text.Contains('万') || text.Contains('亿')) && text.Any(char.IsDigit))
        {
            samples.Add(new ProbeOcrSample(
                timeSeconds,
                text,
                line.Words.Select(word => word.Text).ToArray()));
            if (samples.Count >= maximumSamples)
            {
                return;
            }
        }
    }
}

static int? GetFrameLimit(ProbeOptions options)
{
    int? durationFrameLimit = null;
    if (options.DurationSeconds is > 0)
    {
        var expectedFrames = Math.Ceiling(options.DurationSeconds.Value * options.FramesPerSecond);
        if (expectedFrames <= int.MaxValue)
        {
            durationFrameLimit = (int)expectedFrames;
        }
    }

    return options.MaximumFrames is > 0 && durationFrameLimit is > 0
        ? Math.Min(options.MaximumFrames.Value, durationFrameLimit.Value)
        : options.MaximumFrames ?? durationFrameLimit;
}

static Process CreateFfmpegProcess(ProbeOptions options)
{
    var ocrWidth = checked((int)Math.Round(options.CropWidth * options.OcrScale));
    var ocrHeight = checked((int)Math.Round(options.CropHeight * options.OcrScale));
    var startInfo = new ProcessStartInfo
    {
        FileName = options.FfmpegPath,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("-hide_banner");
    startInfo.ArgumentList.Add("-loglevel");
    startInfo.ArgumentList.Add("warning");
    startInfo.ArgumentList.Add("-nostdin");
    startInfo.ArgumentList.Add("-err_detect");
    startInfo.ArgumentList.Add("ignore_err");
    if (options.StartSeconds > 0)
    {
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(options.StartSeconds.ToString(CultureInfo.InvariantCulture));
    }

    startInfo.ArgumentList.Add("-i");
    startInfo.ArgumentList.Add(options.VideoPath);
    if (options.DurationSeconds is > 0)
    {
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(options.DurationSeconds.Value.ToString(CultureInfo.InvariantCulture));
    }

    startInfo.ArgumentList.Add("-an");
    startInfo.ArgumentList.Add("-sn");
    startInfo.ArgumentList.Add("-dn");
    startInfo.ArgumentList.Add("-vf");
    startInfo.ArgumentList.Add(
        FormattableString.Invariant(
            $"fps={options.FramesPerSecond},crop={options.CropWidth}:{options.CropHeight}:{options.CropX}:{options.CropY},scale={ocrWidth}:{ocrHeight}:flags=lanczos"));
    startInfo.ArgumentList.Add("-pix_fmt");
    startInfo.ArgumentList.Add("bgra");
    startInfo.ArgumentList.Add("-f");
    startInfo.ArgumentList.Add("rawvideo");
    startInfo.ArgumentList.Add("pipe:1");
    return new Process { StartInfo = startInfo };
}

static IReadOnlyList<CombatTextObservation> ReadDamageObservations(
    OcrResult result,
    double timeSeconds,
    double sourceOffsetX,
    double sourceOffsetY,
    double sourceScaleX,
    double sourceScaleY)
{
    var lines = result.Lines.Select(line => new CombatOcrLine(
        line.Words.Select(word => new CombatOcrWord(
            word.Text,
            word.BoundingRect.X,
            word.BoundingRect.Y,
            word.BoundingRect.Width,
            word.BoundingRect.Height)).ToArray()));
    return CombatOcrObservationMapper.ReadDamageObservations(
        lines,
        timeSeconds,
        sourceOffsetX,
        sourceOffsetY,
        sourceScaleX,
        sourceScaleY);
}

static async Task<bool> TryReadFrameAsync(Stream stream, byte[] buffer)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
        if (read == 0)
        {
            if (offset == 0)
            {
                return false;
            }

            throw new EndOfStreamException($"FFmpeg ended after {offset} of {buffer.Length} frame bytes.");
        }

        offset += read;
    }

    return true;
}

static async Task<string> ComputeSha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream));
}

static void WriteAtomically(string path, string content)
{
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath)
        ?? throw new InvalidOperationException("The report output has no parent directory.");
    Directory.CreateDirectory(directory);
    var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
    File.WriteAllText(temporaryPath, content);
    try
    {
        File.Move(temporaryPath, fullPath, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }
}

internal sealed record CombatVideoProbeReport(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    ProbeSource Source,
    ProbeCapture Capture,
    ProbePipeline Pipeline,
    ProbeOcr Ocr,
    CombatDamageReport Damage,
    ProbeMetrics Metrics,
    ProbeProcessing Processing,
    IReadOnlyList<string> FfmpegDiagnostics);

internal sealed record ProbeSource(string Path, long ByteLength, string Sha256);

internal sealed record ProbeCapture(
    double StartSeconds,
    double? RequestedDurationSeconds,
    double FramesPerSecond,
    int CropX,
    int CropY,
    int CropWidth,
    int CropHeight,
    double OcrScale,
    int OcrWidth,
    int OcrHeight,
    int ProcessedFrames,
    string CalibrationProfile,
    string LanguageTag,
    VisionDisplayMode DisplayMode,
    byte BrightnessThreshold);

internal sealed record ProbePipeline(
    ProbePipelineKind RequestedPipeline,
    string ActivePipeline,
    bool FellBackToWindows,
    string? FallbackReason,
    int? FallbackAtFrame,
    CombatTextModelAvailability EngineAvailability,
    RealtimeVisionQuality DataQuality,
    IReadOnlyList<ProbeRuntimeException> RuntimeExceptions);

internal sealed record ProbeRuntimeException(
    string Stage,
    int? FrameIndex,
    string ExceptionType,
    string Message);

internal sealed record ProbeOcr(
    string Language,
    int DetectedTextInstanceCount,
    int FramesWithDamage,
    int CandidateObservationCount,
    int AcceptedObservationCount,
    int RejectedObservationCount,
    IReadOnlyList<ProbeOcrSample> Samples);

internal sealed record ProbeOcrSample(
    double TimeSeconds,
    string LineText,
    IReadOnlyList<string> Words);

internal sealed record ProbeMetrics(
    int ParsedCandidateCount,
    int ConfirmedEventCount,
    int RejectedObservationCount,
    IReadOnlyDictionary<string, int> RejectionReasons,
    int FoldedDuplicateCount,
    int PendingObservationCount,
    double ConfirmedObservationCoverage,
    CombatEvidenceState EvidenceState,
    int SuspiciousSmallConfirmedEventCount,
    long SuspiciousSmallConfirmedDamage,
    int CatastrophicFormatRejectedCount,
    int CohortMagnitudeOutlierEventCount,
    double MaximumCohortMagnitudeRatio,
    long TotalDamage,
    long RecentOneSecondDamage,
    double AverageDps,
    long PeakOneSecondDamage);

internal sealed record ProbeProcessing(
    double TotalWallClockMilliseconds,
    double MeanFrameMilliseconds,
    double P95FrameMilliseconds,
    double MaximumFrameMilliseconds);

internal sealed record ProbeOptions(
    string VideoPath,
    string? OutputPath,
    string FfmpegPath,
    double FramesPerSecond,
    double StartSeconds,
    double? DurationSeconds,
    int CropX,
    int CropY,
    int CropWidth,
    int CropHeight,
    double OcrScale,
    int? MaximumFrames,
    string CalibrationProfileId,
    string ModelDirectory,
    ProbePipelineKind Pipeline)
{
    public static bool TryParse(string[] args, out ProbeOptions? options, out string error)
    {
        options = null;
        error = string.Empty;
        string? videoPath = null;
        string? outputPath = null;
        var ffmpegPath = "ffmpeg";
        var fps = 5d;
        var start = 0d;
        double? duration = null;
        var cropX = 100;
        var cropY = 0;
        var cropWidth = 1400;
        var cropHeight = 800;
        var ocrScale = 1d;
        int? maximumFrames = null;
        var calibrationProfileId = "1080p-zhCN-sdr";
        var modelDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "library",
            "combat-text-models");
        var pipeline = ProbePipelineKind.Windows;

        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            if (index + 1 >= args.Length)
            {
                error = $"Missing value for {name}.";
                return false;
            }

            var value = args[++index];
            switch (name.ToLowerInvariant())
            {
                case "--video":
                    videoPath = value;
                    break;
                case "--output":
                    outputPath = value;
                    break;
                case "--ffmpeg":
                    ffmpegPath = value;
                    break;
                case "--fps":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fps))
                    {
                        error = "FPS must be a number.";
                        return false;
                    }
                    break;
                case "--start":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out start))
                    {
                        error = "Start time must be a number.";
                        return false;
                    }
                    break;
                case "--duration":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDuration))
                    {
                        error = "Duration must be a number.";
                        return false;
                    }
                    duration = parsedDuration;
                    break;
                case "--crop":
                    var parts = value.Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length != 4
                        || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out cropX)
                        || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out cropY)
                        || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out cropWidth)
                        || !int.TryParse(parts[3], CultureInfo.InvariantCulture, out cropHeight))
                    {
                        error = "Crop must use x,y,width,height integers.";
                        return false;
                    }
                    break;
                case "--ocr-scale":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out ocrScale))
                    {
                        error = "OCR scale must be a number.";
                        return false;
                    }
                    break;
                case "--max-frames":
                    if (!int.TryParse(value, CultureInfo.InvariantCulture, out var parsedMaximumFrames))
                    {
                        error = "Maximum frames must be an integer.";
                        return false;
                    }
                    maximumFrames = parsedMaximumFrames;
                    break;
                case "--profile":
                    calibrationProfileId = value;
                    break;
                case "--model-dir":
                    modelDirectory = value;
                    break;
                case "--pipeline":
                    if (!Enum.TryParse<ProbePipelineKind>(value, true, out pipeline))
                    {
                        error = "Pipeline must be windows or paddle.";
                        return false;
                    }
                    break;
                default:
                    error = $"Unknown argument: {name}.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            error = $"Video not found: {videoPath}";
            return false;
        }

        if (!double.IsFinite(fps) || fps is < 0.5 or > 30)
        {
            error = "FPS must be from 0.5 through 30.";
            return false;
        }

        if (!double.IsFinite(start) || start < 0
            || duration is not null && (!double.IsFinite(duration.Value) || duration <= 0)
            || cropX < 0 || cropY < 0 || cropWidth <= 0 || cropHeight <= 0
            || !double.IsFinite(ocrScale) || ocrScale is < 0.5 or > 2
            || maximumFrames is <= 0)
        {
            error = "Start, duration, crop, OCR scale, or maximum-frame values are invalid.";
            return false;
        }

        options = new ProbeOptions(
            Path.GetFullPath(videoPath),
            string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath),
            ffmpegPath,
            fps,
            start,
            duration,
            cropX,
            cropY,
            cropWidth,
            cropHeight,
            ocrScale,
            maximumFrames,
            calibrationProfileId,
            Path.GetFullPath(modelDirectory),
            pipeline);
        return true;
    }

    public static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage: D4Hub.CombatProbe --video <recording.mp4> [--output <report.json>] [--pipeline windows|paddle] [--fps 5] [--start 0] [--duration 20] [--crop x,y,width,height] [--ocr-scale 1] [--profile 1080p-zhCN-sdr] [--model-dir <path>] [--ffmpeg <path>] [--max-frames n]");
    }
}

internal enum ProbePipelineKind
{
    Windows,
    Paddle
}

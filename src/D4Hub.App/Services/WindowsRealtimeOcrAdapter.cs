using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using D4Hub.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace D4Hub.App.Services;

/// <summary>
/// Reads only the calibrated combat-text ROI through the local Windows OCR
/// engine. Scheduling and single-flight behavior are owned by the panel
/// ViewModel; this adapter performs one requested recognition operation.
/// </summary>
public sealed class WindowsRealtimeOcrAdapter : IRealtimeVisionAdapter
{
    private readonly string? _requiredLanguageTag;
    private readonly string _modelDirectory;
    private readonly Dictionary<string, CombatTextModelAvailability> _modelAvailabilityByProfile =
        new(StringComparer.Ordinal);
    private string? _engineLanguageTag;
    private OcrEngine? _engine;

    public WindowsRealtimeOcrAdapter(string? languageTag = null, string? modelDirectory = null)
    {
        if (languageTag is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
            _requiredLanguageTag = languageTag.Trim();
        }

        _modelDirectory = string.IsNullOrWhiteSpace(modelDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "library", "combat-text-models")
            : Path.GetFullPath(modelDirectory);
    }

    public RealtimeVisionCapabilities Capabilities => RealtimeVisionCapabilities.DamageWithPickups;

    public async Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(calibration);
        cancellationToken.ThrowIfCancellationRequested();

        if (_requiredLanguageTag is not null
            && !string.Equals(
                _requiredLanguageTag,
                calibration.LanguageTag,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Adapter language '{_requiredLanguageTag}' does not match calibration language '{calibration.LanguageTag}'.");
        }

        if (calibration.DisplayMode != VisionDisplayMode.StandardDynamicRange)
        {
            throw new InvalidOperationException(
                $"Windows OCR baseline has no validated {calibration.DisplayMode} combat-text preprocessing profile.");
        }

        var combatRegion = calibration.Regions.Values.FirstOrDefault(region =>
            region.Kind == VisionRegionKind.CombatText)
            ?? throw new InvalidOperationException(
                $"Vision calibration '{calibration.Id}' does not define a combat-text region.");
        var pickupRegion = calibration.Regions.Values.FirstOrDefault(region =>
            region.Kind == VisionRegionKind.Materials)
            ?? throw new InvalidOperationException(
                $"Vision calibration '{calibration.Id}' does not define a material-pickup region.");
        var combatReadout = await RecognizeRegionAsync(
            frame,
            combatRegion,
            calibration,
            cancellationToken);
        var pickupReadout = await RecognizeRegionAsync(
            frame,
            pickupRegion,
            calibration,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var lines = combatReadout.Lines;
        var damage = CombatOcrObservationMapper.ReadDamageObservations(
            lines,
            timeSeconds,
            combatReadout.Region.SourceBounds.X,
            combatReadout.Region.SourceBounds.Y,
            combatReadout.Region.SourcePixelsPerOutputPixelX,
            combatReadout.Region.SourcePixelsPerOutputPixelY)
            .Select(observation => observation.Confidence < calibration.MinimumOcrConfidence
                && string.IsNullOrWhiteSpace(observation.RejectionReason)
                    ? observation with { RejectionReason = "below-profile-evidence-threshold" }
                : observation)
            .ToArray();
        var pickups = MaterialPickupObservationMapper.Read(
            pickupReadout.Lines,
            timeSeconds,
            pickupReadout.Region.SourceBounds.X,
            pickupReadout.Region.SourceBounds.Y,
            pickupReadout.Region.SourcePixelsPerOutputPixelX,
            pickupReadout.Region.SourcePixelsPerOutputPixelY);
        var evidenceValues = damage
            .Where(observation => string.IsNullOrWhiteSpace(observation.RejectionReason))
            .Select(observation => observation.Confidence)
            .Concat(pickups
                .Where(observation => string.IsNullOrWhiteSpace(observation.RejectionReason))
                .Select(observation => observation.Confidence))
            .ToArray();
        var evidenceScore = evidenceValues.Length == 0
            ? 0
            : evidenceValues.Average();
        if (!_modelAvailabilityByProfile.TryGetValue(calibration.Id, out var modelAvailability))
        {
            modelAvailability = CombatTextModelBundleValidator.Validate(_modelDirectory, calibration);
            _modelAvailabilityByProfile[calibration.Id] = modelAvailability;
        }
        var modelDetail = modelAvailability.IsAvailable
            ? "Verified ONNX assets are present, but this adapter is explicitly the Windows OCR baseline."
            : $"Calibrated ONNX text spotting unavailable ({modelAvailability.Code}).";
        var quality = RealtimeVisionQuality.Baseline(
            $"Profile {calibration.Id}; language {calibration.LanguageTag}; "
            + $"display {calibration.DisplayMode}; brightness threshold {calibration.BrightnessThreshold}. "
            + $"Material pickup ROI is {pickupRegion.Name}. "
            + modelDetail);
        return new RealtimeVisionReadout(
            damage,
            Array.Empty<VisibleCounterObservation>(),
            Array.Empty<VisibleProgressObservation>(),
            Array.Empty<VisibleBuffObservation>(),
            Array.Empty<VisibleMapMarkerObservation>(),
            evidenceScore,
            quality)
        {
            MaterialPickups = pickups
        };
    }

    private async Task<OcrRegionReadout> RecognizeRegionAsync(
        PixelFrame frame,
        VisionRegionDefinition region,
        VisionCalibrationProfile calibration,
        CancellationToken cancellationToken)
    {
        var extracted = VisionRegionPixels.ExtractBgra(
            frame,
            region.Bounds,
            checked((int)OcrEngine.MaxImageDimension));
        var thresholdedPixels = VisionRegionPixels.ApplyBrightnessThreshold(
            extracted.BgraPixels,
            calibration.BrightnessThreshold);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            thresholdedPixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            extracted.Width,
            extracted.Height,
            BitmapAlphaMode.Ignore);
        var result = await GetEngine(calibration.LanguageTag).RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();
        var lines = result.Lines.Select(line => new CombatOcrLine(
            line.Words.Select(word => new CombatOcrWord(
                word.Text,
                word.BoundingRect.X,
                word.BoundingRect.Y,
                word.BoundingRect.Width,
                word.BoundingRect.Height)).ToArray())).ToArray();
        return new OcrRegionReadout(extracted, lines);
    }

    private sealed record OcrRegionReadout(
        ExtractedPixelRegion Region,
        IReadOnlyList<CombatOcrLine> Lines);

    private OcrEngine GetEngine(string languageTag)
    {
        if (_engine is not null
            && string.Equals(_engineLanguageTag, languageTag, StringComparison.OrdinalIgnoreCase))
        {
            return _engine;
        }

        var language = new Language(languageTag);
        _engine = OcrEngine.TryCreateFromLanguage(language)
            ?? throw new InvalidOperationException(
                $"Windows OCR language '{languageTag}' is not installed.");
        _engineLanguageTag = languageTag;
        return _engine;
    }
}

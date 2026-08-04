using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using D4Hub.Core;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace D4Hub.App.Services;

/// <summary>
/// Offline PP-OCRv5 detector and recognizer backed by the reviewed local
/// PaddleSharp model package. Runtime downloads are never used.
/// </summary>
public sealed class PaddleCombatTextSpottingEngine : ICombatTextSpottingEngine
{
    private const string PackageVersion = "3.3.1";
    private const string PackageSource =
        "https://api.nuget.org/v3-flatcontainer/sdcb.paddleocr.models.local/3.3.1/sdcb.paddleocr.models.local.3.3.1.nupkg";
    private const string PackageSha256 =
        "182EA2ABF9A19FC3D8C9F51F30300409D5BD45A06E3BEFBF27DCA8585577CD71";

    private readonly PaddleOcrAll _ocr;

    public PaddleCombatTextSpottingEngine()
    {
        _ocr = new PaddleOcrAll(LocalFullModels.ChineseV5, PaddleDevice.Mkldnn())
        {
            AllowRotateDetection = false,
            Enable180Classification = false
        };
        Availability = BuildAvailability();
    }

    public CombatTextModelAvailability Availability { get; }

    public Task<CombatTextSpottingResult> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateCalibration(calibration);
        var combatRegion = calibration.Regions.Values.FirstOrDefault(region =>
            region.Kind == VisionRegionKind.CombatText)
            ?? throw new InvalidOperationException(
                $"Vision calibration '{calibration.Id}' does not define a combat-text region.");
        var extracted = VisionRegionPixels.ExtractBgra(frame, combatRegion.Bounds);
        return ReadExtractedRegionAsync(extracted, calibration, timeSeconds, cancellationToken);
    }

    public Task<CombatTextSpottingResult> ReadExtractedRegionAsync(
        ExtractedPixelRegion extracted,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(extracted);
        ValidateCalibration(calibration);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        using var bgra = Mat.FromPixelData(
            extracted.Height,
            extracted.Width,
            MatType.CV_8UC4,
            extracted.BgraPixels);
        using var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        var result = _ocr.Run(bgr);
        cancellationToken.ThrowIfCancellationRequested();

        var observations = new List<CombatTextObservation>();
        var rejectionReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var region in result.Regions)
        {
            var rawText = region.Text?.Trim() ?? string.Empty;
            var matches = CombatDamageTextParser.ParseMatches(rawText);
            if (!CombatTextRecognitionDomain.ContainsOnlyAllowedCharacters(rawText))
            {
                Reject(rejectionReasons, "outside-damage-character-domain");
                continue;
            }

            if (matches.Count != 1)
            {
                Reject(rejectionReasons, matches.Count == 0 ? "no-damage-value" : "ambiguous-damage-values");
                continue;
            }

            var match = matches[0];
            var assessment = CombatOcrObservationMapper.AssessDamageCandidate(match, 1, 1);
            var bounds = region.Rect.BoundingRect();
            var posterior = Math.Clamp(region.Score, 0, 1);
            var rejectionReason = posterior < calibration.MinimumOcrConfidence
                ? "below-profile-evidence-threshold"
                : assessment.RejectionReason;

            observations.Add(new CombatTextObservation(
                match.Damage,
                timeSeconds,
                extracted.SourceBounds.X
                    + ((bounds.Left + (bounds.Width / 2d)) * extracted.SourcePixelsPerOutputPixelX),
                extracted.SourceBounds.Y
                    + ((bounds.Top + (bounds.Height / 2d)) * extracted.SourcePixelsPerOutputPixelY),
                bounds.Width * extracted.SourcePixelsPerOutputPixelX,
                bounds.Height * extracted.SourcePixelsPerOutputPixelY,
                match.RawText,
                posterior,
                rejectionReason,
                posterior));
        }

        stopwatch.Stop();
        return Task.FromResult(new CombatTextSpottingResult(
            observations,
            result.Regions.Length,
            rejectionReasons.Values.Sum(),
            rejectionReasons,
            stopwatch.Elapsed));
    }

    public void Dispose() => _ocr.Dispose();

    private static CombatTextModelAvailability BuildAvailability()
    {
        var assembly = typeof(LocalFullModels).Assembly;
        var assemblyPath = assembly.Location;
        var actualHash = string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath)
            ? "embedded"
            : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        return new CombatTextModelAvailability(
            true,
            "runtime-loaded-unvalidated",
            $"PaddleOCR V5 local runtime {PackageVersion} constructed successfully; this is not a calibration result. "
            + $"Package source {PackageSource}; nupkg SHA-256 {PackageSha256}.",
            $"nuget:Sdcb.PaddleOCR.Models.Local/{PackageVersion}",
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["package"] = PackageSha256,
                ["loaded-assembly"] = actualHash
            });
    }

    private static void Reject(Dictionary<string, int> reasons, string reason) =>
        reasons[reason] = reasons.GetValueOrDefault(reason) + 1;

    private static void ValidateCalibration(VisionCalibrationProfile calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        if (!string.Equals(calibration.LanguageTag, "zh-CN", StringComparison.OrdinalIgnoreCase)
            || calibration.DisplayMode != VisionDisplayMode.StandardDynamicRange)
        {
            throw new InvalidOperationException(
                $"The bundled PaddleOCR model is not calibrated for {calibration.LanguageTag}/{calibration.DisplayMode}.");
        }
    }
}

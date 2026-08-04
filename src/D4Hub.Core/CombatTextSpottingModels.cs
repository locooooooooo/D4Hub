using System.Security.Cryptography;
using System.Text.Json;

namespace D4Hub.Core;

public static class CombatTextRecognitionDomain
{
    public const string AllowedCharacters = "0123456789.,，．万亿兆京";

    public static bool ContainsOnlyAllowedCharacters(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.All(character => char.IsWhiteSpace(character)
            || AllowedCharacters.Contains(character, StringComparison.Ordinal));
}

public sealed record CombatTextDetection(
    PixelRect Bounds,
    double DetectorScore);

public sealed record CombatTextRecognition(
    string RawText,
    long? Damage,
    double? SelectedCandidatePosterior,
    string? RejectionReason);

public sealed record CombatTextSpottingResult(
    IReadOnlyList<CombatTextObservation> Observations,
    int DetectedInstanceCount,
    int RejectedInstanceCount,
    IReadOnlyDictionary<string, int> RejectionReasons,
    TimeSpan ProcessingTime);

/// <summary>
/// Contract for an offline detector plus restricted recognizer. Implementors
/// must not download assets at runtime. They must validate their model bundle
/// before reporting themselves as available.
/// </summary>
public interface ICombatTextSpottingEngine : IDisposable
{
    CombatTextModelAvailability Availability { get; }

    Task<CombatTextSpottingResult> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default);
}

public sealed record CombatTextModelAsset(
    string Role,
    string File,
    string Sha256,
    string SourceUrl,
    string UpstreamVersion,
    string LicenseSpdx);

public sealed record CombatTextModelManifest(
    int SchemaVersion,
    string Pipeline,
    string LanguageTag,
    IReadOnlyList<string> SupportedCalibrationProfiles,
    string RecognitionCharacters,
    IReadOnlyList<CombatTextModelAsset> Assets);

public sealed record CombatTextModelAvailability(
    bool IsAvailable,
    string Code,
    string Detail,
    string ManifestPath,
    CombatTextModelManifest? Manifest,
    IReadOnlyDictionary<string, string> VerifiedAssets);

public static class CombatTextModelBundleValidator
{
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CombatTextModelAvailability Validate(
        string modelDirectory,
        VisionCalibrationProfile? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        var root = Path.GetFullPath(modelDirectory);
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return Unavailable(
                "manifest-missing",
                $"Offline combat text model manifest is missing: {manifestPath}",
                manifestPath);
        }

        CombatTextModelManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CombatTextModelManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return Unavailable(
                "manifest-invalid",
                $"Offline combat text model manifest cannot be read: {exception.Message}",
                manifestPath);
        }

        var manifestError = ValidateManifest(manifest, calibration);
        if (manifestError is not null)
        {
            return Unavailable("manifest-invalid", manifestError, manifestPath, manifest);
        }

        var verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest!.Assets)
        {
            var assetPath = Path.GetFullPath(Path.Combine(root, asset.File));
            if (!IsWithinDirectory(root, assetPath))
            {
                return Unavailable(
                    "asset-path-invalid",
                    $"Model asset '{asset.Role}' resolves outside the model directory.",
                    manifestPath,
                    manifest,
                    verified);
            }

            if (!File.Exists(assetPath))
            {
                return Unavailable(
                    "asset-missing",
                    $"Required offline model asset is missing: {asset.File}",
                    manifestPath,
                    manifest,
                    verified);
            }

            string actualHash;
            try
            {
                using var stream = File.OpenRead(assetPath);
                actualHash = Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (IOException exception)
            {
                return Unavailable(
                    "asset-unreadable",
                    $"Model asset '{asset.File}' cannot be read: {exception.Message}",
                    manifestPath,
                    manifest,
                    verified);
            }

            if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Unavailable(
                    "asset-hash-mismatch",
                    $"Model asset '{asset.File}' failed SHA-256 verification.",
                    manifestPath,
                    manifest,
                    verified);
            }

            verified[asset.Role] = actualHash;
        }

        return new CombatTextModelAvailability(
            true,
            "available",
            "Offline detector and restricted recognizer assets passed manifest and SHA-256 validation.",
            manifestPath,
            manifest,
            verified);
    }

    private static string? ValidateManifest(
        CombatTextModelManifest? manifest,
        VisionCalibrationProfile? calibration)
    {
        if (manifest is null)
        {
            return "Offline combat text model manifest is empty.";
        }

        if (manifest.SchemaVersion != 1)
        {
            return $"Unsupported combat text model manifest schema {manifest.SchemaVersion}.";
        }

        if (string.IsNullOrWhiteSpace(manifest.Pipeline)
            || string.IsNullOrWhiteSpace(manifest.LanguageTag))
        {
            return "Pipeline and languageTag are required.";
        }

        if (!string.Equals(
                manifest.RecognitionCharacters,
                CombatTextRecognitionDomain.AllowedCharacters,
                StringComparison.Ordinal))
        {
            return "The recognizer character domain does not exactly match the D4 damage domain.";
        }

        if (manifest.Assets is null
            || manifest.Assets.Count != 2
            || !manifest.Assets.Any(asset => string.Equals(asset.Role, "detector", StringComparison.OrdinalIgnoreCase))
            || !manifest.Assets.Any(asset => string.Equals(asset.Role, "recognizer", StringComparison.OrdinalIgnoreCase)))
        {
            return "Exactly one detector and one recognizer asset are required.";
        }

        foreach (var asset in manifest.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.File)
                || string.IsNullOrWhiteSpace(asset.SourceUrl)
                || string.IsNullOrWhiteSpace(asset.UpstreamVersion)
                || string.IsNullOrWhiteSpace(asset.LicenseSpdx)
                || asset.Sha256.Length != 64
                || asset.Sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                return $"Asset metadata for '{asset.Role}' is incomplete.";
            }
        }

        if (calibration is not null
            && (!string.Equals(manifest.LanguageTag, calibration.LanguageTag, StringComparison.OrdinalIgnoreCase)
                || manifest.SupportedCalibrationProfiles is null
                || !manifest.SupportedCalibrationProfiles.Contains(calibration.Id, StringComparer.Ordinal)))
        {
            return $"The model bundle does not support calibration '{calibration.Id}'.";
        }

        return null;
    }

    private static bool IsWithinDirectory(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static CombatTextModelAvailability Unavailable(
        string code,
        string detail,
        string manifestPath,
        CombatTextModelManifest? manifest = null,
        IReadOnlyDictionary<string, string>? verified = null) => new(
            false,
            code,
            detail,
            manifestPath,
            manifest,
            verified ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

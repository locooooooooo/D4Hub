using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4Hub.Core;

public sealed record LocalUpdateValidationContext(
    string Product,
    string Channel,
    string Architecture,
    string CurrentVersion,
    string ArtifactDirectory);

public sealed record LocalUpdateArtifact(string FileName, long Size, string Sha256);

public sealed record LocalUpdateManifest(
    int SchemaVersion,
    string Product,
    string Version,
    string Channel,
    string Architecture,
    LocalUpdateArtifact Artifact);

public enum LocalUpdateRejectionCode
{
    None,
    MalformedJson,
    InvalidManifestShape,
    MissingRequiredField,
    UnsupportedSchemaVersion,
    ProductMismatch,
    ChannelMismatch,
    ArchitectureMismatch,
    InvalidVersion,
    VersionNotNewer,
    UnsafeArtifactFileName,
    InvalidArtifactSize,
    InvalidArtifactSha256,
    ArtifactNotFound,
    ArtifactUnreadable,
    ArtifactSizeMismatch,
    ArtifactSha256Mismatch
}

public sealed record LocalUpdateValidationResult(
    bool IsAccepted,
    LocalUpdateRejectionCode RejectionCode,
    string Message,
    LocalUpdateManifest? Manifest)
{
    internal static LocalUpdateValidationResult Accept(LocalUpdateManifest manifest) =>
        new(true, LocalUpdateRejectionCode.None, "Local update artifact matches the manifest.", manifest);

    internal static LocalUpdateValidationResult Reject(
        LocalUpdateRejectionCode code,
        string message,
        LocalUpdateManifest? manifest = null) =>
        new(false, code, message, manifest);
}

public sealed class LocalUpdateManifestValidator
{
    private const int SupportedSchemaVersion = 1;

    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "product",
        "version",
        "channel",
        "architecture",
        "artifact"
    ];

    private static readonly string[] ArtifactProperties = ["fileName", "size", "sha256"];

    private static readonly Regex StrictVersionPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedWindowsFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    public LocalUpdateValidationResult Validate(string manifestJson, LocalUpdateValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateContext(context);

        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return LocalUpdateValidationResult.Reject(
                LocalUpdateRejectionCode.MalformedJson,
                "Manifest JSON is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifestJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
        }
        catch (JsonException exception)
        {
            return LocalUpdateValidationResult.Reject(
                LocalUpdateRejectionCode.MalformedJson,
                $"Manifest JSON is malformed: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.InvalidManifestShape,
                    "Manifest root must be an object.");
            }

            if (!HasOnlyUniqueProperties(root, RootProperties, out var rootShapeError))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.InvalidManifestShape,
                    rootShapeError!);
            }

            if (!TryGetRequiredProperty(root, "schemaVersion", out var schemaVersionElement)
                || !TryGetRequiredProperty(root, "product", out var productElement)
                || !TryGetRequiredProperty(root, "version", out var versionElement)
                || !TryGetRequiredProperty(root, "channel", out var channelElement)
                || !TryGetRequiredProperty(root, "architecture", out var architectureElement)
                || !TryGetRequiredProperty(root, "artifact", out var artifactElement))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.MissingRequiredField,
                    "Manifest is missing one or more required fields.");
            }

            if (schemaVersionElement.ValueKind != JsonValueKind.Number
                || !schemaVersionElement.TryGetInt32(out var schemaVersion)
                || !TryGetNonEmptyString(productElement, out var product)
                || !TryGetNonEmptyString(versionElement, out var version)
                || !TryGetNonEmptyString(channelElement, out var channel)
                || !TryGetNonEmptyString(architectureElement, out var architecture)
                || artifactElement.ValueKind != JsonValueKind.Object)
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.InvalidManifestShape,
                    "Manifest fields have invalid JSON types or empty values.");
            }

            if (!HasOnlyUniqueProperties(artifactElement, ArtifactProperties, out var artifactShapeError))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.InvalidManifestShape,
                    artifactShapeError!);
            }

            if (!TryGetRequiredProperty(artifactElement, "fileName", out var fileNameElement)
                || !TryGetRequiredProperty(artifactElement, "size", out var sizeElement)
                || !TryGetRequiredProperty(artifactElement, "sha256", out var sha256Element))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.MissingRequiredField,
                    "Artifact is missing fileName, size, or sha256.");
            }

            if (!TryGetNonEmptyString(fileNameElement, out var fileName)
                || sizeElement.ValueKind != JsonValueKind.Number
                || !sizeElement.TryGetInt64(out var size)
                || !TryGetNonEmptyString(sha256Element, out var sha256))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.InvalidManifestShape,
                    "Artifact fields have invalid JSON types or empty values.");
            }

            var manifest = new LocalUpdateManifest(
                schemaVersion,
                product,
                version,
                channel,
                architecture,
                new LocalUpdateArtifact(fileName, size, sha256));

            if (schemaVersion != SupportedSchemaVersion)
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.UnsupportedSchemaVersion,
                    $"Unsupported schema version: {schemaVersion}.",
                    manifest);
            }

            if (!string.Equals(product, context.Product, StringComparison.Ordinal))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.ProductMismatch,
                    $"Manifest product '{product}' does not match '{context.Product}'.",
                    manifest);
            }

            if (!string.Equals(channel, context.Channel, StringComparison.Ordinal))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.ChannelMismatch,
                    $"Manifest channel '{channel}' does not match '{context.Channel}'.",
                    manifest);
            }

            if (!string.Equals(architecture, context.Architecture, StringComparison.Ordinal))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.ArchitectureMismatch,
                    $"Manifest architecture '{architecture}' does not match '{context.Architecture}'.",
                    manifest);
            }

            if (!TryParseStrictVersion(version, out var candidateVersion))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.InvalidVersion,
                    $"Manifest version '{version}' must use major.minor.patch numeric form.",
                    manifest);
            }

            var currentVersion = ParseContextVersion(context.CurrentVersion);
            if (candidateVersion <= currentVersion)
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.VersionNotNewer,
                    $"Manifest version '{version}' is not newer than '{context.CurrentVersion}'.",
                    manifest);
            }

            if (!IsSafeArtifactFileName(fileName))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.UnsafeArtifactFileName,
                    "Artifact fileName must be one safe relative file name without path segments.",
                    manifest);
            }

            if (size <= 0)
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.InvalidArtifactSize,
                    "Artifact size must be a positive integer.",
                    manifest);
            }

            if (!IsSha256(sha256))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.InvalidArtifactSha256,
                    "Artifact sha256 must contain exactly 64 hexadecimal characters.",
                    manifest);
            }

            var artifactDirectory = Path.GetFullPath(context.ArtifactDirectory);
            var artifactPath = Path.GetFullPath(Path.Combine(artifactDirectory, fileName));
            if (!string.Equals(Path.GetDirectoryName(artifactPath), artifactDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.UnsafeArtifactFileName,
                    "Artifact fileName escapes the artifact directory.",
                    manifest);
            }

            if (!File.Exists(artifactPath))
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.ArtifactNotFound,
                    $"Artifact does not exist: {fileName}.",
                    manifest);
            }

            try
            {
                using var stream = new FileStream(
                    artifactPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.SequentialScan);

                if (stream.Length != size)
                {
                    return LocalUpdateValidationResult.Reject(
                        LocalUpdateRejectionCode.ArtifactSizeMismatch,
                        $"Artifact size {stream.Length} does not match manifest size {size}.",
                        manifest);
                }

                var actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
                if (!string.Equals(actualSha256, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return LocalUpdateValidationResult.Reject(
                        LocalUpdateRejectionCode.ArtifactSha256Mismatch,
                        "Artifact SHA-256 does not match the manifest.",
                        manifest);
                }
            }
            catch (IOException exception)
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.ArtifactUnreadable,
                    $"Artifact cannot be read: {exception.Message}",
                    manifest);
            }
            catch (UnauthorizedAccessException exception)
            {
                return LocalUpdateValidationResult.Reject(
                    LocalUpdateRejectionCode.ArtifactUnreadable,
                    $"Artifact cannot be read: {exception.Message}",
                    manifest);
            }

            return LocalUpdateValidationResult.Accept(manifest);
        }
    }

    private static void ValidateContext(LocalUpdateValidationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Product);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Architecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.CurrentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ArtifactDirectory);
        _ = ParseContextVersion(context.CurrentVersion);
    }

    private static Version ParseContextVersion(string version)
    {
        if (!TryParseStrictVersion(version, out var parsed))
        {
            throw new ArgumentException(
                "Current version must use major.minor.patch numeric form.",
                nameof(version));
        }

        return parsed;
    }

    private static bool TryParseStrictVersion(string value, out Version version)
    {
        version = new Version();
        return StrictVersionPattern.IsMatch(value) && Version.TryParse(value, out version!);
    }

    private static bool TryGetRequiredProperty(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value);

    private static bool TryGetNonEmptyString(JsonElement element, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = element.GetString();
        if (string.IsNullOrWhiteSpace(candidate) || !string.Equals(candidate, candidate.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool HasOnlyUniqueProperties(
        JsonElement element,
        IEnumerable<string> allowedProperties,
        out string? error)
    {
        error = null;
        var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                error = $"Unexpected manifest property: {property.Name}.";
                return false;
            }

            if (!seen.Add(property.Name))
            {
                error = $"Duplicate manifest property: {property.Name}.";
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeArtifactFileName(string fileName)
    {
        if (fileName.Length == 0
            || !string.Equals(fileName, fileName.Trim(), StringComparison.Ordinal)
            || fileName.EndsWith(".", StringComparison.Ordinal)
            || fileName.Contains("..", StringComparison.Ordinal)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || Path.IsPathRooted(fileName)
            || Path.GetFileName(fileName) != fileName
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var firstExtensionSeparator = fileName.IndexOf('.');
        var deviceName = firstExtensionSeparator < 0 ? fileName : fileName[..firstExtensionSeparator];
        return !ReservedWindowsFileNames.Contains(deviceName);
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}

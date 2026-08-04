using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4Hub.Core;

public sealed class ExternalResourceCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly HashSet<string> AllowedCategories =
        new(StringComparer.Ordinal) { "map-tools" };

    private static readonly HashSet<string> AllowedOfficialStatuses =
        new(StringComparer.Ordinal) { "official", "community", "commercial-third-party" };

    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.Ordinal) { "active", "degraded", "delisted" };

    public int SchemaVersion { get; init; }
    public string CatalogVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public List<ExternalResourceEntry> Entries { get; init; } = [];

    public static ExternalResourceCatalog LoadStrict(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path);
        var catalog = JsonSerializer.Deserialize<ExternalResourceCatalog>(json, JsonOptions)
            ?? throw new InvalidDataException("External resource catalog cannot be null.");
        catalog.EnsureValid();
        return catalog;
    }

    public void EnsureValid()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported external resource catalog schema: {SchemaVersion}.");
        }

        RequireText(CatalogVersion, nameof(CatalogVersion), 64);
        if (GeneratedAt == default)
        {
            throw new InvalidDataException("External resource catalog requires generatedAt.");
        }

        if (Entries.Count == 0)
        {
            throw new InvalidDataException("External resource catalog must contain at least one entry.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            ValidateEntry(entry);
            if (!ids.Add(entry.ResourceId))
            {
                throw new InvalidDataException($"Duplicate external resource id: {entry.ResourceId}.");
            }
        }
    }

    internal static Uri ValidateEntry(ExternalResourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        RequireToken(entry.ResourceId, nameof(entry.ResourceId));
        RequireToken(entry.GameId, nameof(entry.GameId));
        RequireToken(entry.ProviderId, nameof(entry.ProviderId));
        RequireAllowed(entry.Category, nameof(entry.Category), AllowedCategories);
        RequireText(entry.DisplayName, nameof(entry.DisplayName), 80);
        RequireText(entry.Description, nameof(entry.Description), 240);
        RequireAllowed(entry.OfficialStatus, nameof(entry.OfficialStatus), AllowedOfficialStatuses);
        RequireAllowed(entry.Status, nameof(entry.Status), AllowedStatuses);
        RequireText(entry.ReviewedBy, nameof(entry.ReviewedBy), 80);
        RequireText(entry.ReviewMethod, nameof(entry.ReviewMethod), 160);
        RequireText(entry.RiskNotes, nameof(entry.RiskNotes), 320);
        RequireToken(entry.DisclaimerKey, nameof(entry.DisclaimerKey));
        RequireText(entry.Attribution, nameof(entry.Attribution), 160);

        if (entry.ReviewedAt == default)
        {
            throw new InvalidDataException($"External resource {entry.ResourceId} requires reviewedAt.");
        }

        RequireTokenList(entry.Locales, nameof(entry.Locales));
        RequireTokenList(entry.Regions, nameof(entry.Regions));
        if (entry.AllowedHosts.Count == 0)
        {
            throw new InvalidDataException($"External resource {entry.ResourceId} requires allowedHosts.");
        }

        var allowedHosts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var allowedHost in entry.AllowedHosts)
        {
            var normalizedHost = NormalizeDnsHost(allowedHost, nameof(entry.AllowedHosts));
            if (!allowedHosts.Add(normalizedHost))
            {
                throw new InvalidDataException($"External resource {entry.ResourceId} contains duplicate allowedHosts.");
            }
        }

        if (!Uri.TryCreate(entry.CanonicalUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || uri.HostNameType != UriHostNameType.Dns
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException($"External resource {entry.ResourceId} has an unsafe canonicalUrl.");
        }

        var canonicalHost = NormalizeDnsHost(uri.IdnHost, nameof(entry.CanonicalUrl));
        if (!allowedHosts.Contains(canonicalHost))
        {
            throw new InvalidDataException($"External resource {entry.ResourceId} canonical host is not allowlisted.");
        }

        return uri;
    }

    private static string NormalizeDnsHost(string value, string fieldName)
    {
        RequireText(value, fieldName, 253);
        var trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal)
            || trimmed.EndsWith(".", StringComparison.Ordinal)
            || IPAddress.TryParse(trimmed, out _))
        {
            throw new InvalidDataException($"External resource field {fieldName} must be an exact DNS host.");
        }

        var hostType = Uri.CheckHostName(trimmed);
        if (hostType != UriHostNameType.Dns)
        {
            throw new InvalidDataException($"External resource field {fieldName} must be a DNS host.");
        }

        return new IdnMapping().GetAscii(trimmed).ToLowerInvariant();
    }

    private static void RequireTokenList(IReadOnlyCollection<string> values, string fieldName)
    {
        if (values.Count == 0)
        {
            throw new InvalidDataException($"External resource field {fieldName} cannot be empty.");
        }

        foreach (var value in values)
        {
            RequireToken(value, fieldName);
        }
    }

    private static void RequireAllowed(string value, string fieldName, HashSet<string> allowed)
    {
        if (!allowed.Contains(value))
        {
            throw new InvalidDataException($"External resource field {fieldName} has an unsupported value.");
        }
    }

    private static void RequireToken(string value, string fieldName)
    {
        RequireText(value, fieldName, 80);
        if (value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_')))
        {
            throw new InvalidDataException($"External resource field {fieldName} must be an ASCII token.");
        }
    }

    private static void RequireText(string value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > maximumLength)
        {
            throw new InvalidDataException($"External resource field {fieldName} is missing or invalid.");
        }
    }
}

public sealed class ExternalResourceEntry
{
    public string ResourceId { get; init; } = string.Empty;
    public string GameId { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CanonicalUrl { get; init; } = string.Empty;
    public List<string> AllowedHosts { get; init; } = [];
    public List<string> Locales { get; init; } = [];
    public List<string> Regions { get; init; } = [];
    public string OfficialStatus { get; init; } = string.Empty;
    public DateTimeOffset ReviewedAt { get; init; }
    public string ReviewedBy { get; init; } = string.Empty;
    public string ReviewMethod { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RiskNotes { get; init; } = string.Empty;
    public string DisclaimerKey { get; init; } = string.Empty;
    public string Attribution { get; init; } = string.Empty;

    [JsonIgnore]
    public string HostName => GetLaunchUri().IdnHost;

    [JsonIgnore]
    public string ReviewLabel => $"人工复核 {ReviewedAt:yyyy-MM-dd}";

    public Uri GetLaunchUri() => ExternalResourceCatalog.ValidateEntry(this);
}

using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace D4Hub.Core;

public sealed record D2CoreBuildReference(string BuildId, int VariantIndex)
{
    public string LibraryKey => $"d2core:{BuildId}";
    public int VariantNumber => VariantIndex + 1;
    public string CanonicalUrl => $"https://www.d2core.com/d4/planner?bd={Uri.EscapeDataString(BuildId)}&var={VariantNumber}";
}

public static class D2CoreBuildUrl
{
    private static readonly Regex BuildIdPattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

    public static D2CoreBuildReference Parse(string input)
    {
        if (!TryParse(input, out var reference, out var error))
        {
            throw new FormatException(error);
        }

        return reference!;
    }

    public static bool TryParse(string? input, out D2CoreBuildReference? reference, out string error)
    {
        reference = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "请粘贴暗黑核 BD 链接。";
            return false;
        }

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || (uri.Host != "www.d2core.com" && uri.Host != "d2core.com")
            || !uri.AbsolutePath.TrimEnd('/').Equals("/d4/planner", StringComparison.OrdinalIgnoreCase))
        {
            error = "仅支持 https://www.d2core.com/d4/planner 链接。";
            return false;
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("bd", out var buildId) || !BuildIdPattern.IsMatch(buildId))
        {
            error = "链接缺少有效的 bd 参数。";
            return false;
        }

        var variantNumber = 1;
        if (query.TryGetValue("var", out var variantText)
            && (!int.TryParse(variantText, NumberStyles.None, CultureInfo.InvariantCulture, out variantNumber)
                || variantNumber < 1
                || variantNumber > 100))
        {
            error = "链接中的 var 参数无效。";
            return false;
        }

        reference = new D2CoreBuildReference(buildId, variantNumber - 1);
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
            result[key] = value;
        }

        return result;
    }
}

public sealed class PublicBuildRecord
{
    public int SchemaVersion { get; set; } = 1;
    public string Source { get; set; } = "d2core";
    public string BuildId { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int Season { get; set; }
    public string ParserVersion { get; set; } = D2CoreBuildParser.ParserVersion;
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BuildVariantRecord> Variants { get; set; } = new();
}

public sealed class BuildVariantRecord
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<EquipmentItemRecord> Equipment { get; set; } = new();
}

public sealed class EquipmentItemRecord
{
    public int SourceSlot { get; set; }
    public string SourceItemType { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AspectName { get; set; } = string.Empty;
    public int ItemPower { get; set; }
    public bool IsMythic { get; set; }
    public List<ItemAffixRecord> Affixes { get; set; } = new();
}

public sealed class ItemAffixRecord
{
    public string SourceKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public double Value { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public bool IsGreater { get; set; }
    public bool IsTempered { get; set; }
    public int CriticalUpgradeLevel { get; set; }
}

public sealed class BuildLibraryIndex
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BuildLibraryIndexEntry> Builds { get; set; } = new();
}

public sealed class BuildLibraryIndexEntry
{
    public string Source { get; set; } = string.Empty;
    public string BuildId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int Season { get; set; }
    public int VariantCount { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public string Path { get; set; } = string.Empty;
}

public sealed class D2CoreAffixDefinition
{
    public string Key { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string DisplayTemplate { get; init; } = string.Empty;
    public bool IsTempered { get; init; }
    public IReadOnlyList<D2CoreAffixRange> Ranges { get; init; } = Array.Empty<D2CoreAffixRange>();
}

public sealed record D2CoreAffixRange(int ItemPower, double Minimum, double Maximum);

public sealed class D2CoreAffixCatalog
{
    private static readonly Regex ValueTokenPattern = new(
        @"\{VALUE\d*\}(?<operation>[*/]\s*\d+(?:\.\d+)?)?(?:\|(?<format>[^|]*)\|)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ValueRangePattern = new(@"[+xX-]?\[[^\]]+\]\s*%?", RegexOptions.Compiled);
    private static readonly Regex RenderedValueBrackets = new(
        @"\[(?<value>[+-]?[\d,.]+(?:\s*-\s*[\d,.]+)?%?(?:\[x\])?)\]",
        RegexOptions.Compiled);
    private readonly IReadOnlyDictionary<string, D2CoreAffixDefinition> _definitions;

    private D2CoreAffixCatalog(IReadOnlyDictionary<string, D2CoreAffixDefinition> definitions)
    {
        _definitions = definitions;
    }

    public static D2CoreAffixCatalog FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var definitions = new Dictionary<string, D2CoreAffixDefinition>(StringComparer.Ordinal);
        foreach (var affix in root.GetProperty("affix").EnumerateArray())
        {
            var key = GetString(affix, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var ranges = new List<D2CoreAffixRange>();
            if (affix.TryGetProperty("effectList", out var effectList) && effectList.ValueKind == JsonValueKind.Array)
            {
                foreach (var range in effectList.EnumerateArray())
                {
                    ranges.Add(new D2CoreAffixRange(
                        GetInt32(range, "ipower"),
                        GetDouble(range, "min"),
                        GetDouble(range, "max")));
                }
            }

            definitions[key] = new D2CoreAffixDefinition
            {
                Key = key,
                Description = GetString(affix, "desc"),
                DisplayTemplate = GetString(affix, "descTpl"),
                IsTempered = GetBoolean(affix, "tempered"),
                Ranges = ranges
            };
        }

        return new D2CoreAffixCatalog(definitions);
    }

    public ItemAffixRecord Format(JsonElement modifier, int itemPower)
    {
        var key = GetString(modifier, "name");
        var value = GetDouble(modifier, "value");
        var isGreater = GetBoolean(modifier, "greater");
        var criticalUpgradeLevel = GetInt32(modifier, "critLevel");
        if (!_definitions.TryGetValue(key, out var definition))
        {
            return new ItemAffixRecord
            {
                SourceKey = key,
                Name = key,
                DisplayText = PrefixRollType(key, isGreater, false),
                Value = value,
                IsGreater = isGreater,
                CriticalUpgradeLevel = criticalUpgradeLevel
            };
        }

        var range = definition.Ranges.FirstOrDefault(candidate => candidate.ItemPower <= itemPower)
            ?? definition.Ranges.FirstOrDefault();
        var rendered = RenderValue(definition.DisplayTemplate, definition.Description, value);
        var name = ValueRangePattern.Replace(definition.Description, string.Empty)
            .Replace("[x]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '+', '-', 'x', 'X');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = key;
        }

        return new ItemAffixRecord
        {
            SourceKey = key,
            Name = name,
            DisplayText = PrefixRollType(rendered, isGreater, definition.IsTempered),
            Value = value,
            Minimum = range?.Minimum,
            Maximum = range?.Maximum,
            IsGreater = isGreater,
            IsTempered = definition.IsTempered,
            CriticalUpgradeLevel = criticalUpgradeLevel
        };
    }

    private static string RenderValue(string template, string fallback, double value)
    {
        if (string.IsNullOrWhiteSpace(template) || !ValueTokenPattern.IsMatch(template))
        {
            return fallback;
        }

        var rendered = ValueTokenPattern.Replace(template, match =>
        {
            var displayValue = value;
            var operation = match.Groups["operation"].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (operation.StartsWith('*')
                && double.TryParse(operation[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier))
            {
                displayValue *= multiplier;
            }
            else if (operation.StartsWith('/')
                     && double.TryParse(operation[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var divisor)
                     && divisor != 0)
            {
                displayValue /= divisor;
            }

            var format = match.Groups["format"].Value;
            var numberFormat = format.Contains("1%", StringComparison.Ordinal) ? "#,##0.0" : "#,##0.##";
            var suffix = format.Contains('%') ? "%" : string.Empty;
            if (format.EndsWith('x'))
            {
                suffix += "[x]";
            }

            return displayValue.ToString(numberFormat, CultureInfo.InvariantCulture) + suffix;
        });

        return RenderedValueBrackets.Replace(rendered, "${value}").Trim();
    }

    private static string PrefixRollType(string text, bool isGreater, bool isTempered)
    {
        var prefix = isGreater && isTempered ? "太古回火" : isGreater ? "太古" : isTempered ? "回火" : string.Empty;
        return string.IsNullOrEmpty(prefix) ? text : $"{prefix} · {text}";
    }

    internal static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    internal static int GetInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number) ? number : 0;

    internal static double GetDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var number) ? number : 0;

    internal static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
}

public static class D2CoreBuildParser
{
    public const string ParserVersion = "d2core-v1";

    public static PublicBuildRecord Parse(
        string cloudResponseJson,
        D2CoreBuildReference reference,
        D2CoreAffixCatalog affixCatalog,
        DateTimeOffset fetchedAt)
    {
        using var outer = JsonDocument.Parse(cloudResponseJson);
        var responseData = outer.RootElement.GetProperty("data").GetProperty("response_data").GetString()
            ?? throw new InvalidDataException("暗黑核响应缺少 response_data。");
        using var response = JsonDocument.Parse(responseData);
        if (!response.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("暗黑核没有返回 BD 数据。");
        }

        var variants = new List<BuildVariantRecord>();
        if (data.TryGetProperty("variants", out var sourceVariants) && sourceVariants.ValueKind == JsonValueKind.Array)
        {
            var variantIndex = 0;
            foreach (var variant in sourceVariants.EnumerateArray())
            {
                variants.Add(ParseVariant(variant, variantIndex++, affixCatalog));
            }
        }

        var buildId = D2CoreAffixCatalog.GetString(data, "_id");
        if (string.IsNullOrWhiteSpace(buildId))
        {
            buildId = reference.BuildId;
        }

        var record = new PublicBuildRecord
        {
            Source = "d2core",
            BuildId = buildId,
            CanonicalUrl = $"https://www.d2core.com/d4/planner?bd={Uri.EscapeDataString(buildId)}",
            Title = D2CoreAffixCatalog.GetString(data, "title"),
            ClassName = D2CoreAffixCatalog.GetString(data, "char"),
            Season = D2CoreAffixCatalog.GetInt32(data, "season"),
            SourceUpdatedAt = GetUnixTime(data, "_updateTime"),
            FetchedAt = fetchedAt,
            Variants = variants
        };
        record.ContentHash = ComputeContentHash(record);
        return record;
    }

    private static BuildVariantRecord ParseVariant(JsonElement variant, int index, D2CoreAffixCatalog affixCatalog)
    {
        var equipment = new List<EquipmentItemRecord>();
        if (variant.TryGetProperty("gear", out var gear) && gear.ValueKind == JsonValueKind.Object)
        {
            foreach (var slot in gear.EnumerateObject().OrderBy(property => ParseSlot(property.Name)))
            {
                if (slot.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                equipment.Add(ParseEquipment(ParseSlot(slot.Name), slot.Value, affixCatalog));
            }
        }

        return new BuildVariantRecord
        {
            Index = index,
            Name = D2CoreAffixCatalog.GetString(variant, "name"),
            Equipment = equipment
        };
    }

    private static EquipmentItemRecord ParseEquipment(int sourceSlot, JsonElement item, D2CoreAffixCatalog affixCatalog)
    {
        var itemPower = D2CoreAffixCatalog.GetInt32(item, "itemPower");
        var affixes = new List<ItemAffixRecord>();
        if (item.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var modifier in mods.EnumerateArray())
            {
                affixes.Add(affixCatalog.Format(modifier, itemPower));
            }
        }

        var name = D2CoreAffixCatalog.GetString(item, "name");
        var sourceKey = D2CoreAffixCatalog.GetString(item, "key");
        var itemType = D2CoreAffixCatalog.GetString(item, "itemType");
        var sourceType = D2CoreAffixCatalog.GetString(item, "type");
        return new EquipmentItemRecord
        {
            SourceSlot = sourceSlot,
            SourceItemType = itemType,
            SourceKey = sourceKey,
            DisplayName = string.IsNullOrWhiteSpace(name) ? sourceKey : name,
            AspectName = sourceType == "legendary" ? name : D2CoreAffixCatalog.GetString(item, "transfiguredAspectName"),
            ItemPower = itemPower,
            IsMythic = D2CoreAffixCatalog.GetBoolean(item, "mythic"),
            Affixes = affixes
        };
    }

    private static int ParseSlot(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var slot) ? slot : int.MaxValue;

    private static DateTimeOffset GetUnixTime(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UnixEpoch;
    }

    private static string ComputeContentHash(PublicBuildRecord record)
    {
        var canonicalJson = JsonSerializer.Serialize(new
        {
            record.Source,
            record.BuildId,
            record.Title,
            record.ClassName,
            record.Season,
            record.SourceUpdatedAt,
            record.Variants
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }
}

public interface ID2CoreBuildClient
{
    Task<PublicBuildRecord> FetchAsync(D2CoreBuildReference reference, CancellationToken cancellationToken = default);
}

public sealed class D2CoreCloudBuildClient : ID2CoreBuildClient
{
    private const string EnvironmentId = "diablocore-4gkv4qjs9c6a0b40";
    private const string AppSign = "diablocore";
    private const string PublicWebSdkKey = "ed6fe96e6ca08acf392d360094a58477";
    private const string DatabaseVersion = "72698";
    private readonly HttpClient _httpClient;
    private readonly string _affixCatalogPath;

    public D2CoreCloudBuildClient(HttpClient httpClient, string affixCatalogPath)
    {
        _httpClient = httpClient;
        _affixCatalogPath = Path.GetFullPath(affixCatalogPath);
    }

    public async Task<PublicBuildRecord> FetchAsync(
        D2CoreBuildReference reference,
        CancellationToken cancellationToken = default)
    {
        var affixCatalogTask = LoadAffixCatalogAsync(cancellationToken);
        using var request = CreateBuildRequest(reference);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var catalog = await affixCatalogTask.ConfigureAwait(false);
        return D2CoreBuildParser.Parse(responseJson, reference, catalog, DateTimeOffset.UtcNow);
    }

    private async Task<D2CoreAffixCatalog> LoadAffixCatalogAsync(CancellationToken cancellationToken)
    {
        string json;
        if (File.Exists(_affixCatalogPath))
        {
            json = await File.ReadAllTextAsync(_affixCatalogPath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var url = $"https://cloudstorage.d2core.com/data/d4/{DatabaseVersion}/affix_zhCN.json?env=prod&v=8";
            json = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            await WriteAtomicAsync(_affixCatalogPath, json, cancellationToken).ConfigureAwait(false);
        }

        return D2CoreAffixCatalog.FromJson(json);
    }

    private static HttpRequestMessage CreateBuildRequest(D2CoreBuildReference reference)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sequenceId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..14];
        var appSource = CreateAppSource(timestamp);
        var requestData = JsonSerializer.Serialize(new
        {
            bd = reference.BuildId,
            enableVariant = true,
            token = string.Empty
        });
        var payload = new
        {
            action = "functions.invokeFunction",
            dataVersion = "2020-01-10",
            env = EnvironmentId,
            function_name = "function-planner-queryplandetail",
            request_data = requestData,
            seqId = sequenceId
        };

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://tcb-api.tencentcloudapi.com/web?env={EnvironmentId}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("X-SDK-Version", "@cloudbase/js-sdk/1.7.2");
        request.Headers.TryAddWithoutValidation("x-seqid", sequenceId);
        request.Headers.TryAddWithoutValidation("X-TCB-App-Source", appSource);
        request.Headers.Referrer = new Uri("https://www.d2core.com/");
        return request;
    }

    private static string CreateAppSource(long timestamp)
    {
        const string headerJson = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var payloadJson = JsonSerializer.Serialize(new
        {
            data = new { },
            timestamp,
            appAccessKeyId = 1,
            appSign = AppSign
        });
        var unsignedToken = $"{ToBase64Url(Encoding.UTF8.GetBytes(headerJson))}.{ToBase64Url(Encoding.UTF8.GetBytes(payloadJson))}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(PublicWebSdkKey));
        var signature = ToBase64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken)));
        return $"timestamp={timestamp};appAccessKeyId=1;appSign={AppSign};sign={unsignedToken}.{signature}";
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task WriteAtomicAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("缓存路径缺少父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, contents, cancellationToken).ConfigureAwait(false);
        try
        {
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

public sealed class FileBuildLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly string _root;
    private readonly bool _isReadOnly;

    public FileBuildLibraryStore(string root, bool isReadOnly = false)
    {
        _root = Path.GetFullPath(root);
        _isReadOnly = isReadOnly;
    }

    public bool TryLoad(string source, string buildId, out PublicBuildRecord? record)
    {
        record = null;
        var path = GetRecordPath(source, buildId);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            record = JsonSerializer.Deserialize<PublicBuildRecord>(File.ReadAllText(path), JsonOptions);
            return record is not null && record.SchemaVersion == 1 && record.BuildId == buildId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Save(PublicBuildRecord record)
    {
        if (_isReadOnly)
        {
            throw new InvalidOperationException("公共 BD 库是只读的。");
        }

        var recordPath = GetRecordPath(record.Source, record.BuildId);
        WriteAtomic(recordPath, JsonSerializer.Serialize(record, JsonOptions));

        var indexPath = Path.Combine(_root, "index.json");
        var index = LoadIndex(indexPath);
        index.Builds.RemoveAll(entry => entry.Source == record.Source && entry.BuildId == record.BuildId);
        index.Builds.Add(new BuildLibraryIndexEntry
        {
            Source = record.Source,
            BuildId = record.BuildId,
            Title = record.Title,
            ClassName = record.ClassName,
            Season = record.Season,
            VariantCount = record.Variants.Count,
            ContentHash = record.ContentHash,
            SourceUpdatedAt = record.SourceUpdatedAt,
            Path = $"{record.Source}/{record.BuildId}.json"
        });
        index.Builds = index.Builds.OrderBy(entry => entry.Source).ThenBy(entry => entry.BuildId).ToList();
        index.UpdatedAt = DateTimeOffset.UtcNow;
        WriteAtomic(indexPath, JsonSerializer.Serialize(index, JsonOptions));
    }

    private string GetRecordPath(string source, string buildId)
    {
        if (!source.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            || !buildId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new InvalidDataException("BD 库键包含非法字符。");
        }

        return Path.Combine(_root, source, $"{buildId}.json");
    }

    private static BuildLibraryIndex LoadIndex(string path)
    {
        if (!File.Exists(path))
        {
            return new BuildLibraryIndex();
        }

        try
        {
            return JsonSerializer.Deserialize<BuildLibraryIndex>(File.ReadAllText(path), JsonOptions)
                ?? new BuildLibraryIndex();
        }
        catch (JsonException)
        {
            return new BuildLibraryIndex();
        }
    }

    private static void WriteAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("BD 库路径缺少父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, contents);
        try
        {
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

public enum BuildResolutionOrigin
{
    PublicLibrary,
    LocalCache,
    D2CoreNetwork
}

public sealed record BuildResolution(PublicBuildRecord Record, BuildResolutionOrigin Origin);

public sealed class D2CoreBuildResolver
{
    private readonly FileBuildLibraryStore _publicLibrary;
    private readonly FileBuildLibraryStore _localCache;
    private readonly ID2CoreBuildClient _client;

    public D2CoreBuildResolver(
        FileBuildLibraryStore publicLibrary,
        FileBuildLibraryStore localCache,
        ID2CoreBuildClient client)
    {
        _publicLibrary = publicLibrary;
        _localCache = localCache;
        _client = client;
    }

    public async Task<BuildResolution> ResolveAsync(
        D2CoreBuildReference reference,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh)
        {
            var hasPublic = _publicLibrary.TryLoad("d2core", reference.BuildId, out var publicRecord);
            var hasLocal = _localCache.TryLoad("d2core", reference.BuildId, out var localRecord);
            if (hasLocal && (!hasPublic || localRecord!.SourceUpdatedAt > publicRecord!.SourceUpdatedAt))
            {
                return new BuildResolution(localRecord!, BuildResolutionOrigin.LocalCache);
            }

            if (hasPublic)
            {
                return new BuildResolution(publicRecord!, BuildResolutionOrigin.PublicLibrary);
            }

            if (hasLocal)
            {
                return new BuildResolution(localRecord!, BuildResolutionOrigin.LocalCache);
            }
        }

        var fetched = await _client.FetchAsync(reference, cancellationToken).ConfigureAwait(false);
        _localCache.Save(fetched);
        return new BuildResolution(fetched, BuildResolutionOrigin.D2CoreNetwork);
    }
}

public static class D2CoreProfileMapper
{
    private static readonly IReadOnlyDictionary<int, (EquipmentSlotKind Slot, string Label)> StandardSlotMap =
        new Dictionary<int, (EquipmentSlotKind, string)>
        {
            [0] = (EquipmentSlotKind.Helm, "头盔"),
            [1] = (EquipmentSlotKind.Chest, "胸甲"),
            [2] = (EquipmentSlotKind.Gloves, "手套"),
            [3] = (EquipmentSlotKind.Pants, "裤子"),
            [4] = (EquipmentSlotKind.Boots, "靴子"),
            [5] = (EquipmentSlotKind.Ranged, "远程武器"),
            [8] = (EquipmentSlotKind.Amulet, "项链"),
            [9] = (EquipmentSlotKind.RingLeft, "戒指 1"),
            [10] = (EquipmentSlotKind.RingRight, "戒指 2"),
            [12] = (EquipmentSlotKind.MainHand, "主手"),
            [13] = (EquipmentSlotKind.OffHand, "副手")
        };

    private static readonly IReadOnlyDictionary<int, (EquipmentSlotKind Slot, string Label)> BarbarianWeaponSlotMap =
        new Dictionary<int, (EquipmentSlotKind, string)>
        {
            [5] = (EquipmentSlotKind.BarbarianBludgeoning, "双手钝击武器"),
            [6] = (EquipmentSlotKind.BarbarianDualWieldMainHand, "双持主手"),
            [12] = (EquipmentSlotKind.BarbarianSlashing, "双手挥砍武器"),
            [13] = (EquipmentSlotKind.BarbarianDualWieldOffHand, "双持副手")
        };

    public static BuildProfile CreateProfile(PublicBuildRecord record, int variantIndex)
    {
        var variant = record.Variants.FirstOrDefault(candidate => candidate.Index == variantIndex)
            ?? throw new InvalidDataException($"暗黑核 BD 没有第 {variantIndex + 1} 个变体。");
        var defaults = HudProfileFactory.CreateDefaultRules(record.ClassName)
            .ToDictionary(rule => rule.Slot);
        var rules = new List<EquipmentAffixRule>();
        foreach (var item in variant.Equipment)
        {
            if (!TryGetSlotDefinition(record.ClassName, item.SourceSlot, out var mapped))
            {
                continue;
            }

            var layout = defaults[mapped.Slot];
            rules.Add(new EquipmentAffixRule
            {
                Slot = mapped.Slot,
                SlotLabel = mapped.Label,
                ItemName = item.DisplayName,
                MandatoryText = string.Join(Environment.NewLine, item.Affixes.Select(affix => affix.DisplayText)),
                OptionalText = string.IsNullOrWhiteSpace(item.AspectName) || item.AspectName == item.DisplayName
                    ? string.Empty
                    : $"威能 · {item.AspectName}",
                Affixes = item.Affixes,
                IsEnabled = true,
                AnchorX = layout.AnchorX,
                AnchorY = layout.AnchorY,
                DisplayWidth = layout.DisplayWidth
            });
        }

        return new BuildProfile
        {
            Name = string.IsNullOrWhiteSpace(variant.Name) ? record.Title : variant.Name,
            ClassName = TranslateClass(record.ClassName),
            Variant = $"S{record.Season} · 暗黑核 {record.BuildId} / #{variantIndex + 1}",
            Source = "d2core",
            SourceBuildId = record.BuildId,
            SourceVariantIndex = variantIndex,
            SourceUrl = $"{record.CanonicalUrl}&var={variantIndex + 1}",
            LibraryContentHash = record.ContentHash,
            ImportedEquipment = variant.Equipment,
            EquipmentRules = rules
        };
    }

    public static string GetSlotLabel(int sourceSlot) =>
        TryGetSlot(sourceSlot, out var mapped) ? GetSlotLabel(mapped) : $"装备位 {sourceSlot}";

    public static bool TryGetSlot(int sourceSlot, out EquipmentSlotKind slot)
    {
        if (StandardSlotMap.TryGetValue(sourceSlot, out var mapped))
        {
            slot = mapped.Slot;
            return true;
        }

        slot = default;
        return false;
    }

    public static bool TryGetSlot(string? className, int sourceSlot, out EquipmentSlotKind slot)
    {
        if (TryGetSlotDefinition(className, sourceSlot, out var mapped))
        {
            slot = mapped.Slot;
            return true;
        }

        slot = default;
        return false;
    }

    public static bool IsBarbarianClass(string? className) =>
        string.Equals(className, "Barbarian", StringComparison.OrdinalIgnoreCase)
        || string.Equals(className, "野蛮人", StringComparison.Ordinal);

    private static string GetSlotLabel(EquipmentSlotKind slot) =>
        StandardSlotMap.Values.FirstOrDefault(candidate => candidate.Slot == slot).Label ?? string.Empty;

    private static bool TryGetSlotDefinition(
        string? className,
        int sourceSlot,
        out (EquipmentSlotKind Slot, string Label) mapped)
    {
        if (IsBarbarianClass(className) && BarbarianWeaponSlotMap.TryGetValue(sourceSlot, out mapped))
        {
            return true;
        }

        return StandardSlotMap.TryGetValue(sourceSlot, out mapped);
    }

    private static string TranslateClass(string className) => className switch
    {
        "Barbarian" => "野蛮人",
        "Druid" => "德鲁伊",
        "Necromancer" => "死灵法师",
        "Rogue" => "游侠",
        "Sorcerer" => "巫师",
        "Spiritborn" => "魂灵师",
        "Paladin" => "圣骑士",
        "Warlock" => "术士",
        _ => className
    };
}

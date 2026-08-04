using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace D4Hub.Core;

public enum LootFilterStage
{
    General,
    Leveling,
    Early,
    Mid,
    Late,
    Push
}

public sealed class LootFilterLegend
{
    public string Color { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class LootFilterPreset
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "manual";
    public string BuildId { get; set; } = string.Empty;
    public int VariantIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BuildName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int Season { get; set; }
    public LootFilterStage Stage { get; set; }
    public string LevelRange { get; set; } = string.Empty;
    public List<string> UseCases { get; set; } = new();
    public bool IsRecommended { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CopyCode { get; set; } = string.Empty;
    public List<LootFilterLegend> Legend { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string StageLabel => LootFilterMetadata.GetStageLabel(Stage);

    [JsonIgnore]
    public string VariantLabel => VariantIndex > 0 ? $"#{VariantIndex + 1}" : "默认变体";

    [JsonIgnore]
    public string BuildDisplayName => string.IsNullOrWhiteSpace(BuildName) ? "通用过滤器" : BuildName;

    [JsonIgnore]
    public string UseCaseLabel => UseCases.Count == 0 ? "未标注用途" : string.Join(" · ", UseCases);

    [JsonIgnore]
    public string ScopeLabel => string.IsNullOrWhiteSpace(LevelRange) ? StageLabel : LevelRange;

    [JsonIgnore]
    public string ClassLabel => ClassName switch
    {
        "Druid" => "德鲁伊",
        "Barbarian" => "野蛮人",
        "Rogue" => "游侠",
        "Sorcerer" => "巫师",
        "Necromancer" => "死灵法师",
        "Spiritborn" => "灵巫",
        "Paladin" => "圣骑士",
        "Warlock" => "术士",
        _ => ClassName
    };

    [JsonIgnore]
    public string StageUseCaseLabel => $"{StageLabel} · {UseCaseLabel}";

    [JsonIgnore]
    public string RecommendationLabel => IsRecommended ? "推荐" : "可用";

    [JsonIgnore]
    public string SourceUpdatedAtLabel => SourceUpdatedAt is null
        ? "来源更新时间未知"
        : $"来源更新于 {SourceUpdatedAt.Value:yyyy-MM-dd}";

    public DateTimeOffset? SourceUpdatedAt { get; set; }
}

public sealed class LootFilterLibraryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<LootFilterPreset> Filters { get; set; } = new();
}

public static class LootFilterMetadata
{
    private static readonly Regex CodeCharacterPattern = new(
        "^[A-Za-z0-9+/=_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string GetStageLabel(LootFilterStage stage) => stage switch
    {
        LootFilterStage.Leveling => "1-70 开荒",
        LootFilterStage.Early => "前期",
        LootFilterStage.Mid => "中期",
        LootFilterStage.Late => "后期 / 终局",
        LootFilterStage.Push => "冲层",
        _ => "综合"
    };

    public static LootFilterStage InferStage(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Contains("1-70", StringComparison.OrdinalIgnoreCase)
            || value.Contains("开荒", StringComparison.Ordinal))
        {
            return LootFilterStage.Leveling;
        }

        if (value.Contains("前期", StringComparison.Ordinal))
        {
            return LootFilterStage.Early;
        }

        if (value.Contains("中后期", StringComparison.Ordinal)
            || value.Contains("后期", StringComparison.Ordinal)
            || value.Contains("终局", StringComparison.Ordinal))
        {
            return LootFilterStage.Late;
        }

        if (value.Contains("中期", StringComparison.Ordinal))
        {
            return LootFilterStage.Mid;
        }

        if (value.Contains("冲层", StringComparison.Ordinal)
            || value.Contains("天塔", StringComparison.Ordinal)
            || value.Contains("pit", StringComparison.OrdinalIgnoreCase))
        {
            return LootFilterStage.Push;
        }

        return LootFilterStage.General;
    }

    public static string NormalizeCode(string? code)
    {
        var normalized = string.Concat((code ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
        if (normalized.Length < 16 || normalized.Length > 262_144)
        {
            throw new InvalidDataException("过滤码长度无效，请粘贴网站“复制”按钮生成的完整内容。");
        }

        if (!CodeCharacterPattern.IsMatch(normalized))
        {
            throw new InvalidDataException("过滤码包含无效字符，请不要粘贴说明文字或 HTML。");
        }

        return normalized;
    }

    public static void Ensure(LootFilterPreset filter)
    {
        if (filter.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported loot filter schema: {filter.SchemaVersion}");
        }

        filter.Legend ??= new List<LootFilterLegend>();
        filter.UseCases ??= new List<string>();
        if (filter.Legend.Count == 0)
        {
            filter.Legend = CreateDefaultLegend();
        }
        filter.CopyCode = NormalizeCode(filter.CopyCode);
        filter.Name = filter.Name?.Trim() ?? string.Empty;
        filter.BuildName = filter.BuildName?.Trim() ?? string.Empty;
        filter.ClassName = filter.ClassName?.Trim() ?? string.Empty;
        filter.LevelRange = filter.LevelRange?.Trim() ?? string.Empty;
        filter.UseCases = filter.UseCases
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        filter.Source = string.IsNullOrWhiteSpace(filter.Source) ? "manual" : filter.Source.Trim();
        filter.UpdatedAt = filter.UpdatedAt == default ? DateTimeOffset.UtcNow : filter.UpdatedAt;
    }

    public static List<LootFilterLegend> CreateDefaultLegend() =>
    [
        new() { Color = "#E74C3C", Label = "红色", Description = "可升级威能和需求的暗金" },
        new() { Color = "#E58AD4", Label = "粉色", Description = "3 条可用词条" },
        new() { Color = "#4DA3FF", Label = "蓝色", Description = "2 条正确词条" },
        new() { Color = "#7BD88F", Label = "浅绿色", Description = "可升级威能" }
    ];
}

public sealed class FileLootFilterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;
    private readonly bool _isReadOnly;

    public FileLootFilterStore(string path, bool isReadOnly = false)
    {
        _path = Path.GetFullPath(path);
        _isReadOnly = isReadOnly;
    }

    public IReadOnlyList<LootFilterPreset> LoadAll()
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<LootFilterPreset>();
        }

        try
        {
            var document = JsonSerializer.Deserialize<LootFilterLibraryDocument>(File.ReadAllText(_path), JsonOptions);
            if (document is null || document.SchemaVersion != 1 || document.Filters is null)
            {
                return Array.Empty<LootFilterPreset>();
            }

            var valid = new List<LootFilterPreset>();
            foreach (var filter in document.Filters)
            {
                try
                {
                    LootFilterMetadata.Ensure(filter);
                    valid.Add(filter);
                }
                catch (InvalidDataException)
                {
                    // A damaged entry must not hide the rest of the local collection.
                }
            }

            return valid;
        }
        catch (JsonException)
        {
            return Array.Empty<LootFilterPreset>();
        }
    }

    public void Save(IReadOnlyCollection<LootFilterPreset> filters)
    {
        if (_isReadOnly)
        {
            throw new InvalidOperationException("内置过滤器库是只读的。");
        }

        var document = new LootFilterLibraryDocument
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Filters = filters.ToList()
        };
        foreach (var filter in document.Filters)
        {
            LootFilterMetadata.Ensure(filter);
        }

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("过滤器库路径缺少父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        try
        {
            File.Move(temporaryPath, _path, true);
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

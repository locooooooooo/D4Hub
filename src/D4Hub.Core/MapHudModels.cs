using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4Hub.Core;

/// <summary>
/// 开荒地图 HUD 的持久化设置，挂在 <see cref="OverlaySettings.MapHud"/> 下。
/// 底图、POI 与音频全部只允许用户显式提供的本地路径；禁止游戏资产打包。
/// </summary>
public sealed class MapHudSettings : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _currentRegion = "dry_steppes";
    private double _opacity = 1.0;
    private double _overlayScale = 1.1;
    private double _timerBarBackgroundWidth = 0.40;
    private bool _timerBarHorizontal = true;
    private bool _showChests = true;
    private bool _showEliteChests = true;
    private bool _showEvents = true;
    private bool _showRituals = true;
    private bool _showDungeons = true;
    private string? _mapImagePath;
    private string? _poiDataPath;
    private double _scheduleOffsetSeconds;
    private bool _audioEnabled;
    private double _audioVolume = 0.70;
    private string? _audioBossPath;
    private string? _audioElitePath;
    private string? _audioBluePath;
    private string _hotkeyToggle = "Oem3";
    private string _hotkeyRedraw = "F5";
    private string _hotkeyResetPlacement = "F6";

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public string CurrentRegion
    {
        get => _currentRegion;
        set => SetField(ref _currentRegion, string.IsNullOrWhiteSpace(value) ? "dry_steppes" : value);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetField(ref _opacity, Math.Clamp(value, 0.10, 1.0));
    }

    public double OverlayScale
    {
        get => _overlayScale;
        set => SetField(ref _overlayScale, Math.Clamp(value, 0.50, 2.0));
    }

    public double TimerBarBackgroundWidth
    {
        get => _timerBarBackgroundWidth;
        set => SetField(ref _timerBarBackgroundWidth, Math.Clamp(value, 0.0, 1.0));
    }

    public bool TimerBarHorizontal
    {
        get => _timerBarHorizontal;
        set => SetField(ref _timerBarHorizontal, value);
    }

    public bool ShowChests
    {
        get => _showChests;
        set => SetField(ref _showChests, value);
    }

    public bool ShowEliteChests
    {
        get => _showEliteChests;
        set => SetField(ref _showEliteChests, value);
    }

    public bool ShowEvents
    {
        get => _showEvents;
        set => SetField(ref _showEvents, value);
    }

    public bool ShowRituals
    {
        get => _showRituals;
        set => SetField(ref _showRituals, value);
    }

    public bool ShowDungeons
    {
        get => _showDungeons;
        set => SetField(ref _showDungeons, value);
    }

    /// <summary>用户本地底图目录；约定 {MapImagePath}/{regionKey}.png。</summary>
    public string? MapImagePath
    {
        get => _mapImagePath;
        set => SetField(ref _mapImagePath, value);
    }

    /// <summary>用户本地社区 POI JSON 路径。</summary>
    public string? PoiDataPath
    {
        get => _poiDataPath;
        set => SetField(ref _poiDataPath, value);
    }

    /// <summary>世界事件日程手动偏移校准（秒，负值提前、正值延后）。</summary>
    public double ScheduleOffsetSeconds
    {
        get => _scheduleOffsetSeconds;
        set => SetField(ref _scheduleOffsetSeconds, value);
    }

    public bool AudioEnabled
    {
        get => _audioEnabled;
        set => SetField(ref _audioEnabled, value);
    }

    public string? AudioBossPath
    {
        get => _audioBossPath;
        set => SetField(ref _audioBossPath, value);
    }

    public string? AudioElitePath
    {
        get => _audioElitePath;
        set => SetField(ref _audioElitePath, value);
    }

    public string? AudioBluePath
    {
        get => _audioBluePath;
        set => SetField(ref _audioBluePath, value);
    }

    /// <summary>音频提醒音量（0–1）。</summary>
    public double AudioVolume
    {
        get => _audioVolume;
        set => SetField(ref _audioVolume, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>切换地图 HUD 显隐的全局热键（WPF Key 枚举名，如 Oem3 即 `~`）。</summary>
    public string HotkeyToggle
    {
        get => _hotkeyToggle;
        set => SetField(ref _hotkeyToggle, string.IsNullOrWhiteSpace(value) ? "Oem3" : value);
    }

    /// <summary>重绘地图的全局热键。</summary>
    public string HotkeyRedraw
    {
        get => _hotkeyRedraw;
        set => SetField(ref _hotkeyRedraw, string.IsNullOrWhiteSpace(value) ? "F5" : value);
    }

    /// <summary>重置地图位置（重新贴附）的全局热键。</summary>
    public string HotkeyResetPlacement
    {
        get => _hotkeyResetPlacement;
        set => SetField(ref _hotkeyResetPlacement, string.IsNullOrWhiteSpace(value) ? "F6" : value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>
/// POI 标记类别，JSON 序列化使用 camelCase 字符串（chest/elite/event/ritual/dungeon），由 <see cref="PoiCatalogStore"/> 的选项配置。
/// 读取时对未识别字符串返回哨兵值（<see cref="PoiCatalogStore"/> 会在逐条校验中拒绝），保证社区 JSON 中单条坏数据不炸整个文件。
/// </summary>
public enum PoiMarkerCategory
{
    Chest,
    Elite,
    Event,
    Ritual,
    Dungeon
}

/// <summary>
/// 宽容的类别转换器：未知字符串映射为 <see cref="PoiMarkerCategory"/> 哨兵值（255），
/// 由逐条校验（<see cref="PoiCatalogStore.IsValidMarker"/>）拒绝，而不是让整个文件反序列化失败。
/// </summary>
public sealed class PoiMarkerCategoryConverter : JsonConverter<PoiMarkerCategory>
{
    public const PoiMarkerCategory UnknownSentinel = (PoiMarkerCategory)255;

    public override PoiMarkerCategory Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return Enum.TryParse<PoiMarkerCategory>(value, ignoreCase: true, out var parsed)
            ? parsed
            : UnknownSentinel;
    }

    public override void Write(
        Utf8JsonWriter writer,
        PoiMarkerCategory value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

/// <summary>单个 POI 标记；坐标为相对底图左上角的归一化 0–1。x/y 缺失或越界时整条被拒绝。</summary>
public sealed class PoiMarker
{
    public PoiMarkerCategory Category { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public string? Label { get; set; }
}

/// <summary>社区 POI JSON 中单个区域的数据集。</summary>
public sealed class PoiRegion
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<PoiMarker> Markers { get; set; } = new();
}

/// <summary>社区 POI JSON 根结构。</summary>
public sealed class PoiCatalog
{
    public int FormatVersion { get; set; } = 1;
    public List<PoiRegion> Regions { get; set; } = new();
}

/// <summary>
/// 社区 POI JSON 加载器。单条记录非法时拒绝该条并保留其余数据；
/// 文件缺失、格式版本不符或根结构非法时整体 fail-closed。
/// </summary>
public static class PoiCatalogStore
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions PoiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new PoiMarkerCategoryConverter() }
    };

    public static PoiCatalog? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        PoiCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<PoiCatalog>(
                File.ReadAllText(path),
                PoiJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (catalog is null || catalog.FormatVersion != CurrentFormatVersion)
        {
            return null;
        }

        catalog.Regions ??= new List<PoiRegion>();
        foreach (var region in catalog.Regions)
        {
            region.Markers ??= new List<PoiMarker>();
            region.Markers = region.Markers
                .Where(marker => IsValidMarker(marker))
                .ToList();
        }

        return catalog;
    }

    public static IReadOnlyList<PoiMarker> GetMarkers(PoiCatalog? catalog, string regionKey)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(regionKey))
        {
            return [];
        }

        return catalog.Regions
            .FirstOrDefault(region => string.Equals(region.Key, regionKey, StringComparison.Ordinal))
            ?.Markers ?? [];
    }

    private static bool IsValidMarker(PoiMarker marker)
    {
        if (marker is null)
        {
            return false;
        }

        var x = marker.X;
        var y = marker.Y;
        if (x is null || y is null
            || !double.IsFinite(x.Value) || x.Value is < 0 or > 1
            || !double.IsFinite(y.Value) || y.Value is < 0 or > 1)
        {
            return false;
        }

        return Enum.IsDefined(marker.Category)
            && marker.Category != PoiMarkerCategoryConverter.UnknownSentinel;
    }
}

/// <summary>世界事件类型。</summary>
public enum WorldEventKind
{
    Helltide,
    WorldBoss,
    Legion
}

/// <summary>世界事件日程条目（公开轮换周期 + 进行时长 + 相位偏移，全部可配置）。</summary>
public sealed record WorldEventScheduleEntry(
    WorldEventKind Kind,
    TimeSpan Cycle,
    TimeSpan Duration,
    TimeSpan PhaseOffset);

/// <summary>
/// 世界事件时钟：基于公开日程推算 + 用户手动偏移校准，纯函数可测试。
/// 结果只用于"日程推算"提示，不代表游戏内部精确倒计时。
/// </summary>
public sealed class WorldEventClock
{
    private readonly WorldEventScheduleEntry[] _entries;
    private TimeSpan _manualOffset;

    public WorldEventClock(IEnumerable<WorldEventScheduleEntry> entries)
    {
        _entries = entries.ToArray();
    }

    /// <summary>手动偏移校准（秒）。</summary>
    public double ManualOffsetSeconds
    {
        get => _manualOffset.TotalSeconds;
        set => _manualOffset = TimeSpan.FromSeconds(value);
    }

    public WorldEventScheduleEntry? GetEntry(WorldEventKind kind) =>
        _entries.FirstOrDefault(entry => entry.Kind == kind);

    /// <summary>
    /// 推算指定事件的剩余时间与状态。
    /// <paramref name="now"/> 注入以便测试与离线回放。
    /// </summary>
    public (TimeSpan Remaining, bool Active) Evaluate(WorldEventKind kind, DateTimeOffset now)
    {
        var entry = GetEntry(kind);
        if (entry is null || entry.Cycle <= TimeSpan.Zero)
        {
            return (TimeSpan.Zero, false);
        }

        var adjusted = now.ToUniversalTime() + _manualOffset;
        var cycleSeconds = entry.Cycle.TotalSeconds;
        var elapsed = (adjusted - DateTimeOffset.UnixEpoch).TotalSeconds % cycleSeconds;
        if (elapsed < 0)
        {
            elapsed += cycleSeconds;
        }

        var phase = (elapsed - entry.PhaseOffset.TotalSeconds + cycleSeconds) % cycleSeconds;
        var active = phase < entry.Duration.TotalSeconds;
        var remaining = active
            ? entry.Duration.TotalSeconds - phase
            : cycleSeconds - phase;
        return (TimeSpan.FromSeconds(Math.Max(0, remaining)), active);
    }
}

/// <summary>
/// 世界事件边缘检测器：记录上一观测时刻的进行状态，返回"由等待转入进行"的事件。
/// 用于音频提醒等一次性触发；初始化时以当前状态为基线，避免启动即误触发。
/// </summary>
public sealed class WorldEventEdgeTracker
{
    private readonly Dictionary<WorldEventKind, bool> _lastActive;

    public WorldEventEdgeTracker(WorldEventClock clock, DateTimeOffset now)
    {
        _lastActive = new Dictionary<WorldEventKind, bool>();
        foreach (var kind in Enum.GetValues<WorldEventKind>())
        {
            _lastActive[kind] = clock.Evaluate(kind, now).Active;
        }
    }

    /// <summary>
    /// 推进到 <paramref name="now"/>，返回所有"由等待转入进行中"的事件。
    /// 结果已把当前状态写回内部基线；同一事件不会重复上报直到它再次经历等待。
    /// </summary>
    public IReadOnlyList<WorldEventKind> Rising(WorldEventClock clock, DateTimeOffset now)
    {
        var rising = new List<WorldEventKind>();
        foreach (var kind in Enum.GetValues<WorldEventKind>())
        {
            var active = clock.Evaluate(kind, now).Active;
            var wasActive = _lastActive.TryGetValue(kind, out var previous) && previous;
            if (active && !wasActive)
            {
                rising.Add(kind);
            }

            _lastActive[kind] = active;
        }

        return rising;
    }
}

/// <summary>公开社区共识的默认日程；数值仅为推算基线，可被用户偏移校准修正。</summary>
public static class WorldEventSchedule
{
    /// <summary>地狱狂潮：约 2 小时 15 分一轮，进行约 55 分钟。</summary>
    public static readonly TimeSpan HelltideCycle = TimeSpan.FromMinutes(135);

    public static readonly TimeSpan HelltideDuration = TimeSpan.FromMinutes(55);

    /// <summary>世界 Boss：约 3 小时 30 分一轮，进行约 15 分钟。</summary>
    public static readonly TimeSpan WorldBossCycle = TimeSpan.FromMinutes(210);

    public static readonly TimeSpan WorldBossDuration = TimeSpan.FromMinutes(15);

    /// <summary>军团：约 30 分钟一轮，进行约 10 分钟。</summary>
    public static readonly TimeSpan LegionCycle = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan LegionDuration = TimeSpan.FromMinutes(10);

    public static readonly WorldEventScheduleEntry[] Defaults =
    [
        new(WorldEventKind.Helltide, HelltideCycle, HelltideDuration, TimeSpan.FromMinutes(0)),
        new(WorldEventKind.WorldBoss, WorldBossCycle, WorldBossDuration, TimeSpan.FromMinutes(0)),
        new(WorldEventKind.Legion, LegionCycle, LegionDuration, TimeSpan.FromMinutes(5))
    ];
}

using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Input;
using D4Hub.App.Services;
using D4Hub.Core;

namespace D4Hub.App.ViewModels;

public readonly record struct HudPlacement(int Left, int Top, int Width, int Height);

public readonly record struct TransmutationReminderPlacement(int Left, int Top, int Width, int Height);

public readonly record struct StatisticsHudPlacement(int Left, int Top, int Width, int Height);

public readonly record struct MapHudPlacement(int Left, int Top, int Width, int Height);

public readonly record struct ScreenshotPreview(
    string Path,
    int ImageWidth,
    int ImageHeight,
    NormalizedRect PanelBounds);

public sealed record BuildClassFilterOption(string Label, string? ClassName);
public sealed record BuildSeasonFilterOption(string Label, BuildSeasonMode? Mode);
public sealed record BuildDifficultyFilterOption(string Label, BuildDifficultyMode? Mode);
public sealed record BuildPurposeFilterOption(string Label, BuildPurpose? Purpose);

public enum HubWorkspace
{
    Overview,
    Hud,
    LootFilters,
    Resources
}

public sealed class HudViewModel : ObservableObject
{
    private readonly IStateStore _stateStore;
    private readonly GameWindowLocator _windowLocator;
    private readonly ScreenFrameService _screenFrames;
    private readonly CharacterPanelDetector _panelDetector;
    private readonly BuildFingerprintService _fingerprints;
    private readonly TransmutationSceneDetector _transmutationSceneDetector;
    private readonly TransmutationReminderStateMachine _transmutationReminderState = new();
    private readonly D2CoreBuildResolver _d2CoreResolver;
    private readonly Stopwatch _realtimeCaptureClock = Stopwatch.StartNew();
    private readonly WorldEventClock _worldEventClock = new(WorldEventSchedule.Defaults);
    private BuildDocument _document;
    private BuildProfile? _selectedProfile;
    private EquipmentAffixRule? _selectedRule;
    private BuildProfile? _currentProfile;
    private string _gameStatus = "等待暗黑破坏神 IV";
    private string _panelStatus = "角色面板未检测";
    private string _recognitionStatus = "BD 未识别";
    private double _recognitionConfidence;
    private bool _isTracking = true;
    private string _d2CoreUrl = string.Empty;
    private string _d2CoreStatus = "等待粘贴暗黑核 BD";
    private bool _isD2CoreImporting;
    private PixelFrame? _lastFrame;
    private PanelDetection? _lastPanel;
    private GameClientWindow? _lastWindow;
    private bool _isLayoutEditing;
    private int _layoutClientWidth;
    private int _layoutClientHeight;
    private string _layoutStatus = "打开角色面板后，可按当前角色和分辨率校准位置";
    private string _activeLayoutIdentity = string.Empty;
    private Dictionary<EquipmentSlotKind, HudSlotLayout>? _layoutSnapshot;
    private HubWorkspace _activeWorkspace = HubWorkspace.Overview;
    private string _communityStatus = "抖音 @loco · QQ 群 736495487";
    private BuildClassFilterOption _selectedClassFilter = new("全部职业", null);
    private BuildSeasonFilterOption _selectedSeasonFilter = new("全部模式", null);
    private BuildDifficultyFilterOption _selectedDifficultyFilter = new("全部难度", null);
    private BuildPurposeFilterOption _selectedPurposeFilter = new("全部用途", null);
    private readonly System.Windows.Threading.DispatcherTimer _mapRefreshDebounce;

    public HudViewModel(
        IStateStore stateStore,
        BuildDocument document,
        GameWindowLocator windowLocator,
        ScreenFrameService screenFrames,
        CharacterPanelDetector panelDetector,
        BuildFingerprintService fingerprints,
        TransmutationSceneDetector transmutationSceneDetector,
        D2CoreBuildResolver d2CoreResolver,
        IReadOnlyList<ExternalResourceEntry> externalResources,
        LootFilterCollectionViewModel lootFilterCollection)
    {
        _stateStore = stateStore;
        _document = document;
        _windowLocator = windowLocator;
        _screenFrames = screenFrames;
        _panelDetector = panelDetector;
        _fingerprints = fingerprints;
        _transmutationSceneDetector = transmutationSceneDetector;
        _d2CoreResolver = d2CoreResolver;
        ExternalResources = externalResources;
        LootFilters = lootFilterCollection;
        RealtimePanel = new RealtimePanelViewModel(new WindowsRealtimeOcrAdapter());

        _document.EnsureValid();
        RealtimePanel.SetCollectionEnabled(_document.Overlay.DamageStatisticsHudEnabled);
        _selectedProfile = Profiles.FirstOrDefault(profile => profile.Id == _document.SelectedProfileId)
            ?? Profiles.FirstOrDefault();
        _selectedRule = _selectedProfile?.EquipmentRules.FirstOrDefault();

        // 地图 HUD 设置防抖刷新：区域/显示内容/路径等变更后一次性通知窗口重载，
        // 滑块拖动产生的连续变更会被合并为最后一次。
        _mapRefreshDebounce = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _mapRefreshDebounce.Tick += (_, _) =>
        {
            _mapRefreshDebounce.Stop();
            MapHudRefreshRequested?.Invoke();
        };
        MapHudSettings.PropertyChanged += (_, _) =>
        {
            _mapRefreshDebounce.Stop();
            _mapRefreshDebounce.Start();
        };

        AddProfileCommand = new RelayCommand(AddProfile);
        RemoveProfileCommand = new RelayCommand(() => RemoveProfileRequested?.Invoke(), () => Document.Profiles.Count > 1);
        CaptureFingerprintCommand = new RelayCommand(CaptureFingerprint, () => SelectedProfile is not null && _lastFrame is not null && _lastPanel is not null);
        LoadScreenshotCommand = new RelayCommand(() => ScreenshotRequested?.Invoke());
        ToggleTrackingCommand = new RelayCommand(ToggleTracking);
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke());
        ExportCommand = new RelayCommand(() => ExportRequested?.Invoke());
        PasteD2CoreCommand = new RelayCommand(() => PasteD2CoreRequested?.Invoke(), () => !IsD2CoreImporting);
        ImportD2CoreCommand = new RelayCommand(() => _ = ImportD2CoreAsync(D2CoreUrl, false), () => !IsD2CoreImporting);
        RefreshD2CoreCommand = new RelayCommand(() => _ = ImportD2CoreAsync(D2CoreUrl, true), () => !IsD2CoreImporting && !string.IsNullOrWhiteSpace(D2CoreUrl));
        SaveCommand = new RelayCommand(() => Save());
        BeginLayoutEditingCommand = new RelayCommand(BeginLayoutEditing, () => SelectedProfile is not null && !IsLayoutEditing);
        SaveLayoutTemplateCommand = new RelayCommand(SaveLayoutTemplate, () => IsLayoutEditing);
        CancelLayoutEditingCommand = new RelayCommand(CancelLayoutEditing, () => IsLayoutEditing);
        ResetLayoutCommand = new RelayCommand(ResetLayout, () => IsLayoutEditing && SelectedProfile is not null);
        OpenHudWorkspaceCommand = new RelayCommand(() => ActiveWorkspace = HubWorkspace.Hud);
        OpenLootFiltersWorkspaceCommand = new RelayCommand(() => ActiveWorkspace = HubWorkspace.LootFilters);
        OpenResourcesWorkspaceCommand = new RelayCommand(() => ActiveWorkspace = HubWorkspace.Resources);
        CopyDouyinHandleCommand = new RelayCommand(() => CopyDouyinHandleRequested?.Invoke());
        CopyCommunityGroupCommand = new RelayCommand(() => CopyCommunityGroupRequested?.Invoke());

        WireDocument();
        RefreshFilterOptions();
    }

    public BuildDocument Document
    {
        get => _document;
        private set
        {
            UnwireDocument();
            _document = value;
            _document.EnsureValid();
            WireDocument();
            RefreshFilterOptions();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Profiles));
            OnPropertyChanged(nameof(FilteredProfiles));
            OnPropertyChanged(nameof(ProfileCountText));
            OnPropertyChanged(nameof(HudDisplayMode));
            OnPropertyChanged(nameof(IsCompactHudMode));
            OnPropertyChanged(nameof(IsValuesHudMode));
        }
    }

    public IEnumerable<BuildProfile> Profiles => Document.Profiles;

    public IEnumerable<BuildProfile> FilteredProfiles => Document.Profiles.Where(profile =>
        (SelectedClassFilter.ClassName is null
            || string.Equals(profile.ClassName, SelectedClassFilter.ClassName, StringComparison.Ordinal))
        && (SelectedSeasonFilter.Mode is null || profile.SeasonMode == SelectedSeasonFilter.Mode)
        && (SelectedDifficultyFilter.Mode is null || profile.DifficultyMode == SelectedDifficultyFilter.Mode)
        && (SelectedPurposeFilter.Purpose is null || profile.Purposes.Contains(SelectedPurposeFilter.Purpose.Value)));

    public ObservableCollection<BuildClassFilterOption> ClassFilters { get; } = new();

    public IReadOnlyList<BuildSeasonFilterOption> SeasonFilters { get; } =
    [
        new("全部模式", null),
        new("赛季模式", BuildSeasonMode.Seasonal),
        new("永恒模式", BuildSeasonMode.Eternal),
        new("未标注", BuildSeasonMode.Unknown)
    ];

    public IReadOnlyList<BuildDifficultyFilterOption> DifficultyFilters { get; } =
    [
        new("全部难度", null),
        new("普通模式", BuildDifficultyMode.Standard),
        new("专家模式", BuildDifficultyMode.Hardcore),
        new("未标注", BuildDifficultyMode.Unknown)
    ];

    public IReadOnlyList<BuildPurposeFilterOption> PurposeFilters { get; } =
    [
        new("全部用途", null),
        new("开荒", BuildPurpose.Leveling),
        new("冲层", BuildPurpose.PitPush),
        new("速刷", BuildPurpose.SpeedFarm),
        new("首领", BuildPurpose.Bossing),
        new("综合", BuildPurpose.General)
    ];

    public BuildClassFilterOption SelectedClassFilter
    {
        get => _selectedClassFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedClassFilter, value))
            {
                ApplyProfileFilters();
            }
        }
    }

    public BuildSeasonFilterOption SelectedSeasonFilter
    {
        get => _selectedSeasonFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedSeasonFilter, value))
            {
                ApplyProfileFilters();
            }
        }
    }

    public BuildDifficultyFilterOption SelectedDifficultyFilter
    {
        get => _selectedDifficultyFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedDifficultyFilter, value))
            {
                ApplyProfileFilters();
            }
        }
    }

    public BuildPurposeFilterOption SelectedPurposeFilter
    {
        get => _selectedPurposeFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedPurposeFilter, value))
            {
                ApplyProfileFilters();
            }
        }
    }

    public HubWorkspace ActiveWorkspace
    {
        get => _activeWorkspace;
        set
        {
            if (!SetProperty(ref _activeWorkspace, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsOverviewPage));
            OnPropertyChanged(nameof(IsHudPage));
            OnPropertyChanged(nameof(IsLootFiltersPage));
            OnPropertyChanged(nameof(IsResourcesPage));
            OnPropertyChanged(nameof(WorkspaceSubtitle));
        }
    }

    public bool IsOverviewPage
    {
        get => ActiveWorkspace == HubWorkspace.Overview;
        set
        {
            if (value)
            {
                ActiveWorkspace = HubWorkspace.Overview;
            }
        }
    }

    public bool IsHudPage
    {
        get => ActiveWorkspace == HubWorkspace.Hud;
        set
        {
            if (value)
            {
                ActiveWorkspace = HubWorkspace.Hud;
            }
        }
    }

    public bool IsLootFiltersPage
    {
        get => ActiveWorkspace == HubWorkspace.LootFilters;
        set
        {
            if (value)
            {
                ActiveWorkspace = HubWorkspace.LootFilters;
            }
        }
    }

    public bool IsResourcesPage
    {
        get => ActiveWorkspace == HubWorkspace.Resources;
        set
        {
            if (value)
            {
                ActiveWorkspace = HubWorkspace.Resources;
            }
        }
    }

    public string WorkspaceSubtitle => ActiveWorkspace switch
    {
        HubWorkspace.LootFilters => "战利品过滤器集合",
        HubWorkspace.Overview => "本地游戏辅助工作台",
        HubWorkspace.Hud => "HUD 叠层与 BD 配置",
        HubWorkspace.Resources => "开荒地图 HUD 与社区地图",
        _ => "本地游戏辅助工作台"
    };

    public IReadOnlyList<ExternalResourceEntry> ExternalResources { get; }

    public LootFilterCollectionViewModel LootFilters { get; }

    /// <summary>
    /// User-facing realtime statistics controls and the latest trusted snapshot.
    /// </summary>
    public RealtimePanelViewModel RealtimePanel { get; }

    public ExternalResourceEntry? HelltidesResource => ExternalResources.FirstOrDefault(entry =>
        string.Equals(entry.ResourceId, "diablo-iv.helltides-map", StringComparison.Ordinal)
        && string.Equals(entry.Status, "active", StringComparison.Ordinal));

    public string ProfileCountText => $"{Document.Profiles.Count} 个 BD";

    public BuildProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
            {
                return;
            }

            Document.SelectedProfileId = value?.Id ?? string.Empty;
            SelectedRule = value?.EquipmentRules.FirstOrDefault();
            _activeLayoutIdentity = string.Empty;
            OnPropertyChanged(nameof(FingerprintStatus));
            OnPropertyChanged(nameof(SelectedProfileSummary));
            BeginLayoutEditingCommand.RaiseCanExecuteChanged();
            Save(false);
        }
    }

    public EquipmentAffixRule? SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(ref _selectedRule, value);
    }

    public BuildProfile? CurrentProfile
    {
        get => _currentProfile;
        private set
        {
            if (SetProperty(ref _currentProfile, value))
            {
                _activeLayoutIdentity = string.Empty;
                OnPropertyChanged(nameof(CurrentProfileName));
                OnPropertyChanged(nameof(VisibleHudRules));
            }
        }
    }

    public IEnumerable<EquipmentAffixRule> VisibleHudRules =>
        (CurrentProfile ?? SelectedProfile)?.EquipmentRules.Where(rule => rule.IsEnabled)
        ?? Enumerable.Empty<EquipmentAffixRule>();

    public string CurrentProfileName => CurrentProfile?.Name ?? "未识别 BD";

    public HudDisplayMode HudDisplayMode
    {
        get => Document.Overlay.HudDisplayMode;
        set
        {
            if (Document.Overlay.HudDisplayMode == value)
            {
                return;
            }

            Document.Overlay.HudDisplayMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCompactHudMode));
            OnPropertyChanged(nameof(IsValuesHudMode));
        }
    }

    public bool IsCompactHudMode
    {
        get => HudDisplayMode == HudDisplayMode.Compact;
        set
        {
            if (value)
            {
                HudDisplayMode = HudDisplayMode.Compact;
            }
        }
    }

    public bool IsValuesHudMode
    {
        get => HudDisplayMode == HudDisplayMode.Values;
        set
        {
            if (value)
            {
                HudDisplayMode = HudDisplayMode.Values;
            }
        }
    }

    public bool IsDamageStatisticsHudEnabled
    {
        get => Document.Overlay.DamageStatisticsHudEnabled;
        set
        {
            if (Document.Overlay.DamageStatisticsHudEnabled == value)
            {
                return;
            }

            Document.Overlay.DamageStatisticsHudEnabled = value;
            RealtimePanel.SetCollectionEnabled(value);
            OnPropertyChanged();
            if (!value)
            {
                StatisticsHudHidden?.Invoke();
            }
        }
    }

    public bool IsStatisticsHudCompact
    {
        get => Document.Overlay.StatisticsHudCompact;
        set
        {
            if (Document.Overlay.StatisticsHudCompact == value)
            {
                return;
            }

            Document.Overlay.StatisticsHudCompact = value;
            OnPropertyChanged();
        }
    }

    /// <summary>开荒地图 HUD 持久化设置；旧存档缺失时兜底创建。</summary>
    public MapHudSettings MapHudSettings => Document.Overlay.MapHud ??= new MapHudSettings();

    /// <summary>世界事件时钟（公开日程推算 + 手动偏移校准），供地图 HUD 窗口驱动计时条。</summary>
    public WorldEventClock WorldEventClock => _worldEventClock;

    public bool IsMapHudEnabled
    {
        get => MapHudSettings.Enabled;
        set
        {
            if (MapHudSettings.Enabled == value)
            {
                return;
            }

            MapHudSettings.Enabled = value;
            OnPropertyChanged();
            if (!value)
            {
                MapHudHidden?.Invoke();
            }
        }
    }

    /// <summary>计时条排列的互斥单选辅助：横排为真时竖排为假，反之亦然。</summary>
    public bool MapTimerBarVertical
    {
        get => !MapHudSettings.TimerBarHorizontal;
        set
        {
            if (MapHudSettings.TimerBarHorizontal == !value)
            {
                return;
            }

            MapHudSettings.TimerBarHorizontal = !value;
            OnPropertyChanged();
        }
    }

    /// <summary>全局热键：切换地图 HUD 显隐。</summary>
    public void ToggleMapHudVisibility() => IsMapHudEnabled = !IsMapHudEnabled;

    /// <summary>全局热键：重绘地图（重载底图与 POI 并应用显示样式）。</summary>
    public void RedrawMap() => MapHudRefreshRequested?.Invoke();

    /// <summary>全局热键：重置地图位置（按当前游戏窗口重新计算贴附位置）。</summary>
    public void ResetMapPlacement()
    {
        var window = _lastWindow;
        if (window is null)
        {
            return;
        }

        MapHudPlacementChanged?.Invoke(CalculateMapHudPlacement(window.Value, MapHudSettings));
    }

    public string SelectedProfileSummary => SelectedProfile is null
        ? "未选择"
        : $"{SelectedProfile.ClassName} · {SelectedProfile.Variant}";

    public string D2CoreUrl
    {
        get => _d2CoreUrl;
        set
        {
            if (SetProperty(ref _d2CoreUrl, value?.Trim() ?? string.Empty))
            {
                RefreshD2CoreCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string D2CoreStatus
    {
        get => _d2CoreStatus;
        private set => SetProperty(ref _d2CoreStatus, value);
    }

    public bool IsD2CoreImporting
    {
        get => _isD2CoreImporting;
        private set
        {
            if (SetProperty(ref _isD2CoreImporting, value))
            {
                ImportD2CoreCommand.RaiseCanExecuteChanged();
                RefreshD2CoreCommand.RaiseCanExecuteChanged();
                PasteD2CoreCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> TransmutationReminders { get; } = new[]
    {
        "威能",
        "回火",
        "精造",
        "打孔"
    };

    public string FingerprintStatus => SelectedProfile?.Fingerprint?.IsComplete == true
        ? $"已采集 · {SelectedProfile.Fingerprint.CapturedAt.ToLocalTime():MM-dd HH:mm}"
        : "尚未采集";

    public string GameStatus
    {
        get => _gameStatus;
        private set => SetProperty(ref _gameStatus, value);
    }

    public string PanelStatus
    {
        get => _panelStatus;
        private set => SetProperty(ref _panelStatus, value);
    }

    public string RecognitionStatus
    {
        get => _recognitionStatus;
        private set => SetProperty(ref _recognitionStatus, value);
    }

    public double RecognitionConfidence
    {
        get => _recognitionConfidence;
        private set => SetProperty(ref _recognitionConfidence, value);
    }

    public bool IsTracking
    {
        get => _isTracking;
        private set
        {
            if (SetProperty(ref _isTracking, value))
            {
                OnPropertyChanged(nameof(TrackingButtonText));
                OnPropertyChanged(nameof(TrackingStatusText));
            }
        }
    }

    public string TrackingButtonText => IsTracking ? "暂停监测" : "开始监测";

    public string TrackingStatusText => IsTracking ? "监测已启用" : "监测已暂停";

    public string CommunityStatus
    {
        get => _communityStatus;
        private set => SetProperty(ref _communityStatus, value);
    }

    public bool IsLayoutEditing
    {
        get => _isLayoutEditing;
        private set
        {
            if (!SetProperty(ref _isLayoutEditing, value))
            {
                return;
            }

            if (value)
            {
                RealtimePanel.InvalidatePendingReadout();
            }

            OnPropertyChanged(nameof(IsLayoutIdle));
            BeginLayoutEditingCommand.RaiseCanExecuteChanged();
            SaveLayoutTemplateCommand.RaiseCanExecuteChanged();
            CancelLayoutEditingCommand.RaiseCanExecuteChanged();
            ResetLayoutCommand.RaiseCanExecuteChanged();
            LayoutEditingChanged?.Invoke(value);
        }
    }

    public bool IsLayoutIdle => !IsLayoutEditing;

    public string LayoutStatus
    {
        get => _layoutStatus;
        private set => SetProperty(ref _layoutStatus, value);
    }

    public string LayoutResolutionLabel => _layoutClientWidth > 0 && _layoutClientHeight > 0
        ? $"{_layoutClientWidth} x {_layoutClientHeight}"
        : "等待游戏画面";

    public RelayCommand AddProfileCommand { get; }
    public RelayCommand RemoveProfileCommand { get; }
    public RelayCommand CaptureFingerprintCommand { get; }
    public RelayCommand LoadScreenshotCommand { get; }
    public RelayCommand ToggleTrackingCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }
    public RelayCommand PasteD2CoreCommand { get; }
    public RelayCommand ImportD2CoreCommand { get; }
    public RelayCommand RefreshD2CoreCommand { get; }
    public ICommand SaveCommand { get; }
    public RelayCommand BeginLayoutEditingCommand { get; }
    public RelayCommand SaveLayoutTemplateCommand { get; }
    public RelayCommand CancelLayoutEditingCommand { get; }
    public RelayCommand ResetLayoutCommand { get; }
    public RelayCommand OpenHudWorkspaceCommand { get; }
    public RelayCommand OpenLootFiltersWorkspaceCommand { get; }
    public RelayCommand OpenResourcesWorkspaceCommand { get; }
    public RelayCommand CopyDouyinHandleCommand { get; }
    public RelayCommand CopyCommunityGroupCommand { get; }

    public Action? RemoveProfileRequested { get; set; }
    public Action? ScreenshotRequested { get; set; }
    public Action? ImportRequested { get; set; }
    public Action? ExportRequested { get; set; }
    public Action? PasteD2CoreRequested { get; set; }
    public Action? CopyDouyinHandleRequested { get; set; }
    public Action? CopyCommunityGroupRequested { get; set; }
    public Action<HudPlacement>? HudPlacementChanged { get; set; }
    public Action? HudHidden { get; set; }
    public Action<StatisticsHudPlacement>? StatisticsHudPlacementChanged { get; set; }
    public Action? StatisticsHudHidden { get; set; }
    public Action<TransmutationReminderPlacement>? TransmutationReminderPlacementChanged { get; set; }
    public Action? TransmutationReminderHidden { get; set; }
    public Action<MapHudPlacement>? MapHudPlacementChanged { get; set; }
    public Action? MapHudHidden { get; set; }

    /// <summary>地图 HUD 设置变更（防抖后）通知窗口重载底图/POI/显示样式。</summary>
    public Action? MapHudRefreshRequested { get; set; }
    public Action<ScreenshotPreview>? PreviewReady { get; set; }
    public Action<bool>? LayoutEditingChanged { get; set; }

    public void SetCommunityStatus(string message) => CommunityStatus = message;

    public void PollLive()
    {
        if (IsLayoutEditing)
        {
            StatisticsHudHidden?.Invoke();
            HideTransmutationReminder();
            MapHudHidden?.Invoke();
            PollLayoutEditing();
            return;
        }

        if (!IsTracking || (!Document.Overlay.AutoAttach && !RealtimePanel.IsCollectionEnabled))
        {
            StatisticsHudHidden?.Invoke();
            HideTransmutationReminder();
            HudHidden?.Invoke();
            MapHudHidden?.Invoke();
            return;
        }

        var gameWindow = _windowLocator.FindDiabloWindow();
        if (gameWindow is null)
        {
            GameStatus = "暗黑破坏神 IV 未运行";
            PanelStatus = "角色面板未检测";
            SetSelectedProfileWaitingStatus("等待游戏");
            _lastFrame = null;
            _lastPanel = null;
            RealtimePanel.MarkWaitingForGame(_realtimeCaptureClock.Elapsed.TotalSeconds);
            StatisticsHudHidden?.Invoke();
            CaptureFingerprintCommand.RaiseCanExecuteChanged();
            HideTransmutationReminder();
            HudHidden?.Invoke();
            MapHudHidden?.Invoke();
            return;
        }

        try
        {
            var window = gameWindow.Value;
            _lastWindow = window;
            GameStatus = window.IsForeground ? "游戏窗口 · 前台" : "游戏窗口 · 后台";
            if (!window.IsForeground)
            {
                RealtimePanel.MarkWaitingForGame(_realtimeCaptureClock.Elapsed.TotalSeconds);
                StatisticsHudHidden?.Invoke();
                PanelStatus = "切回游戏后继续识别";
                SetSelectedProfileWaitingStatus("切回游戏显示");
                HideTransmutationReminder();
                HudHidden?.Invoke();
                MapHudHidden?.Invoke();
                return;
            }

            var frame = _screenFrames.Capture(window);
            if (IsDamageStatisticsHudEnabled)
            {
                var statisticsPlacement = CalculateStatisticsHudPlacement(window, IsStatisticsHudCompact);
                var exclusion = CalculateStatisticsHudOcrExclusion(window);
                RealtimePanel.CaptureFrame(frame, _realtimeCaptureClock.Elapsed.TotalSeconds, exclusion);
                StatisticsHudPlacementChanged?.Invoke(statisticsPlacement);
            }
            else
            {
                StatisticsHudHidden?.Invoke();
            }
            if (IsMapHudEnabled)
            {
                MapHudPlacementChanged?.Invoke(CalculateMapHudPlacement(window, MapHudSettings));
            }
            else
            {
                MapHudHidden?.Invoke();
            }
            if (!Document.Overlay.AutoAttach)
            {
                HideTransmutationReminder();
                HudHidden?.Invoke();
                return;
            }

            if (UpdateTransmutationReminder(window, frame))
            {
                _lastFrame = frame;
                _lastPanel = null;
                CaptureFingerprintCommand.RaiseCanExecuteChanged();
                HudHidden?.Invoke();
                return;
            }

            var panel = _panelDetector.Detect(frame);
            _lastFrame = frame;
            _lastPanel = panel;
            CaptureFingerprintCommand.RaiseCanExecuteChanged();

            PanelStatus = panel.Confidence >= Document.Overlay.PanelConfidenceThreshold
                ? $"角色面板 · {panel.Confidence:P0}"
                : $"未确认角色面板 · {panel.Confidence:P0}";

            if (panel.Confidence < Document.Overlay.PanelConfidenceThreshold)
            {
                SetSelectedProfileWaitingStatus("等待角色面板");
                RecognitionConfidence = 0;
                HudHidden?.Invoke();
                return;
            }

            if (SelectedProfile?.Fingerprint?.IsComplete != true)
            {
                CurrentProfile = SelectedProfile;
                RecognitionConfidence = 0;
                RecognitionStatus = $"手动选择 BD · {SelectedProfile?.Name}";
                UpdateHudPlacement(window, panel);
                return;
            }

            var match = _fingerprints.Recognize(
                frame,
                panel,
                Profiles,
                Document.Overlay.BuildConfidenceThreshold);
            CurrentProfile = match.Profile;
            RecognitionConfidence = match.Confidence;
            RecognitionStatus = match.Profile is null
                ? Profiles.Any(profile => profile.Fingerprint?.IsComplete == true)
                    ? $"未识别 BD · {match.Confidence:P0}"
                    : "需要采集 BD 指纹"
                : $"{match.Profile.Name} · {match.Confidence:P0}";

            UpdateHudPlacement(window, panel);
        }
        catch (Exception exception)
        {
            GameStatus = $"画面捕获失败 · {exception.Message}";
            StatisticsHudHidden?.Invoke();
            HideTransmutationReminder();
            HudHidden?.Invoke();
        }
    }

    public void LearnFromScreenshot(string path)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        try
        {
            var frame = _screenFrames.Load(path);
            var panel = _panelDetector.Detect(frame);
            if (panel.Confidence < 0.30)
            {
                PanelStatus = $"截图中未找到角色面板 · {panel.Confidence:P0}";
                return;
            }

            SelectedProfile.Fingerprint = _fingerprints.Capture(frame, panel);
            _lastFrame = frame;
            _lastPanel = panel;
            CurrentProfile = SelectedProfile;
            RecognitionConfidence = 1;
            PanelStatus = $"截图角色面板 · {panel.Confidence:P0}";
            RecognitionStatus = $"已登记 {SelectedProfile.Name}";
            OnPropertyChanged(nameof(FingerprintStatus));
            OnPropertyChanged(nameof(VisibleHudRules));
            Save(false);
            PreviewReady?.Invoke(new ScreenshotPreview(path, frame.Width, frame.Height, panel.Bounds));
        }
        catch (Exception exception)
        {
            RecognitionStatus = $"截图读取失败 · {exception.Message}";
        }
    }

    public void ImportFrom(string path)
    {
        try
        {
            var imported = new JsonStateStore(path).LoadStrict();
            var selectedProfile = imported.Profiles.FirstOrDefault(profile => profile.Id == imported.SelectedProfileId)
                ?? throw new InvalidDataException("导入文件中没有已选择的 BD。");

            _stateStore.Save(imported);
            Document = imported;
            _selectedProfile = selectedProfile;
            _selectedRule = selectedProfile.EquipmentRules.FirstOrDefault();
            CurrentProfile = selectedProfile;
            _activeLayoutIdentity = string.Empty;
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(SelectedRule));
            OnPropertyChanged(nameof(FingerprintStatus));
            OnPropertyChanged(nameof(SelectedProfileSummary));
            RemoveProfileCommand.RaiseCanExecuteChanged();
            CaptureFingerprintCommand.RaiseCanExecuteChanged();
            BeginLayoutEditingCommand.RaiseCanExecuteChanged();
            RecognitionStatus = "BD 库已导入";
        }
        catch (Exception exception)
        {
            RecognitionStatus = $"BD 库导入失败 · {exception.Message}";
        }
    }

    public async Task ImportD2CoreAsync(string input, bool forceRefresh = false)
    {
        if (!D2CoreBuildUrl.TryParse(input, out var reference, out var error))
        {
            D2CoreStatus = error;
            RecognitionStatus = "暗黑核链接无效";
            return;
        }

        D2CoreUrl = reference!.CanonicalUrl;
        IsD2CoreImporting = true;
        D2CoreStatus = forceRefresh ? "正在从暗黑核刷新…" : "正在查找公共 BD 库…";
        try
        {
            var resolution = await _d2CoreResolver.ResolveAsync(reference, forceRefresh);
            var profile = D2CoreProfileMapper.CreateProfile(resolution.Record, reference.VariantIndex);
            var existing = Document.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Source, "d2core", StringComparison.OrdinalIgnoreCase)
                && candidate.SourceBuildId == reference.BuildId
                && candidate.SourceVariantIndex == reference.VariantIndex);
            existing ??= Document.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Source, "d2core", StringComparison.OrdinalIgnoreCase)
                && candidate.SourceBuildId == reference.BuildId
                && candidate.SourceVariantIndex == reference.VariantIndex + 1
                && string.Equals(candidate.SourceUrl, reference.CanonicalUrl, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                profile.Id = existing.Id;
                profile.Fingerprint = existing.Fingerprint;
                profile.LayoutTemplates = existing.LayoutTemplates;
                Document.Profiles[Document.Profiles.IndexOf(existing)] = profile;
            }
            else
            {
                Document.Profiles.Add(profile);
            }

            SelectedProfile = profile;
            CurrentProfile = profile;
            var itemCount = profile.ImportedEquipment.Count;
            var affixCount = profile.ImportedEquipment.Sum(item => item.Affixes.Count);
            var origin = resolution.Origin switch
            {
                BuildResolutionOrigin.PublicLibrary => "公共库命中 · 零联网",
                BuildResolutionOrigin.LocalCache => "本地缓存命中 · 零联网",
                _ => "暗黑核已获取并写入本地库"
            };
            D2CoreStatus = $"{origin} · {itemCount} 件装备 / {affixCount} 条词缀";
            RecognitionStatus = $"已导入 {profile.Name}";
            Save(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or ArgumentException)
        {
            D2CoreStatus = $"导入失败 · {exception.Message}";
            RecognitionStatus = "暗黑核 BD 导入失败";
        }
        finally
        {
            IsD2CoreImporting = false;
        }
    }

    public void ExportTo(string path)
    {
        new JsonStateStore(path).Save(Document);
        RecognitionStatus = "BD 库已导出";
    }

    public void RemoveSelectedProfile()
    {
        if (SelectedProfile is null || Document.Profiles.Count <= 1)
        {
            return;
        }

        var index = Document.Profiles.IndexOf(SelectedProfile);
        Document.Profiles.Remove(SelectedProfile);
        SelectedProfile = Document.Profiles[Math.Clamp(index - 1, 0, Document.Profiles.Count - 1)];
        RemoveProfileCommand.RaiseCanExecuteChanged();
        Save(false);
    }

    public void Save(bool showStatus = true)
    {
        _stateStore.Save(Document);
        if (showStatus)
        {
            RecognitionStatus = $"已保存 · {DateTime.Now:HH:mm:ss}";
        }
    }

    public void CancelLayoutEditing()
    {
        if (!IsLayoutEditing)
        {
            return;
        }

        if (SelectedProfile is not null && _layoutSnapshot is not null)
        {
            RestoreLayout(SelectedProfile, _layoutSnapshot);
        }

        FinishLayoutEditing("已取消位置调整");
    }

    private void AddProfile()
    {
        var profile = HudProfileFactory.CreateStarterProfile();
        profile.Name = $"新 BD {Document.Profiles.Count + 1}";
        profile.ClassName = "待设置职业";
        profile.Fingerprint = null;
        Document.Profiles.Add(profile);
        SelectedProfile = profile;
        RemoveProfileCommand.RaiseCanExecuteChanged();
        Save(false);
    }

    private void CaptureFingerprint()
    {
        if (SelectedProfile is null || _lastFrame is null || _lastPanel is null)
        {
            return;
        }

        SelectedProfile.Fingerprint = _fingerprints.Capture(_lastFrame, _lastPanel.Value);
        CurrentProfile = SelectedProfile;
        RecognitionConfidence = 1;
        RecognitionStatus = $"已登记 {SelectedProfile.Name}";
        OnPropertyChanged(nameof(FingerprintStatus));
        Save(false);
    }

    private void ToggleTracking()
    {
        IsTracking = !IsTracking;
        if (!IsTracking)
        {
            RealtimePanel.InvalidatePendingReadout();
            StatisticsHudHidden?.Invoke();
            HideTransmutationReminder();
            HudHidden?.Invoke();
            GameStatus = "画面监测已暂停";
        }
    }

    private bool UpdateTransmutationReminder(GameClientWindow window, PixelFrame frame)
    {
        var detection = _transmutationSceneDetector.Detect(frame);
        var state = _transmutationReminderState.Advance(detection);
        if (!state.IsVisible)
        {
            if (state.VisibilityChanged)
            {
                TransmutationReminderHidden?.Invoke();
            }

            return false;
        }

        PanelStatus = $"嬗变物品 · {state.Confidence:P0}";
        RecognitionStatus = "嬗变改造提醒";
        RecognitionConfidence = state.Confidence;
        TransmutationReminderPlacementChanged?.Invoke(
            CalculateReminderPlacement(window, state.SelectedRecipeBounds));
        return true;
    }

    private void HideTransmutationReminder()
    {
        _transmutationReminderState.Reset();
        TransmutationReminderHidden?.Invoke();
    }

    private static TransmutationReminderPlacement CalculateReminderPlacement(
        GameClientWindow window,
        NormalizedRect recipeBounds)
    {
        var displayScale = Math.Clamp(window.Width / 1920d, 0.90, 1.20);
        var width = (int)Math.Round(272 * displayScale);
        var height = (int)Math.Round(52 * displayScale);
        var horizontalGap = (int)Math.Round(32 * displayScale);
        var verticalGap = (int)Math.Round(12 * displayScale);
        const int margin = 8;
        var recipeLeft = window.Left + (int)Math.Round(recipeBounds.X * window.Width);
        var recipeTop = window.Top + (int)Math.Round(recipeBounds.Y * window.Height);
        var minimumLeft = window.Left + margin;
        var maximumLeft = Math.Max(minimumLeft, window.Left + window.Width - width - margin);
        var minimumTop = window.Top + margin;
        var maximumTop = Math.Max(minimumTop, window.Top + window.Height - height - margin);
        var left = Math.Clamp(Quantize(recipeLeft - width - horizontalGap, 4), minimumLeft, maximumLeft);
        var top = Math.Clamp(Quantize(recipeTop - height - verticalGap, 4), minimumTop, maximumTop);
        return new TransmutationReminderPlacement(left, top, width, height);
    }

    private static int Quantize(int value, int step) =>
        (int)Math.Round(value / (double)step) * step;

    public static StatisticsHudPlacement CalculateStatisticsHudPlacement(
        GameClientWindow window,
        bool compact)
    {
        var displayScale = Math.Clamp(window.Height / 1080d, 0.80, 1.50);
        var width = (int)Math.Round(320 * displayScale);
        var height = (int)Math.Round((compact ? 64 : 310) * displayScale);
        var minimapReserve = (int)Math.Round(304 * displayScale);
        var topMargin = (int)Math.Round(44 * displayScale);
        const int edgeMargin = 8;
        var minimumLeft = window.Left + edgeMargin;
        var maximumLeft = Math.Max(minimumLeft, window.Left + window.Width - width - edgeMargin);
        var preferredLeft = window.Left + window.Width - minimapReserve - width;
        var left = Math.Clamp(Quantize(preferredLeft, 4), minimumLeft, maximumLeft);
        var top = Math.Clamp(
            Quantize(window.Top + topMargin, 4),
            window.Top + edgeMargin,
            Math.Max(window.Top + edgeMargin, window.Top + window.Height - height - edgeMargin));
        return new StatisticsHudPlacement(left, top, width, height);
    }

    /// <summary>
    /// 计算开荒地图 HUD 的放置：贴附游戏窗口左上角，尺寸按分辨率与覆屏系数缩放。
    /// 地图只依赖游戏窗口矩形，不依赖角色面板检测；最终位置可在布局编辑态手动调整。
    /// </summary>
    public static MapHudPlacement CalculateMapHudPlacement(
        GameClientWindow window,
        MapHudSettings settings)
    {
        var displayScale = Math.Clamp(window.Height / 1080d, 0.80, 1.50);
        var width = (int)Math.Round(440 * displayScale * settings.OverlayScale);
        var height = (int)Math.Round(520 * displayScale * settings.OverlayScale);
        const int edgeMargin = 12;
        const int topMargin = 56;
        var minimumLeft = window.Left + edgeMargin;
        var maximumLeft = Math.Max(minimumLeft, window.Left + window.Width - width - edgeMargin);
        var minimumTop = window.Top + edgeMargin;
        var maximumTop = Math.Max(minimumTop, window.Top + window.Height - height - edgeMargin);
        var left = Math.Clamp(Quantize(window.Left + edgeMargin, 4), minimumLeft, maximumLeft);
        var top = Math.Clamp(Quantize(window.Top + topMargin, 4), minimumTop, maximumTop);
        return new MapHudPlacement(left, top, width, height);
    }

    public static PixelRect CalculateStatisticsHudOcrExclusion(GameClientWindow window)
    {
        // Mask the expanded footprint even in compact mode. During a mode
        // switch, the captured desktop can still contain the previous expanded
        // frame until UpdatePlacement is rendered.
        var placement = CalculateStatisticsHudPlacement(window, compact: false);
        return new PixelRect(
            placement.Left - window.Left,
            placement.Top - window.Top,
            placement.Width,
            placement.Height);
    }

    private void BeginLayoutEditing()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var gameWindow = _windowLocator.FindDiabloWindow();
        if (gameWindow is null || _lastPanel is null)
        {
            LayoutStatus = "请先打开游戏角色面板，等待 HUD 显示后再调整";
            return;
        }

        _lastWindow = gameWindow;
        _layoutClientWidth = gameWindow.Value.Width;
        _layoutClientHeight = gameWindow.Value.Height;
        _layoutSnapshot = CaptureLayout(SelectedProfile);
        _activeLayoutIdentity = string.Empty;
        HudProfileFactory.ResetLayout(SelectedProfile);
        HudLayoutTemplateService.Apply(SelectedProfile, _layoutClientWidth, _layoutClientHeight);
        CurrentProfile = SelectedProfile;
        IsLayoutEditing = true;
        LayoutStatus = $"正在调整 {SelectedProfile.Name} · {LayoutResolutionLabel} · 拖动词缀块到目标位置";
        OnPropertyChanged(nameof(LayoutResolutionLabel));
        UpdateHudPlacement(gameWindow.Value, _lastPanel.Value);
    }

    private void SaveLayoutTemplate()
    {
        if (!IsLayoutEditing || SelectedProfile is null)
        {
            return;
        }

        HudLayoutTemplateService.Capture(SelectedProfile, _layoutClientWidth, _layoutClientHeight);
        Save(false);
        FinishLayoutEditing($"已保存 {SelectedProfile.Name} · {LayoutResolutionLabel} 模板");
    }

    private void ResetLayout()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        HudProfileFactory.ResetLayout(SelectedProfile);
        LayoutStatus = $"已恢复默认位置 · {LayoutResolutionLabel}，保存后生效";
        OnPropertyChanged(nameof(VisibleHudRules));
    }

    private void PollLayoutEditing()
    {
        if (_lastWindow is null || _lastPanel is null)
        {
            return;
        }

        CurrentProfile = SelectedProfile;
        UpdateHudPlacement(_lastWindow.Value, _lastPanel.Value);
    }

    private void FinishLayoutEditing(string status)
    {
        _layoutSnapshot = null;
        IsLayoutEditing = false;
        LayoutStatus = status;
        _activeLayoutIdentity = string.Empty;
        Save(false);
    }

    private static Dictionary<EquipmentSlotKind, HudSlotLayout> CaptureLayout(BuildProfile profile) =>
        profile.EquipmentRules.ToDictionary(
            rule => rule.Slot,
            rule => new HudSlotLayout
            {
                Slot = rule.Slot,
                AnchorX = rule.AnchorX,
                AnchorY = rule.AnchorY,
                DisplayWidth = rule.DisplayWidth
            });

    private static void RestoreLayout(
        BuildProfile profile,
        IReadOnlyDictionary<EquipmentSlotKind, HudSlotLayout> snapshot)
    {
        foreach (var rule in profile.EquipmentRules)
        {
            if (!snapshot.TryGetValue(rule.Slot, out var layout))
            {
                continue;
            }

            rule.AnchorX = layout.AnchorX;
            rule.AnchorY = layout.AnchorY;
            rule.DisplayWidth = layout.DisplayWidth;
        }
    }

    private void SetSelectedProfileWaitingStatus(string status)
    {
        CurrentProfile = SelectedProfile;
        RecognitionConfidence = 0;
        RecognitionStatus = SelectedProfile is null
            ? "BD 未识别"
            : $"已选择 BD · {SelectedProfile.Name} · {status}";
    }

    private void UpdateHudPlacement(GameClientWindow window, PanelDetection panel)
    {
        ApplyLayoutTemplate(window);
        var panelLeft = window.Left + (int)Math.Round(panel.Bounds.X * window.Width);
        var panelTop = window.Top + (int)Math.Round(panel.Bounds.Y * window.Height);
        var availableWidth = panel.Bounds.Width * window.Width;
        var availableHeight = panel.Bounds.Height * window.Height;
        var scale = Math.Min(
            availableWidth / HudLayoutMetrics.DesignWidth,
            availableHeight / HudLayoutMetrics.DesignHeight);
        var hudWidth = (int)Math.Round(HudLayoutMetrics.DesignWidth * scale);
        var hudHeight = (int)Math.Round(HudLayoutMetrics.DesignHeight * scale);
        HudPlacementChanged?.Invoke(new HudPlacement(panelLeft, panelTop, hudWidth, hudHeight));
    }

    private void ApplyLayoutTemplate(GameClientWindow window)
    {
        var profile = CurrentProfile ?? SelectedProfile;
        if (profile is null || IsLayoutEditing)
        {
            return;
        }

        var identity = $"{profile.Id}:{window.Width}x{window.Height}";
        if (string.Equals(identity, _activeLayoutIdentity, StringComparison.Ordinal))
        {
            return;
        }

        HudProfileFactory.ResetLayout(profile);
        var applied = HudLayoutTemplateService.Apply(profile, window.Width, window.Height);
        _activeLayoutIdentity = identity;
        _layoutClientWidth = window.Width;
        _layoutClientHeight = window.Height;
        OnPropertyChanged(nameof(LayoutResolutionLabel));
        LayoutStatus = applied
            ? $"已加载 {profile.Name} · {LayoutResolutionLabel} 模板"
            : $"{profile.Name} · {LayoutResolutionLabel} 尚未保存专用模板";
    }

    private void WireDocument()
    {
        Document.Profiles.CollectionChanged += ProfilesCollectionChanged;
        Document.Overlay.PropertyChanged += PersistedPropertyChanged;
        foreach (var profile in Document.Profiles)
        {
            WireProfile(profile);
        }
    }

    private void UnwireDocument()
    {
        Document.Profiles.CollectionChanged -= ProfilesCollectionChanged;
        Document.Overlay.PropertyChanged -= PersistedPropertyChanged;
        foreach (var profile in Document.Profiles)
        {
            UnwireProfile(profile);
        }
    }

    private void ProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BuildProfile profile in e.OldItems)
            {
                UnwireProfile(profile);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BuildProfile profile in e.NewItems)
            {
                WireProfile(profile);
            }
        }

        OnPropertyChanged(nameof(Profiles));
        RefreshFilterOptions();
        OnPropertyChanged(nameof(FilteredProfiles));
        OnPropertyChanged(nameof(ProfileCountText));
        Save(false);
    }

    private void WireProfile(BuildProfile profile)
    {
        profile.PropertyChanged += PersistedPropertyChanged;
        foreach (var rule in profile.EquipmentRules)
        {
            rule.PropertyChanged += PersistedPropertyChanged;
        }
    }

    private void UnwireProfile(BuildProfile profile)
    {
        profile.PropertyChanged -= PersistedPropertyChanged;
        foreach (var rule in profile.EquipmentRules)
        {
            rule.PropertyChanged -= PersistedPropertyChanged;
        }
    }

    private void PersistedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedProfileSummary));
        OnPropertyChanged(nameof(VisibleHudRules));
        if (sender is BuildProfile && e.PropertyName == nameof(BuildProfile.ClassName))
        {
            RefreshFilterOptions();
            OnPropertyChanged(nameof(FilteredProfiles));
        }
        if (ReferenceEquals(sender, Document.Overlay)
            && e.PropertyName == nameof(OverlaySettings.HudDisplayMode))
        {
            OnPropertyChanged(nameof(HudDisplayMode));
            OnPropertyChanged(nameof(IsCompactHudMode));
            OnPropertyChanged(nameof(IsValuesHudMode));
        }
        if (ReferenceEquals(sender, Document.Overlay)
            && e.PropertyName == nameof(OverlaySettings.DamageStatisticsHudEnabled))
        {
            OnPropertyChanged(nameof(IsDamageStatisticsHudEnabled));
        }
        if (ReferenceEquals(sender, Document.Overlay)
            && e.PropertyName == nameof(OverlaySettings.StatisticsHudCompact))
        {
            OnPropertyChanged(nameof(IsStatisticsHudCompact));
        }
        Save(false);
    }

    private void RefreshFilterOptions()
    {
        var selectedClassName = _selectedClassFilter.ClassName;
        ClassFilters.Clear();
        ClassFilters.Add(new BuildClassFilterOption("全部职业", null));
        foreach (var className in Document.Profiles
                     .Select(profile => profile.ClassName)
                     .Where(className => !string.IsNullOrWhiteSpace(className))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(className => className, StringComparer.Ordinal))
        {
            ClassFilters.Add(new BuildClassFilterOption(className, className));
        }

        _selectedClassFilter = ClassFilters.FirstOrDefault(option =>
            string.Equals(option.ClassName, selectedClassName, StringComparison.Ordinal))
            ?? ClassFilters[0];
        OnPropertyChanged(nameof(SelectedClassFilter));
    }

    private void ApplyProfileFilters()
    {
        OnPropertyChanged(nameof(FilteredProfiles));
        var visibleProfiles = FilteredProfiles.ToList();
        if (visibleProfiles.Count > 0 && (SelectedProfile is null || !visibleProfiles.Contains(SelectedProfile)))
        {
            SelectedProfile = visibleProfiles[0];
        }
    }
}

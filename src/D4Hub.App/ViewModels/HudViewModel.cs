using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Input;
using D4Hub.App.Services;
using D4Hub.Core;

namespace D4Hub.App.ViewModels;

public readonly record struct HudPlacement(int Left, int Top, int Width, int Height);

public readonly record struct TransmutationReminderPlacement(int Left, int Top, int Width, int Height);

public readonly record struct ScreenshotPreview(
    string Path,
    int ImageWidth,
    int ImageHeight,
    NormalizedRect PanelBounds);

public enum HubWorkspace
{
    Overview,
    Hud
}

public sealed class HudViewModel : ObservableObject
{
    private readonly IStateStore _stateStore;
    private readonly GameWindowLocator _windowLocator;
    private readonly ScreenFrameService _screenFrames;
    private readonly CharacterPanelDetector _panelDetector;
    private readonly BuildFingerprintService _fingerprints;
    private readonly TransmutationSceneDetector _transmutationSceneDetector;
    private readonly D2CoreBuildResolver _d2CoreResolver;
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

    public HudViewModel(
        IStateStore stateStore,
        BuildDocument document,
        GameWindowLocator windowLocator,
        ScreenFrameService screenFrames,
        CharacterPanelDetector panelDetector,
        BuildFingerprintService fingerprints,
        TransmutationSceneDetector transmutationSceneDetector,
        D2CoreBuildResolver d2CoreResolver)
    {
        _stateStore = stateStore;
        _document = document;
        _windowLocator = windowLocator;
        _screenFrames = screenFrames;
        _panelDetector = panelDetector;
        _fingerprints = fingerprints;
        _transmutationSceneDetector = transmutationSceneDetector;
        _d2CoreResolver = d2CoreResolver;

        _document.EnsureValid();
        _selectedProfile = Profiles.FirstOrDefault(profile => profile.Id == _document.SelectedProfileId)
            ?? Profiles.FirstOrDefault();
        _selectedRule = _selectedProfile?.EquipmentRules.FirstOrDefault();

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
        CopyDouyinHandleCommand = new RelayCommand(() => CopyDouyinHandleRequested?.Invoke());
        CopyCommunityGroupCommand = new RelayCommand(() => CopyCommunityGroupRequested?.Invoke());

        WireDocument();
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(Profiles));
            OnPropertyChanged(nameof(ProfileCountText));
            OnPropertyChanged(nameof(HudDisplayMode));
            OnPropertyChanged(nameof(IsCompactHudMode));
            OnPropertyChanged(nameof(IsValuesHudMode));
        }
    }

    public IEnumerable<BuildProfile> Profiles => Document.Profiles;

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

    public string WorkspaceSubtitle => IsOverviewPage
        ? "本地游戏辅助工作台"
        : "HUD 叠层与 BD 配置";

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
    public Action<TransmutationReminderPlacement>? TransmutationReminderPlacementChanged { get; set; }
    public Action? TransmutationReminderHidden { get; set; }
    public Action<ScreenshotPreview>? PreviewReady { get; set; }
    public Action<bool>? LayoutEditingChanged { get; set; }

    public void SetCommunityStatus(string message) => CommunityStatus = message;

    public void PollLive()
    {
        if (IsLayoutEditing)
        {
            HideTransmutationReminder();
            PollLayoutEditing();
            return;
        }

        if (!IsTracking || !Document.Overlay.AutoAttach)
        {
            HideTransmutationReminder();
            HudHidden?.Invoke();
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
            CaptureFingerprintCommand.RaiseCanExecuteChanged();
            HideTransmutationReminder();
            HudHidden?.Invoke();
            return;
        }

        try
        {
            var window = gameWindow.Value;
            _lastWindow = window;
            GameStatus = window.IsForeground ? "游戏窗口 · 前台" : "游戏窗口 · 后台";
            if (!window.IsForeground)
            {
                PanelStatus = "切回游戏后继续识别";
                SetSelectedProfileWaitingStatus("切回游戏显示");
                HideTransmutationReminder();
                HudHidden?.Invoke();
                return;
            }

            var frame = _screenFrames.Capture(window);
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
            HideTransmutationReminder();
            HudHidden?.Invoke();
            GameStatus = "画面监测已暂停";
        }
    }

    private bool UpdateTransmutationReminder(GameClientWindow window, PixelFrame frame)
    {
        var detection = _transmutationSceneDetector.Detect(frame);
        if (!detection.IsTransmutationVisible)
        {
            TransmutationReminderHidden?.Invoke();
            return false;
        }

        PanelStatus = $"嬗变物品 · {detection.ContextConfidence:P0}";
        RecognitionStatus = "嬗变改造提醒";
        RecognitionConfidence = detection.ContextConfidence;
        TransmutationReminderPlacementChanged?.Invoke(
            CalculateReminderPlacement(window, detection.SelectedRecipeBounds));
        return true;
    }

    private void HideTransmutationReminder()
    {
        TransmutationReminderHidden?.Invoke();
    }

    private static TransmutationReminderPlacement CalculateReminderPlacement(
        GameClientWindow window,
        NormalizedRect recipeBounds)
    {
        const int width = 272;
        const int height = 52;
        const int horizontalGap = 40;
        const int verticalGap = 14;
        const int margin = 8;
        var recipeLeft = window.Left + (int)Math.Round(recipeBounds.X * window.Width);
        var recipeTop = window.Top + (int)Math.Round(recipeBounds.Y * window.Height);
        var left = Math.Clamp(
            recipeLeft - width - horizontalGap,
            window.Left + margin,
            Math.Max(window.Left + margin, window.Left + window.Width - width - margin));
        var top = Math.Clamp(
            recipeTop - height - verticalGap,
            window.Top + margin,
            Math.Max(window.Top + margin, window.Top + window.Height - height - margin));
        return new TransmutationReminderPlacement(left, top, width, height);
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
        if (ReferenceEquals(sender, Document.Overlay)
            && e.PropertyName == nameof(OverlaySettings.HudDisplayMode))
        {
            OnPropertyChanged(nameof(HudDisplayMode));
            OnPropertyChanged(nameof(IsCompactHudMode));
            OnPropertyChanged(nameof(IsValuesHudMode));
        }
        Save(false);
    }
}

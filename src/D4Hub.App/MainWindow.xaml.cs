using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using D4Hub.App.Services;
using D4Hub.App.ViewModels;
using D4Hub.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Velopack;

namespace D4Hub.App;

public partial class MainWindow : Window
{
    private readonly HudViewModel _viewModel;
    private readonly DispatcherTimer _pollTimer;
    private readonly RealtimeCaptureLifecycle _realtimeCaptureLifecycle;
    private readonly UpdateManager? _updateManager;
    private UpdateInfo? _availableUpdate;
    private bool _isUpdateReady;
    private bool _isUpdateBusy;
    private GlobalHotkeyService? _hotkeyService;
    private OverlayWindow? _overlayWindow;
    private StatisticsOverlayWindow? _statisticsOverlayWindow;
    private TransmutationReminderWindow? _transmutationReminderWindow;
    private MapOverlayWindow? _mapOverlayWindow;
    private PreviewWindow? _previewWindow;
    private readonly ExternalResourceEntry? _helltidesResource;
    private bool _isHelltidesWebViewInitializing;
    private bool _isHelltidesWebViewReady;
    private int _blockedHelltidesRequestCount;

    public MainWindow(HudViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _helltidesResource = viewModel.HelltidesResource;
        DataContext = viewModel;

        var configuredUpdateFeed = AppContext.GetData("D4Hub.UpdateFeedUrl") as string;
        if (string.IsNullOrWhiteSpace(configuredUpdateFeed))
        {
            UpdateButton.IsEnabled = false;
            UpdateButtonText.Text = "本地候选版";
            UpdateStatusText.Text = "未配置线上更新源";
        }
        else if (!Uri.TryCreate(configuredUpdateFeed, UriKind.Absolute, out var updateFeedUri)
            || updateFeedUri.Scheme != Uri.UriSchemeHttps)
        {
            UpdateButton.IsEnabled = false;
            UpdateButtonText.Text = "更新未配置";
            UpdateStatusText.Text = "更新源必须使用 HTTPS";
        }
        else
        {
            try
            {
                var updateManager = new UpdateManager(updateFeedUri.AbsoluteUri.TrimEnd('/'));
                if (updateManager.IsInstalled)
                {
                    _updateManager = updateManager;
                }
                else
                {
                    UpdateButton.IsEnabled = false;
                    UpdateButtonText.Text = "便携版";
                    UpdateStatusText.Text = "安装版支持自动更新";
                }
            }
            catch (Exception)
            {
                UpdateButton.IsEnabled = false;
                UpdateStatusText.Text = "更新服务不可用";
            }
        }

        _viewModel.RemoveProfileRequested = ConfirmRemoveProfile;
        _viewModel.ScreenshotRequested = SelectScreenshot;
        _viewModel.ImportRequested = ImportProfiles;
        _viewModel.ExportRequested = ExportProfiles;
        _viewModel.PasteD2CoreRequested = PasteAndImportD2Core;
        _viewModel.LootFilters.CopyRequested = CopyLootFilter;
        _viewModel.CopyDouyinHandleRequested = () => CopyCommunityText(
            "loco",
            "已复制抖音号 @loco，去抖音搜索关注。");
        _viewModel.CopyCommunityGroupRequested = () => CopyCommunityText(
            "736495487",
            "已复制 QQ 群号 736495487。");
        _viewModel.HudPlacementChanged = placement => Dispatcher.Invoke(() => _overlayWindow?.UpdatePlacement(placement));
        _viewModel.HudHidden = () => Dispatcher.Invoke(() => _overlayWindow?.HideHud());
        _viewModel.StatisticsHudPlacementChanged = placement =>
            Dispatcher.Invoke(() => _statisticsOverlayWindow?.UpdatePlacement(placement));
        _viewModel.StatisticsHudHidden = () =>
            Dispatcher.Invoke(() => _statisticsOverlayWindow?.HideHud());
        _viewModel.TransmutationReminderPlacementChanged = placement =>
            Dispatcher.Invoke(() => _transmutationReminderWindow?.UpdatePlacement(placement));
        _viewModel.TransmutationReminderHidden = () =>
            Dispatcher.Invoke(() => _transmutationReminderWindow?.HideReminder());
        _viewModel.MapHudPlacementChanged = placement =>
            Dispatcher.Invoke(() => _mapOverlayWindow?.UpdatePlacement(placement));
        _viewModel.MapHudHidden = () =>
            Dispatcher.Invoke(() => _mapOverlayWindow?.HideHud());
        _viewModel.MapHudRefreshRequested = () =>
            Dispatcher.Invoke(() => _mapOverlayWindow?.RefreshMap());
        _viewModel.PreviewReady = ShowPreview;
        _viewModel.LayoutEditingChanged = isEditing => Dispatcher.Invoke(() =>
        {
            _overlayWindow?.SetLayoutEditing(isEditing);
            _mapOverlayWindow?.SetLayoutEditing(isEditing);
            if (isEditing)
            {
                _overlayWindow?.Show();
                Hide();
            }
            else if (!IsVisible)
            {
                Show();
                Activate();
            }
        });

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _pollTimer.Tick += (_, _) => _viewModel.PollLive();
        _realtimeCaptureLifecycle = new RealtimeCaptureLifecycle(
            _pollTimer.Start,
            _pollTimer.Stop);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _hotkeyService = new GlobalHotkeyService(this);
        _viewModel.MapHudSettings.PropertyChanged += OnMapHudSettingChanged;
        RegisterGlobalHotkeys();
    }

    /// <summary>
    /// 注册全局热键：F2 跟踪开关 + 地图 HUD 三个可配置热键（切换显隐/重绘/重置）。
    /// 配置变更时整体重注册；单个热键注册失败（被占用）不影响其余。
    /// </summary>
    private void RegisterGlobalHotkeys()
    {
        if (_hotkeyService is null)
        {
            return;
        }

        _hotkeyService.UnregisterAll();
        _hotkeyService.TryRegister(
            Key.F2,
            ModifierKeys.None,
            () => Dispatcher.Invoke(() => _viewModel.ToggleTrackingCommand.Execute(null)));
        RegisterMapHotkey(_viewModel.MapHudSettings.HotkeyToggle, _viewModel.ToggleMapHudVisibility);
        RegisterMapHotkey(_viewModel.MapHudSettings.HotkeyRedraw, _viewModel.RedrawMap);
        RegisterMapHotkey(_viewModel.MapHudSettings.HotkeyResetPlacement, _viewModel.ResetMapPlacement);
    }

    private void RegisterMapHotkey(string keyName, Action action)
    {
        if (string.IsNullOrWhiteSpace(keyName)
            || !Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)
            || key == Key.None)
        {
            return;
        }

        _hotkeyService?.TryRegister(key, ModifierKeys.None, () => Dispatcher.Invoke(action));
    }

    private void OnMapHudSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MapHudSettings.HotkeyToggle)
            or nameof(MapHudSettings.HotkeyRedraw)
            or nameof(MapHudSettings.HotkeyResetPlacement)))
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(RegisterGlobalHotkeys));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _overlayWindow ??= new OverlayWindow(_viewModel);
        _statisticsOverlayWindow ??= new StatisticsOverlayWindow(_viewModel);
        _transmutationReminderWindow ??= new TransmutationReminderWindow(_viewModel);
        _mapOverlayWindow ??= new MapOverlayWindow(_viewModel);
        if (_realtimeCaptureLifecycle.Start())
        {
            _viewModel.PollLive();
        }

        await CheckForUpdatesAsync(showResult: false);
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateManager is null || _isUpdateBusy)
        {
            return;
        }

        if (_availableUpdate is not null && _isUpdateReady)
        {
            PromptToApplyDownloadedUpdate(_availableUpdate);
            return;
        }

        if (_availableUpdate is null)
        {
            await CheckForUpdatesAsync(showResult: true);
            if (_availableUpdate is null)
            {
                return;
            }
        }

        await DownloadAndApplyUpdateAsync(_availableUpdate);
    }

    private async Task CheckForUpdatesAsync(bool showResult)
    {
        if (_updateManager is null || _isUpdateBusy)
        {
            return;
        }

        SetUpdateBusy(true, "正在检查更新…");
        try
        {
            _availableUpdate = await _updateManager.CheckForUpdatesAsync().ConfigureAwait(true);
            if (_availableUpdate is null)
            {
                _isUpdateReady = false;
                UpdateButtonText.Text = "已是最新版";
                UpdateStatusText.Text = $"DHub {_updateManager.CurrentVersion} · stable";
                if (showResult)
                {
                    MessageBox.Show(this, "当前已经是最新稳定版。", "DHub 更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                _isUpdateReady = false;
                var version = _availableUpdate.TargetFullRelease.Version;
                UpdateButtonText.Text = $"更新到 {version}";
                UpdateStatusText.Text = $"新版本 {version} 可用";
            }
        }
        catch (Exception exception)
        {
            UpdateButtonText.Text = "重试更新";
            UpdateStatusText.Text = "暂时无法检查更新";
            if (showResult)
            {
                MessageBox.Show(
                    this,
                    $"无法连接更新服务。当前版本仍可离线使用。\n\n{exception.Message}",
                    "DHub 更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

    private async Task DownloadAndApplyUpdateAsync(UpdateInfo update)
    {
        var version = update.TargetFullRelease.Version;
        var decision = MessageBox.Show(
            this,
            $"下载 DHub {version}？下载完成后会保存当前设置并重启安装。",
            "DHub 更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        SetUpdateBusy(true, "正在下载 0%…");
        try
        {
            await _updateManager!.DownloadUpdatesAsync(
                update,
                progress => Dispatcher.Invoke(() =>
                {
                    UpdateButtonText.Text = $"下载 {progress}%";
                    UpdateStatusText.Text = $"正在校验更新 · {progress}%";
                })).ConfigureAwait(true);

            _isUpdateReady = true;
            SetUpdateBusy(false);
            PromptToApplyDownloadedUpdate(update);
        }
        catch (Exception exception)
        {
            _isUpdateReady = false;
            UpdateButtonText.Text = $"更新到 {version}";
            UpdateStatusText.Text = "下载或校验失败";
            MessageBox.Show(
                this,
                $"更新未安装，当前版本没有变化。\n\n{exception.Message}",
                "DHub 更新",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

    private void PromptToApplyDownloadedUpdate(UpdateInfo update)
    {
        var version = update.TargetFullRelease.Version;
        var restart = MessageBox.Show(
            this,
            $"DHub {version} 已下载并通过校验。现在重启完成更新？",
            "DHub 更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);
        if (restart != MessageBoxResult.Yes)
        {
            UpdateButtonText.Text = "重启并更新";
            UpdateStatusText.Text = $"DHub {version} 已就绪";
            return;
        }

        try
        {
            _viewModel.Save(false);
            _updateManager!.ApplyUpdatesAndRestart(update);
        }
        catch (Exception exception)
        {
            UpdateButtonText.Text = "重试重启更新";
            UpdateStatusText.Text = $"DHub {version} 已下载，重启失败";
            MessageBox.Show(
                this,
                $"更新已经下载，但暂时无法启动安装程序。当前版本没有变化。\n\n{exception.Message}",
                "DHub 更新",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SetUpdateBusy(bool isBusy, string? status = null)
    {
        _isUpdateBusy = isBusy;
        UpdateButton.IsEnabled = !isBusy && _updateManager is not null;
        if (status is not null)
        {
            UpdateButtonText.Text = status;
            UpdateStatusText.Text = status;
        }
    }

    private void Window_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= Window_ContentRendered;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            if (RootGrid.RenderTransform is TranslateTransform slide)
            {
                slide.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(340))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _realtimeCaptureLifecycle.Stop();
        HelltidesWebView.Dispose();
        _viewModel.CancelLayoutEditing();
        _viewModel.Save(false);
        _hotkeyService?.Dispose();
        _previewWindow?.Close();
        if (_overlayWindow is not null)
        {
            _overlayWindow.AllowClose = true;
            _overlayWindow.Close();
        }

        if (_statisticsOverlayWindow is not null)
        {
            _statisticsOverlayWindow.AllowClose = true;
            _statisticsOverlayWindow.Close();
        }

        if (_transmutationReminderWindow is not null)
        {
            _transmutationReminderWindow.AllowClose = true;
            _transmutationReminderWindow.Close();
        }
    }

    private void CaptionMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CaptionMaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CaptionClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaxRestoreGlyph.Text = maximized ? "\uE923" : "\uE922";
        WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
    }

    private async void ResourcesNavigation_Checked(object sender, RoutedEventArgs e) =>
        await EnsureHelltidesWebViewAsync();

    private async Task EnsureHelltidesWebViewAsync()
    {
        if (_helltidesResource is null)
        {
            ShowHelltidesError("地图资源不可用", "内置资源清单未通过校验，已阻止网页加载。");
            return;
        }

        if (_isHelltidesWebViewReady)
        {
            if (HelltidesWebView.Source is null)
            {
                NavigateHelltidesHome();
            }

            return;
        }

        if (_isHelltidesWebViewInitializing)
        {
            return;
        }

        _isHelltidesWebViewInitializing = true;
        ShowHelltidesLoading();
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "D4Hub",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.IsInPrivateModeEnabled = true;
            await HelltidesWebView.EnsureCoreWebView2Async(environment, controllerOptions);
            await ConfigureHelltidesWebViewAsync();
            _isHelltidesWebViewReady = true;
            UpdateHelltidesPrivacyStatus();
            NavigateHelltidesHome();
        }
        catch (Exception)
        {
            ShowHelltidesError(
                "内嵌地图无法启动",
                "请检查网络以及 Microsoft Edge WebView2 Runtime，或使用系统浏览器打开。");
        }
        finally
        {
            _isHelltidesWebViewInitializing = false;
        }
    }

    private async Task ConfigureHelltidesWebViewAsync()
    {
        var core = HelltidesWebView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (!HelltidesPrivacyPolicy.ShouldBlockRequest(args.Request.Uri))
            {
                return;
            }

            args.Response = core.Environment.CreateWebResourceResponse(
                Stream.Null,
                204,
                "No Content",
                "Content-Length: 0\r\nCache-Control: no-store");
            _blockedHelltidesRequestCount++;
            UpdateHelltidesPrivacyStatus();
        };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(HelltidesPrivacyPolicy.DomSanitizerScript);

        core.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedHelltidesNavigation(args.Uri))
            {
                args.Cancel = true;
                Dispatcher.BeginInvoke(() => ConfirmAndOpenExternalUri(args.Uri));
                return;
            }

            ShowHelltidesLoading();
        };
        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
            {
                HelltidesLoadingOverlay.Visibility = Visibility.Collapsed;
                HelltidesErrorOverlay.Visibility = Visibility.Collapsed;
                HelltidesWebView.Visibility = Visibility.Visible;
                return;
            }

            ShowHelltidesError("地图暂时无法加载", "Helltides.com 没有完成响应，请稍后重试。");
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            Dispatcher.BeginInvoke(() => ConfirmAndOpenExternalUri(args.Uri));
        };
        core.DownloadStarting += (_, args) =>
        {
            args.Cancel = true;
            Dispatcher.BeginInvoke(() => MessageBox.Show(
                this,
                "内嵌地图不允许下载文件。",
                "DHub 地图工具",
                MessageBoxButton.OK,
                MessageBoxImage.Information));
        };
        core.PermissionRequested += (_, args) =>
        {
            args.State = CoreWebView2PermissionState.Deny;
            args.Handled = true;
        };
    }

    private void UpdateHelltidesPrivacyStatus()
    {
        HelltidesPrivacyStatusText.Text =
            $"隐私模式 · 临时会话 · 已拦截 {_blockedHelltidesRequestCount} 个追踪/广告请求";
    }

    private void NavigateHelltidesHome()
    {
        if (_helltidesResource is null || !_isHelltidesWebViewReady)
        {
            return;
        }

        ShowHelltidesLoading();
        HelltidesWebView.CoreWebView2.Navigate(_helltidesResource.GetLaunchUri().AbsoluteUri);
    }

    private bool IsAllowedHelltidesNavigation(string candidate)
    {
        if (_helltidesResource is null
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort)
        {
            return false;
        }

        var allowedHost = _helltidesResource.GetLaunchUri().IdnHost;
        return string.Equals(uri.IdnHost, allowedHost, StringComparison.Ordinal);
    }

    private void ShowHelltidesLoading()
    {
        HelltidesWebView.Visibility = Visibility.Collapsed;
        HelltidesErrorOverlay.Visibility = Visibility.Collapsed;
        HelltidesLoadingOverlay.Visibility = Visibility.Visible;
    }

    private void ShowHelltidesError(string title, string message)
    {
        HelltidesWebView.Visibility = Visibility.Collapsed;
        HelltidesLoadingOverlay.Visibility = Visibility.Collapsed;
        HelltidesErrorTitle.Text = title;
        HelltidesErrorMessage.Text = message;
        HelltidesErrorOverlay.Visibility = Visibility.Visible;
    }

    private async void RetryHelltides_Click(object sender, RoutedEventArgs e)
    {
        if (_isHelltidesWebViewReady)
        {
            NavigateHelltidesHome();
            return;
        }

        await EnsureHelltidesWebViewAsync();
    }

    private async void ReloadHelltides_Click(object sender, RoutedEventArgs e)
    {
        if (!_isHelltidesWebViewReady)
        {
            await EnsureHelltidesWebViewAsync();
            return;
        }

        ShowHelltidesLoading();
        HelltidesWebView.Reload();
    }

    private void OpenHelltidesInBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (_helltidesResource is null)
        {
            ShowHelltidesError("地图资源不可用", "内置资源清单未通过校验，已阻止网页打开。");
            return;
        }

        ConfirmAndOpenExternalUri(_helltidesResource.GetLaunchUri().AbsoluteUri);
    }

    private void ConfirmAndOpenExternalUri(string candidate)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || uri.HostNameType != UriHostNameType.Dns)
        {
            MessageBox.Show(
                this,
                "该链接未通过 HTTPS 安全校验，已阻止打开。",
                "DHub 地图工具",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"即将在系统默认浏览器打开第三方站点：\n\n{uri.IdnHost}\n\n离开 DHub 后，内容、Cookie 和隐私处理由该站点负责。继续打开？",
            "打开第三方站点",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            MessageBox.Show(
                this,
                "系统浏览器未能打开该地址。",
                "DHub 地图工具",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SelectScreenshot()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择打开角色面板的 D4 截图",
            Filter = "图像文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.LearnFromScreenshot(dialog.FileName);
        }
    }

    private void ShowPreview(ScreenshotPreview preview)
    {
        _previewWindow?.Close();
        _previewWindow = new PreviewWindow(_viewModel, preview) { Owner = this };
        _previewWindow.Show();
    }

    private void ConfirmRemoveProfile()
    {
        if (_viewModel.SelectedProfile is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"删除 BD“{_viewModel.SelectedProfile.Name}”？",
            "DHub",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.RemoveSelectedProfile();
        }
    }

    private void ImportProfiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 DHub BD 库",
            Filter = "DHub 数据 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ImportFrom(dialog.FileName);
        }
    }

    private void ExportProfiles()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 DHub BD 库",
            Filter = "DHub 数据 (*.json)|*.json",
            FileName = "dhub-build-library.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ExportTo(dialog.FileName);
        }
    }

    private void PasteAndImportD2Core()
    {
        if (!Clipboard.ContainsText())
        {
            MessageBox.Show(this, "剪贴板中没有暗黑核 BD 链接。", "DHub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _viewModel.D2CoreUrl = Clipboard.GetText();
        _ = _viewModel.ImportD2CoreAsync(_viewModel.D2CoreUrl);
    }

    private void CopyCommunityText(string value, string copiedStatus)
    {
        try
        {
            Clipboard.SetText(value);
            _viewModel.SetCommunityStatus(copiedStatus);
        }
        catch (Exception)
        {
            _viewModel.SetCommunityStatus("复制失败，请手动记录页面中的账号或群号。");
        }
    }

    private void CopyLootFilter(string code)
    {
        try
        {
            Clipboard.SetText(code);
        }
        catch (Exception)
        {
            _viewModel.LootFilters.SetStatus("复制失败，请检查系统剪贴板状态。");
        }
    }

    private void MapImageBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择地图底图目录（每区域一张 {区域key}.png）"
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.MapHudSettings.MapImagePath = dialog.FolderName;
        }
    }

    private void PoiBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择社区 POI JSON 文件",
            Filter = "JSON 文件 (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.MapHudSettings.PoiDataPath = dialog.FileName;
        }
    }

    private void AudioBrowse_Click(object sender, RoutedEventArgs e)
    {
        var slot = (sender as FrameworkElement)?.Tag as string;
        var dialog = new OpenFileDialog
        {
            Title = "选择音频提醒文件",
            Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true || slot is null)
        {
            return;
        }

        switch (slot)
        {
            case "boss":
                _viewModel.MapHudSettings.AudioBossPath = dialog.FileName;
                break;
            case "elite":
                _viewModel.MapHudSettings.AudioElitePath = dialog.FileName;
                break;
            case "blue":
                _viewModel.MapHudSettings.AudioBluePath = dialog.FileName;
                break;
        }
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using D4Hub.App.Services;
using D4Hub.App.ViewModels;
using Microsoft.Win32;
using Velopack;

namespace D4Hub.App;

public partial class MainWindow : Window
{
    private readonly HudViewModel _viewModel;
    private readonly DispatcherTimer _pollTimer;
    private readonly UpdateManager? _updateManager;
    private UpdateInfo? _availableUpdate;
    private bool _isUpdateReady;
    private bool _isUpdateBusy;
    private GlobalHotkeyService? _hotkeyService;
    private OverlayWindow? _overlayWindow;
    private TransmutationReminderWindow? _transmutationReminderWindow;
    private PreviewWindow? _previewWindow;

    public MainWindow(HudViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
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
        _viewModel.CopyDouyinHandleRequested = () => CopyCommunityText(
            "loco",
            "已复制抖音号 @loco，去抖音搜索关注。");
        _viewModel.CopyCommunityGroupRequested = () => CopyCommunityText(
            "736495487",
            "已复制 QQ 群号 736495487。");
        _viewModel.HudPlacementChanged = placement => Dispatcher.Invoke(() => _overlayWindow?.UpdatePlacement(placement));
        _viewModel.HudHidden = () => Dispatcher.Invoke(() => _overlayWindow?.HideHud());
        _viewModel.TransmutationReminderPlacementChanged = placement =>
            Dispatcher.Invoke(() => _transmutationReminderWindow?.UpdatePlacement(placement));
        _viewModel.TransmutationReminderHidden = () =>
            Dispatcher.Invoke(() => _transmutationReminderWindow?.HideReminder());
        _viewModel.PreviewReady = ShowPreview;
        _viewModel.LayoutEditingChanged = isEditing => Dispatcher.Invoke(() =>
        {
            _overlayWindow?.SetLayoutEditing(isEditing);
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
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _hotkeyService = new GlobalHotkeyService(this);
        _hotkeyService.TryRegister(
            Key.F2,
            ModifierKeys.None,
            () => Dispatcher.Invoke(() => _viewModel.ToggleTrackingCommand.Execute(null)));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _overlayWindow = new OverlayWindow(_viewModel);
        _transmutationReminderWindow = new TransmutationReminderWindow(_viewModel);
        _pollTimer.Start();
        _viewModel.PollLive();
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
        _pollTimer.Stop();
        _viewModel.CancelLayoutEditing();
        _viewModel.Save(false);
        _hotkeyService?.Dispose();
        _previewWindow?.Close();
        if (_overlayWindow is not null)
        {
            _overlayWindow.AllowClose = true;
            _overlayWindow.Close();
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
}

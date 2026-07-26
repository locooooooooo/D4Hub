using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using D4Hub.App.Services;
using D4Hub.App.ViewModels;
using Microsoft.Win32;

namespace D4Hub.App;

public partial class MainWindow : Window
{
    private readonly HudViewModel _viewModel;
    private readonly DispatcherTimer _pollTimer;
    private GlobalHotkeyService? _hotkeyService;
    private OverlayWindow? _overlayWindow;
    private TransmutationReminderWindow? _transmutationReminderWindow;
    private PreviewWindow? _previewWindow;

    public MainWindow(HudViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _overlayWindow = new OverlayWindow(_viewModel);
        _transmutationReminderWindow = new TransmutationReminderWindow(_viewModel);
        _pollTimer.Start();
        _viewModel.PollLive();
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

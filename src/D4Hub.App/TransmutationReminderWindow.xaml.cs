using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using D4Hub.App.Services;
using System.Windows.Media;
using D4Hub.App.ViewModels;

namespace D4Hub.App;

public partial class TransmutationReminderWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);
    private IntPtr _handle;
    private TransmutationReminderPlacement? _lastPlacement;

    public TransmutationReminderWindow(HudViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public bool AllowClose { get; set; }

    public void UpdatePlacement(TransmutationReminderPlacement placement)
    {
        EnsureHandle();
        if (IsVisible && _lastPlacement == placement)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        Width = placement.Width / dpi.DpiScaleX;
        Height = placement.Height / dpi.DpiScaleY;
        if (!IsVisible)
        {
            ApplyPlacement(placement, show: false);
            Show();
        }

        ApplyPlacement(placement, show: true);
        _lastPlacement = placement;
    }

    private void EnsureHandle()
    {
        if (_handle == IntPtr.Zero)
        {
            _handle = new WindowInteropHelper(this).EnsureHandle();
        }
    }

    private void ApplyPlacement(TransmutationReminderPlacement placement, bool show)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            _handle,
            show ? HwndTopmost : IntPtr.Zero,
            placement.Left,
            placement.Top,
            Math.Max(1, placement.Width),
            Math.Max(1, placement.Height),
            SwpNoActivate | (show ? SwpShowWindow : SwpNoZOrder));
    }

    public void HideReminder()
    {
        if (IsVisible)
        {
            Hide();
        }

        _lastPlacement = null;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        OverlayCapturePolicy.ExcludeFromCapture(_handle);
        var styles = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(_handle, GwlExStyle, new IntPtr(styles | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));

    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr handle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

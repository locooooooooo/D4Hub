using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using D4Hub.App.Services;
using D4Hub.App.ViewModels;

namespace D4Hub.App;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);
    private IntPtr _handle;
    private bool _isLayoutEditing;

    public OverlayWindow(HudViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public bool AllowClose { get; set; }

    public void UpdatePlacement(HudPlacement placement)
    {
        if (!IsVisible)
        {
            Show();
        }

        if (_handle != IntPtr.Zero)
        {
            SetWindowPos(
                _handle,
                HwndTopmost,
                placement.Left,
                placement.Top,
                Math.Max(1, placement.Width),
                Math.Max(1, placement.Height),
                SwpNoActivate | SwpShowWindow);
        }
    }

    public void HideHud()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    public void SetLayoutEditing(bool isEditing)
    {
        _isLayoutEditing = isEditing;
        Focusable = isEditing;
        EnsureHandle();
        ApplyInteractionStyle();
        if (isEditing && IsVisible)
        {
            Dispatcher.BeginInvoke(new Action(ActivateForEditing));
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        OverlayCapturePolicy.ExcludeFromCapture(_handle);
        ApplyInteractionStyle();
    }

    private void EnsureHandle()
    {
        if (_handle == IntPtr.Zero)
        {
            _handle = new WindowInteropHelper(this).EnsureHandle();
            OverlayCapturePolicy.ExcludeFromCapture(_handle);
        }
    }

    private void ApplyInteractionStyle()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var styles = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        styles |= WsExToolWindow;
        if (_isLayoutEditing)
        {
            styles &= ~WsExTransparent;
            styles &= ~WsExNoActivate;
        }
        else
        {
            styles |= WsExTransparent | WsExNoActivate;
        }

        SetWindowLongPtr(_handle, GwlExStyle, new IntPtr(styles));
        SetWindowPos(
            _handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private void ActivateForEditing()
    {
        if (_isLayoutEditing && IsVisible)
        {
            Activate();
            Focus();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isLayoutEditing
            || e.Key != Key.Escape
            || DataContext is not HudViewModel viewModel
            || !viewModel.CancelLayoutEditingCommand.CanExecute(null))
        {
            return;
        }

        viewModel.CancelLayoutEditingCommand.Execute(null);
        e.Handled = true;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

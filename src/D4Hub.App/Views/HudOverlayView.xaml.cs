using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using D4Hub.Core;

namespace D4Hub.App.Views;

public partial class HudOverlayView : UserControl
{
    public static readonly DependencyProperty ProfileNameProperty = DependencyProperty.Register(
        nameof(ProfileName),
        typeof(string),
        typeof(HudOverlayView),
        new PropertyMetadata("未识别 BD"));

    public static readonly DependencyProperty RulesProperty = DependencyProperty.Register(
        nameof(Rules),
        typeof(IEnumerable),
        typeof(HudOverlayView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode),
        typeof(HudDisplayMode),
        typeof(HudOverlayView),
        new PropertyMetadata(HudDisplayMode.Compact));

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing),
        typeof(bool),
        typeof(HudOverlayView),
        new PropertyMetadata(false));

    private EquipmentAffixRule? _draggedRule;
    private Point _dragOffset;

    public HudOverlayView()
    {
        InitializeComponent();
    }

    public string ProfileName
    {
        get => (string)GetValue(ProfileNameProperty);
        set => SetValue(ProfileNameProperty, value);
    }

    public IEnumerable? Rules
    {
        get => (IEnumerable?)GetValue(RulesProperty);
        set => SetValue(RulesProperty, value);
    }

    public HudDisplayMode DisplayMode
    {
        get => (HudDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    private void Rule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditing || sender is not FrameworkElement { DataContext: EquipmentAffixRule rule })
        {
            return;
        }

        var point = e.GetPosition(DesignCanvas);
        _draggedRule = rule;
        _dragOffset = new Point(point.X - rule.AnchorX, point.Y - rule.AnchorY);
        DesignCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void DesignCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedRule is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(DesignCanvas);
        _draggedRule.AnchorX = Math.Round(Math.Clamp(
            point.X - _dragOffset.X,
            0,
            HudLayoutMetrics.DesignWidth - _draggedRule.DisplayWidth));
        _draggedRule.AnchorY = Math.Round(Math.Clamp(
            point.Y - _dragOffset.Y,
            46,
            HudLayoutMetrics.DesignHeight - 20));
        e.Handled = true;
    }

    private void DesignCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void DesignCanvas_LostMouseCapture(object sender, MouseEventArgs e) => _draggedRule = null;

    private void EndDrag()
    {
        _draggedRule = null;
        if (DesignCanvas.IsMouseCaptured)
        {
            DesignCanvas.ReleaseMouseCapture();
        }
    }
}

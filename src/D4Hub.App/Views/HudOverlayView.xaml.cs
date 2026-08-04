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
    private FrameworkElement? _draggedElement;
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
        _draggedElement = (FrameworkElement)sender;
        _dragOffset = new Point(point.X - rule.AnchorX, point.Y - rule.AnchorY);
        _draggedElement.Focus();
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
        MoveRule(
            _draggedRule,
            point.X - _dragOffset.X,
            point.Y - _dragOffset.Y,
            _draggedElement?.ActualHeight ?? 0);
        e.Handled = true;
    }

    private void Rule_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEditing || sender is not FrameworkElement { DataContext: EquipmentAffixRule rule } element)
        {
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var (deltaX, deltaY) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0)
        };
        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        MoveRule(rule, rule.AnchorX + deltaX, rule.AnchorY + deltaY, element.ActualHeight);
        e.Handled = true;
    }

    private void DesignCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void DesignCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _draggedRule = null;
        _draggedElement = null;
    }

    private void EndDrag()
    {
        _draggedRule = null;
        _draggedElement = null;
        if (DesignCanvas.IsMouseCaptured)
        {
            DesignCanvas.ReleaseMouseCapture();
        }
    }

    private static void MoveRule(EquipmentAffixRule rule, double x, double y, double renderedHeight)
    {
        var width = double.IsFinite(rule.DisplayWidth)
            ? Math.Clamp(rule.DisplayWidth, 1, HudLayoutMetrics.DesignWidth)
            : 1;
        var height = double.IsFinite(renderedHeight)
            ? Math.Clamp(renderedHeight, 20, HudLayoutMetrics.DesignHeight - 46)
            : 20;
        var maxX = Math.Max(0, HudLayoutMetrics.DesignWidth - width);
        var maxY = Math.Max(46, HudLayoutMetrics.DesignHeight - height - 5);
        rule.AnchorX = Math.Round(Math.Clamp(x, 0, maxX));
        rule.AnchorY = Math.Round(Math.Clamp(y, 46, maxY));
    }
}

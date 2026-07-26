using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace D4Hub.App.Views;

/// <summary>
/// 为 ProgressBar 提供平滑的数值过渡动画。
/// 绑定 SmoothValue 代替 Value，数值变化时以缓动动画过渡，
/// 适合轮询驱动的状态（如识别置信度）避免跳动。
/// </summary>
public static class ProgressBarSmoother
{
    public static readonly DependencyProperty SmoothValueProperty =
        DependencyProperty.RegisterAttached(
            "SmoothValue",
            typeof(double),
            typeof(ProgressBarSmoother),
            new FrameworkPropertyMetadata(0d, OnSmoothValueChanged));

    public static void SetSmoothValue(DependencyObject element, double value) =>
        element.SetValue(SmoothValueProperty, value);

    public static double GetSmoothValue(DependencyObject element) =>
        (double)element.GetValue(SmoothValueProperty);

    private static void OnSmoothValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.ProgressBar bar || e.NewValue is not double target)
        {
            return;
        }

        bar.BeginAnimation(
            RangeBase.ValueProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(340))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }
}

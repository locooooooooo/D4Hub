using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using D4Hub.App.ViewModels;
using D4Hub.Core;

namespace D4Hub.App;

public partial class PreviewWindow : Window
{
    public PreviewWindow(HudViewModel viewModel, ScreenshotPreview preview)
    {
        InitializeComponent();
        DataContext = viewModel;

        PreviewCanvas.Width = preview.ImageWidth;
        PreviewCanvas.Height = preview.ImageHeight;
        ScreenshotImage.Width = preview.ImageWidth;
        ScreenshotImage.Height = preview.ImageHeight;
        ScreenshotImage.Source = new BitmapImage(new Uri(preview.Path, UriKind.Absolute));

        var left = preview.PanelBounds.X * preview.ImageWidth;
        var top = preview.PanelBounds.Y * preview.ImageHeight;
        var width = preview.PanelBounds.Width * preview.ImageWidth;
        var height = preview.PanelBounds.Height * preview.ImageHeight;
        var scale = Math.Min(
            width / HudLayoutMetrics.DesignWidth,
            height / HudLayoutMetrics.DesignHeight);
        HudPreview.Width = HudLayoutMetrics.DesignWidth * scale;
        HudPreview.Height = HudLayoutMetrics.DesignHeight * scale;
        Canvas.SetLeft(HudPreview, left);
        Canvas.SetTop(HudPreview, top);
    }
}

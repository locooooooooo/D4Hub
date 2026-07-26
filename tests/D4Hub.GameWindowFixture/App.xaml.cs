using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace D4Hub.GameWindowFixture;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length != 1 || !File.Exists(e.Args[0]))
        {
            Shutdown(2);
            return;
        }

        var window = new Window
        {
            Title = "暗黑破坏神IV",
            Width = 1600,
            Height = 960,
            MinWidth = 1000,
            MinHeight = 600,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            Background = Brushes.Black,
            Content = new Image
            {
                Source = new BitmapImage(new Uri(Path.GetFullPath(e.Args[0]), UriKind.Absolute)),
                Stretch = Stretch.Fill
            }
        };
        MainWindow = window;
        window.Show();
    }
}

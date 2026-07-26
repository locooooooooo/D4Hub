using System.IO;
using System.Net.Http;
using System.Windows;
using D4Hub.App.Services;
using D4Hub.App.ViewModels;
using D4Hub.Core;

namespace D4Hub.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D4Hub",
            "build-state.json");
        var localDataRoot = Path.GetDirectoryName(statePath)!;
        var stateStore = new JsonStateStore(statePath);
        var d2CoreClient = new D2CoreCloudBuildClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            Path.Combine(localDataRoot, "cache", "d2core-affix-72698-zhCN.json"));
        var d2CoreResolver = new D2CoreBuildResolver(
            new FileBuildLibraryStore(Path.Combine(AppContext.BaseDirectory, "library"), isReadOnly: true),
            new FileBuildLibraryStore(Path.Combine(localDataRoot, "build-library")),
            d2CoreClient);
        var viewModel = new HudViewModel(
            stateStore,
            stateStore.Load(),
            new GameWindowLocator(),
            new ScreenFrameService(),
            new CharacterPanelDetector(),
            new BuildFingerprintService(),
            new TransmutationSceneDetector(),
            d2CoreResolver);
        var mainWindow = new MainWindow(viewModel);
        MainWindow = mainWindow;
        mainWindow.Show();

        if (e.Args.Length == 2
            && string.Equals(e.Args[0], "--preview", StringComparison.OrdinalIgnoreCase)
            && File.Exists(e.Args[1]))
        {
            viewModel.LearnFromScreenshot(Path.GetFullPath(e.Args[1]));
        }
    }
}

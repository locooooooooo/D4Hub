using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using D4Hub.App.Services;
using D4Hub.App.ViewModels;
using D4Hub.Core;
using Velopack;

namespace D4Hub.App;

public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        EnsureWindowsDirectoryEnvironment();
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        if (args.Length == 1 && string.Equals(args[0], "--verify-xaml-startup", StringComparison.Ordinal))
        {
            return;
        }

        app.Run();
    }

    private static void EnsureWindowsDirectoryEnvironment()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("windir")))
        {
            return;
        }

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot) && Directory.Exists(systemRoot))
        {
            Environment.SetEnvironmentVariable("windir", systemRoot, EnvironmentVariableTarget.Process);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D4Hub",
            "build-state.json");
        var localDataRoot = Path.GetDirectoryName(statePath)!;
        var isFirstRun = !File.Exists(statePath);
        var stateStore = new JsonStateStore(statePath);
        var publicLibrary = new FileBuildLibraryStore(
            Path.Combine(AppContext.BaseDirectory, "library"),
            isReadOnly: true);
        var d2CoreClient = new D2CoreCloudBuildClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            Path.Combine(localDataRoot, "cache", "d2core-affix-72698-zhCN.json"));
        var d2CoreResolver = new D2CoreBuildResolver(
            publicLibrary,
            new FileBuildLibraryStore(Path.Combine(localDataRoot, "build-library")),
            d2CoreClient);
        var document = stateStore.Load();
        var defaultProfiles = BuildLibrarySeeder.CreateProfiles(publicLibrary);
        if (isFirstRun && defaultProfiles.Count > 0)
        {
            document.Profiles = new ObservableCollection<BuildProfile>(defaultProfiles);
            document.SelectedProfileId = defaultProfiles[0].Id;
            stateStore.Save(document);
        }
        else if (BuildLibrarySeeder.MergeMissingProfiles(document, defaultProfiles) > 0)
        {
            stateStore.Save(document);
        }

        var viewModel = new HudViewModel(
            stateStore,
            document,
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

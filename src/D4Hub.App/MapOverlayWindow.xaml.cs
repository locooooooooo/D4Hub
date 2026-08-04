using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using D4Hub.App.Services;
using D4Hub.App.ViewModels;
using D4Hub.Core;

namespace D4Hub.App;

public partial class MapOverlayWindow : Window
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
    private static readonly (PoiMarkerCategory Category, Color Color, string Glyph)[] MarkerStyles =
    [
        (PoiMarkerCategory.Chest, Color.FromRgb(0xE7, 0x4C, 0x3C), "◆"),
        (PoiMarkerCategory.Elite, Color.FromRgb(0xF3, 0x9C, 0x12), "★"),
        (PoiMarkerCategory.Event, Color.FromRgb(0x34, 0x98, 0xDB), "●"),
        (PoiMarkerCategory.Ritual, Color.FromRgb(0x9B, 0x59, 0xB6), "◈"),
        (PoiMarkerCategory.Dungeon, Color.FromRgb(0x1A, 0xBC, 0x9C), "◉")
    ];
    private const double MarkerSize = 16;

    private readonly HudViewModel _viewModel;
    private readonly DispatcherTimer _timer;
    private readonly List<(double X, double Y, PoiMarkerCategory Category)> _markers = new();
    private readonly Dictionary<string, MediaPlayer> _audioPlayers = new();
    private readonly WorldEventEdgeTracker _eventEdgeTracker;
    private IntPtr _handle;
    private bool _isLayoutEditing;
    private PoiCatalog? _poiCatalog;

    public MapOverlayWindow(HudViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _eventEdgeTracker = new WorldEventEdgeTracker(viewModel.WorldEventClock, DateTimeOffset.Now);
        RefreshMap();
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateTimers();
        _timer.Start();
    }

    public bool AllowClose { get; set; }

    public void UpdatePlacement(MapHudPlacement placement)
    {
        if (!IsVisible)
        {
            Show();
        }

        EnsureHandle();
        SetWindowPos(
            _handle,
            HwndTopmost,
            placement.Left,
            placement.Top,
            Math.Max(1, placement.Width),
            Math.Max(1, placement.Height),
            SwpNoActivate | SwpShowWindow);
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

    /// <summary>
    /// 按当前设置重新加载底图与 POI 标记并应用显示样式；由主窗口在设置变更时调用。
    /// 加载失败一律 fail-closed：隐藏对应图层并保留窗口，不崩溃。
    /// </summary>
    public void RefreshMap()
    {
        LoadMapImage();
        LoadPoiMarkers();
        RepositionMarkers();
        ApplyDisplaySettings();
    }

    private void ApplyDisplaySettings()
    {
        var settings = _viewModel.MapHudSettings;

        // 地图内容透明度（底图 + POI）
        MapHost.Opacity = settings.Opacity;

        // 计时条背景不透明度（0 = 全透明，仅文字）
        var backgroundAlpha = (byte)Math.Clamp((int)(settings.TimerBarBackgroundWidth * 255), 0, 255);
        var background = new SolidColorBrush(Color.FromArgb(backgroundAlpha, 0x00, 0x00, 0x00));
        HelltideBar.Background = background;
        WorldBossBar.Background = background;
        LegionBar.Background = background;

        // 计时条排列：横排（三条并排）或竖排（上下堆叠）
        TimerBar.Orientation = settings.TimerBarHorizontal ? Orientation.Horizontal : Orientation.Vertical;
        if (settings.TimerBarHorizontal)
        {
            HelltideBar.Margin = new Thickness(0, 0, 3, 0);
            WorldBossBar.Margin = new Thickness(0, 0, 3, 0);
            LegionBar.Margin = new Thickness(0);
        }
        else
        {
            HelltideBar.Margin = new Thickness(0, 0, 0, 3);
            WorldBossBar.Margin = new Thickness(0, 0, 0, 3);
            LegionBar.Margin = new Thickness(0);
        }
    }

    private void LoadMapImage()
    {
        MapImage.Source = null;
        var settings = _viewModel.MapHudSettings;
        if (string.IsNullOrWhiteSpace(settings.MapImagePath))
        {
            return;
        }

        var path = Path.Combine(settings.MapImagePath, $"{settings.CurrentRegion}.png");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            MapImage.Source = bitmap;
        }
        catch (Exception)
        {
            MapImage.Source = null;
        }
    }

    private void LoadPoiMarkers()
    {
        _markers.Clear();
        var settings = _viewModel.MapHudSettings;
        _poiCatalog = PoiCatalogStore.Load(settings.PoiDataPath);
        var markers = PoiCatalogStore.GetMarkers(_poiCatalog, settings.CurrentRegion);
        foreach (var marker in markers)
        {
            if (!IsCategoryVisible(marker.Category, settings)
                || marker.X is not double x
                || marker.Y is not double y)
            {
                continue;
            }

            _markers.Add((x, y, marker.Category));
        }
    }

    private static bool IsCategoryVisible(PoiMarkerCategory category, MapHudSettings settings) => category switch
    {
        PoiMarkerCategory.Chest => settings.ShowChests,
        PoiMarkerCategory.Elite => settings.ShowEliteChests,
        PoiMarkerCategory.Event => settings.ShowEvents,
        PoiMarkerCategory.Ritual => settings.ShowRituals,
        PoiMarkerCategory.Dungeon => settings.ShowDungeons,
        _ => false
    };

    private void RepositionMarkers()
    {
        PoiLayer.Children.Clear();
        var width = Math.Max(1, MapHost.ActualWidth);
        var height = Math.Max(1, MapHost.ActualHeight);
        foreach (var marker in _markers)
        {
            var style = MarkerStyles.First(style => style.Category == marker.Category);
            var element = new Border
            {
                Width = MarkerSize,
                Height = MarkerSize,
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)),
                BorderBrush = new SolidColorBrush(style.Color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = style.Glyph,
                    Foreground = new SolidColorBrush(style.Color),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(element, marker.X * width - MarkerSize / 2);
            Canvas.SetTop(element, marker.Y * height - MarkerSize / 2);
            PoiLayer.Children.Add(element);
        }
    }

    private void UpdateTimers()
    {
        _viewModel.WorldEventClock.ManualOffsetSeconds = _viewModel.MapHudSettings.ScheduleOffsetSeconds;
        var now = DateTimeOffset.Now;
        UpdateTimer(HelltideRemaining, HelltideStatus, _viewModel.WorldEventClock.Evaluate(WorldEventKind.Helltide, now));
        UpdateTimer(WorldBossRemaining, WorldBossStatus, _viewModel.WorldEventClock.Evaluate(WorldEventKind.WorldBoss, now));
        UpdateTimer(LegionRemaining, LegionStatus, _viewModel.WorldEventClock.Evaluate(WorldEventKind.Legion, now));

        // 音频提醒：事件由等待转入进行中时播放一次对应音效；fail-closed（无文件/失败静默）
        if (_viewModel.MapHudSettings.AudioEnabled)
        {
            foreach (var kind in _eventEdgeTracker.Rising(_viewModel.WorldEventClock, now))
            {
                PlayEventAudio(kind);
            }
        }
    }

    private void PlayEventAudio(WorldEventKind kind)
    {
        var settings = _viewModel.MapHudSettings;
        var path = kind switch
        {
            WorldEventKind.WorldBoss => settings.AudioBossPath,
            WorldEventKind.Helltide => settings.AudioElitePath,
            WorldEventKind.Legion => settings.AudioBluePath,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        if (!_audioPlayers.TryGetValue(path, out var player))
        {
            try
            {
                player = new MediaPlayer();
                player.Open(new Uri(path, UriKind.Absolute));
                _audioPlayers[path] = player;
            }
            catch (Exception)
            {
                return;
            }
        }

        player.Volume = settings.AudioVolume;
        player.Stop();
        player.Position = TimeSpan.Zero;
        player.Play();
    }

    private static void UpdateTimer(TextBlock remaining, TextBlock status, (TimeSpan Remaining, bool Active) result)
    {
        var remainingText = result.Remaining.TotalHours >= 1
            ? $"{(int)result.Remaining.TotalHours}:{result.Remaining.Minutes:00}:{result.Remaining.Seconds:00}"
            : $"{result.Remaining.Minutes:00}:{result.Remaining.Seconds:00}";
        remaining.Text = remainingText;
        status.Text = result.Active ? "● 进行中" : "○ 等待中";
        status.Foreground = new SolidColorBrush(result.Active
            ? Color.FromRgb(0x27, 0xAE, 0x60)
            : Color.FromRgb(0x8A, 0x85, 0x78));
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

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RepositionMarkers();
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

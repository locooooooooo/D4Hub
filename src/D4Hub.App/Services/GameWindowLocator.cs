using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace D4Hub.App.Services;

public readonly record struct GameClientWindow(
    IntPtr Handle,
    string Title,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsForeground,
    bool IsMinimized);

public sealed class GameWindowLocator
{
    private static readonly string[] SupportedTitles =
    {
        "Diablo IV",
        "Diablo 4",
        "暗黑破坏神IV",
        "暗黑破坏神 IV"
    };

    private static readonly string[] SupportedProcessNames =
    {
        "Diablo IV",
        "DiabloIV",
        "Fenris-Win64-Shipping"
    };

    public GameClientWindow? FindDiabloWindow()
    {
        return FindWindow(SupportedTitles, 800, 500, exactTitle: false, requireGameProcess: true);
    }

    public GameClientWindow? FindWindowByTitle(string title)
    {
        return FindWindow(new[] { title }, 120, 80, exactTitle: true, requireGameProcess: false);
    }

    public IReadOnlyList<GameClientWindow> ListVisibleWindows()
    {
        var windows = new List<GameClientWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var title = ReadWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title)
                || !TryGetClientBounds(handle, out var left, out var top, out var width, out var height))
            {
                return true;
            }

            windows.Add(new GameClientWindow(
                handle,
                title,
                left,
                top,
                width,
                height,
                GetForegroundWindow() == handle,
                IsIconic(handle)));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static GameClientWindow? FindWindow(
        IReadOnlyCollection<string> supportedTitles,
        int minimumWidth,
        int minimumHeight,
        bool exactTitle,
        bool requireGameProcess)
    {
        var matches = new List<GameClientWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var title = ReadWindowTitle(handle);
            var supported = exactTitle
                ? supportedTitles.Any(candidate => string.Equals(title, candidate, StringComparison.OrdinalIgnoreCase))
                : supportedTitles.Any(candidate => title.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (!supported)
            {
                return true;
            }

            if (requireGameProcess && !IsSupportedGameProcess(handle))
            {
                return true;
            }

            if (!TryGetClientBounds(handle, out var left, out var top, out var width, out var height)
                || width < minimumWidth
                || height < minimumHeight)
            {
                return true;
            }

            matches.Add(new GameClientWindow(
                handle,
                title,
                left,
                top,
                width,
                height,
                GetForegroundWindow() == handle,
                IsIconic(handle)));
            return true;
        }, IntPtr.Zero);

        return matches
            .Where(match => !match.IsMinimized)
            .OrderByDescending(match => (long)match.Width * match.Height)
            .Cast<GameClientWindow?>()
            .FirstOrDefault();
    }

    private static string ReadWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool TryGetClientBounds(
        IntPtr handle,
        out int left,
        out int top,
        out int width,
        out int height)
    {
        left = top = width = height = 0;
        if (!GetClientRect(handle, out var clientRect))
        {
            return false;
        }

        var origin = new NativePoint();
        if (!ClientToScreen(handle, ref origin))
        {
            return false;
        }

        left = origin.X;
        top = origin.Y;
        width = clientRect.Right - clientRect.Left;
        height = clientRect.Bottom - clientRect.Top;
        return width > 0 && height > 0;
    }

    private static bool IsSupportedGameProcess(IntPtr handle)
    {
        GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return SupportedProcessNames.Any(name =>
                string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumLength);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
}

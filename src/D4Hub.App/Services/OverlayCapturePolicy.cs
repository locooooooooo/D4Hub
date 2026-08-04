using System.Runtime.InteropServices;

namespace D4Hub.App.Services;

public readonly record struct OverlayCaptureAffinityReceipt(
    bool SetSucceeded,
    bool QuerySucceeded,
    uint Affinity,
    int ErrorCode)
{
    public const uint ExcludeFromCaptureAffinity = 0x00000011;

    public bool IsExcluded =>
        SetSucceeded && QuerySucceeded && Affinity == ExcludeFromCaptureAffinity;
}

internal static class OverlayCapturePolicy
{
    public static OverlayCaptureAffinityReceipt ExcludeFromCapture(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return new OverlayCaptureAffinityReceipt(false, false, 0, 6);
        }

        var setSucceeded = SetWindowDisplayAffinity(
            handle,
            OverlayCaptureAffinityReceipt.ExcludeFromCaptureAffinity);
        var errorCode = setSucceeded ? 0 : Marshal.GetLastWin32Error();
        var querySucceeded = GetWindowDisplayAffinity(handle, out var affinity);
        if (!querySucceeded && errorCode == 0)
        {
            errorCode = Marshal.GetLastWin32Error();
        }

        return new OverlayCaptureAffinityReceipt(
            setSucceeded,
            querySucceeded,
            querySucceeded ? affinity : 0,
            errorCode);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
}

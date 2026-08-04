namespace D4Hub.App.Services;

/// <summary>
/// Idempotent lifecycle gate for the live screen polling timer. Window Loaded
/// and Closing may be raised more than once by a host; only state transitions
/// are allowed to start or stop the underlying capture loop.
/// </summary>
public sealed class RealtimeCaptureLifecycle
{
    private readonly Action _start;
    private readonly Action _stop;

    public RealtimeCaptureLifecycle(Action start, Action stop)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _stop = stop ?? throw new ArgumentNullException(nameof(stop));
    }

    public bool IsRunning { get; private set; }

    public bool Start()
    {
        if (IsRunning)
        {
            return false;
        }

        _start();
        IsRunning = true;
        return true;
    }

    public bool Stop()
    {
        if (!IsRunning)
        {
            return false;
        }

        _stop();
        IsRunning = false;
        return true;
    }
}

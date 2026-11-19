namespace HomeFocus.Core.Tracking;

/// <summary>Runs the <see cref="ActivityTracker"/> once a second on a background timer and handles pausing.</summary>
public sealed class TrackingService : IDisposable
{
    private readonly ActivityTracker _tracker;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private DateTime? _pausedUntil;
    private bool _disposed;

    public TrackingService(ActivityTracker tracker, Func<DateTime>? utcNow = null)
    {
        _tracker = tracker;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised after every tick, on the timer thread.</summary>
    public event EventHandler? Ticked;

    public TimeSpan IdleThreshold
    {
        get { lock (_gate) return _tracker.IdleThreshold; }
        set { lock (_gate) _tracker.IdleThreshold = value; }
    }

    public DateTime? PausedUntilUtc
    {
        get { lock (_gate) return _pausedUntil; }
    }

    public bool IsPaused => PausedUntilUtc is { } u && u > _utcNow();

    /// <summary>Snapshot of the open segment (may be null).</summary>
    public ActivityRecord? Current
    {
        get { lock (_gate) return _tracker.Current; }
    }

    public void Start() => _timer.Change(0, 1000);

    public void Pause(TimeSpan duration)
    {
        lock (_gate) _pausedUntil = _utcNow() + duration;
        Tick();
    }

    public void Resume()
    {
        lock (_gate) _pausedUntil = null;
        Tick();
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var now = _utcNow();
            if (_pausedUntil is { } u && u <= now) _pausedUntil = null;
            try
            {
                _tracker.Tick(now, _pausedUntil is not null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tracker tick failed: {ex}");
            }
        }
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            try { _tracker.Stop(_utcNow()); } catch { /* shutting down */ }
        }
    }
}

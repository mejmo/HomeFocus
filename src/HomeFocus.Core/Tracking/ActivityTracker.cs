using HomeFocus.Core.Data;

namespace HomeFocus.Core.Tracking;

public interface IForegroundSource
{
    /// <summary>The app in the foreground, or null when there is none (desktop, lock screen…).</summary>
    ForegroundApp? GetForeground();

    /// <summary>Time since the last keyboard or mouse input.</summary>
    TimeSpan GetIdleTime();
}

/// <summary>
/// Turns periodic samples of the foreground app into activity segments.
/// Call <see cref="Tick"/> about once a second; it is not thread-safe.
/// </summary>
public sealed class ActivityTracker(IForegroundSource source, IActivitySink sink, string deviceId)
{
    /// <summary>Segments shorter than this are dropped (alt-tab flicker).</summary>
    public static readonly TimeSpan MinSegment = TimeSpan.FromSeconds(2);

    /// <summary>A gap between ticks longer than this means the PC slept.</summary>
    public static readonly TimeSpan MaxTickGap = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(5);

    private ActivityRecord? _current;
    private DateTime _lastTick;
    private DateTime _lastFlush;

    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(3);

    public ActivityRecord? Current => _current;

    public void Tick(DateTime nowUtc, bool paused = false)
    {
        if (_current is not null && nowUtc - _lastTick > MaxTickGap)
            Close(_lastTick); // slept or hung: the segment ends where we last saw it
        _lastTick = nowUtc;

        if (paused)
        {
            Close(nowUtc);
            return;
        }

        var idle = source.GetIdleTime();
        if (idle >= IdleThreshold)
        {
            if (_current is { Kind: ActivityKind.Idle })
            {
                Extend(nowUtc);
                return;
            }
            // Idle began at the last input, but never before the segment we are cutting short.
            // With nothing open (startup, after a pause) idle time is counted from now.
            var idleStart = nowUtc;
            if (_current is not null)
            {
                idleStart = nowUtc - idle;
                if (idleStart < _current.StartUtc) idleStart = _current.StartUtc;
                Close(idleStart);
            }
            Start(ActivityKind.Idle, "idle", "Idle", idleStart, nowUtc);
            return;
        }

        var fg = source.GetForeground();
        if (fg is null)
        {
            Close(nowUtc);
            return;
        }

        if (_current is { Kind: ActivityKind.App } cur && string.Equals(cur.AppKey, fg.AppKey, StringComparison.OrdinalIgnoreCase))
        {
            Extend(nowUtc);
            return;
        }

        Close(nowUtc);
        Start(ActivityKind.App, fg.AppKey, fg.DisplayName, nowUtc, nowUtc);
    }

    /// <summary>Ends the open segment (e.g. on shutdown).</summary>
    public void Stop(DateTime nowUtc) => Close(nowUtc);

    private void Start(ActivityKind kind, string key, string name, DateTime start, DateTime now)
    {
        _current = new ActivityRecord(Guid.NewGuid().ToString("N"), deviceId, kind, key, name, null, start, now);
        _lastFlush = DateTime.MinValue;
        FlushIfDue(now);
    }

    private void Extend(DateTime now)
    {
        _current = _current! with { EndUtc = now };
        FlushIfDue(now);
    }

    private void FlushIfDue(DateTime now)
    {
        if (_current is null || _current.Duration < MinSegment || now - _lastFlush < FlushEvery) return;
        sink.Upsert(_current);
        _lastFlush = now;
    }

    private void Close(DateTime end)
    {
        if (_current is null) return;
        var rec = _current with { EndUtc = end < _current.StartUtc ? _current.StartUtc : end };
        _current = null;
        if (rec.Duration >= MinSegment) sink.Upsert(rec);
        else if (_lastFlush != DateTime.MinValue) sink.Delete(rec.Id);
    }
}

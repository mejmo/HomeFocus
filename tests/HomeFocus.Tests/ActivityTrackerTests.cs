using HomeFocus.Core;
using HomeFocus.Core.Data;
using HomeFocus.Core.Tracking;

namespace HomeFocus.Tests;

public class ActivityTrackerTests
{
    private sealed class FakeSource : IForegroundSource
    {
        public ForegroundApp? App = new("devenv.exe", "Visual Studio");
        public TimeSpan Idle = TimeSpan.Zero;
        public ForegroundApp? GetForeground() => App;
        public TimeSpan GetIdleTime() => Idle;
    }

    private sealed class MemorySink : IActivitySink
    {
        public readonly Dictionary<string, ActivityRecord> Rows = new();
        public void Upsert(ActivityRecord r) => Rows[r.Id] = r;
        public void Delete(string id) => Rows.Remove(id);
        public List<ActivityRecord> Ordered => Rows.Values.OrderBy(r => r.StartUtc).ToList();
    }

    private static readonly DateTime T0 = new(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);

    private static (ActivityTracker, FakeSource, MemorySink) Create()
    {
        var src = new FakeSource();
        var sink = new MemorySink();
        return (new ActivityTracker(src, sink, "pc1"), src, sink);
    }

    private static void Run(ActivityTracker t, int fromSec, int toSec, FakeSource? src = null, Action<int>? each = null)
    {
        for (var s = fromSec; s <= toSec; s++)
        {
            each?.Invoke(s);
            t.Tick(T0.AddSeconds(s));
        }
    }

    [Fact]
    public void Switching_apps_creates_one_segment_per_app()
    {
        var (t, src, sink) = Create();
        Run(t, 0, 60);
        src.App = new ForegroundApp("chrome.exe", "Chrome");
        Run(t, 61, 120);
        t.Stop(T0.AddSeconds(120));

        var rows = sink.Ordered;
        Assert.Equal(2, rows.Count);
        Assert.Equal("devenv.exe", rows[0].AppKey);
        Assert.Equal(TimeSpan.FromSeconds(61), rows[0].Duration);
        Assert.Equal("chrome.exe", rows[1].AppKey);
        Assert.Equal(T0.AddSeconds(120), rows[1].EndUtc);
    }

    [Fact]
    public void Idle_backdates_end_of_app_segment_to_last_input()
    {
        var (t, src, sink) = Create();
        t.IdleThreshold = TimeSpan.FromMinutes(3);
        Run(t, 0, 600, each: s => src.Idle = TimeSpan.FromSeconds(Math.Max(0, s - 100)));
        t.Stop(T0.AddSeconds(600));

        var rows = sink.Ordered;
        Assert.Equal(2, rows.Count);
        Assert.Equal(ActivityKind.App, rows[0].Kind);
        Assert.Equal(T0.AddSeconds(100), rows[0].EndUtc); // last input at 100 s
        Assert.Equal(ActivityKind.Idle, rows[1].Kind);
        Assert.Equal(T0.AddSeconds(100), rows[1].StartUtc);
        Assert.Equal(T0.AddSeconds(600), rows[1].EndUtc);
    }

    [Fact]
    public void Returning_from_idle_starts_new_app_segment()
    {
        var (t, src, sink) = Create();
        t.IdleThreshold = TimeSpan.FromSeconds(30);
        src.Idle = TimeSpan.FromSeconds(40);
        Run(t, 0, 50);
        src.Idle = TimeSpan.Zero;
        Run(t, 51, 80);
        t.Stop(T0.AddSeconds(80));

        var rows = sink.Ordered;
        Assert.Equal([ActivityKind.Idle, ActivityKind.App], rows.Select(r => r.Kind));
        Assert.Equal(T0, rows[0].StartUtc); // nothing open at start: idle counted from first tick
        Assert.Equal(T0.AddSeconds(51), rows[1].StartUtc);
    }

    [Fact]
    public void Sleep_gap_closes_segment_at_last_tick()
    {
        var (t, _, sink) = Create();
        Run(t, 0, 10);
        t.Tick(T0.AddHours(2)); // resumed from sleep
        t.Stop(T0.AddHours(2).AddSeconds(10));

        var rows = sink.Ordered;
        Assert.Equal(2, rows.Count);
        Assert.Equal(T0.AddSeconds(10), rows[0].EndUtc);
        Assert.Equal(T0.AddHours(2), rows[1].StartUtc);
    }

    [Fact]
    public void Pause_closes_segment_and_records_nothing()
    {
        var (t, _, sink) = Create();
        Run(t, 0, 10);
        for (var s = 11; s <= 100; s++) t.Tick(T0.AddSeconds(s), paused: true);
        Assert.Single(sink.Rows);
        Assert.Equal(T0.AddSeconds(11), sink.Ordered[0].EndUtc);
        Assert.Null(t.Current);
    }

    [Fact]
    public void Flicker_shorter_than_minimum_is_dropped()
    {
        var (t, src, sink) = Create();
        Run(t, 0, 10);
        src.App = new ForegroundApp("explorer.exe", "Prieskumník");
        t.Tick(T0.AddSeconds(11));
        src.App = new ForegroundApp("devenv.exe", "Visual Studio");
        Run(t, 12, 20);
        t.Stop(T0.AddSeconds(20));
        Assert.DoesNotContain(sink.Rows.Values, r => r.AppKey == "explorer.exe");
    }
}

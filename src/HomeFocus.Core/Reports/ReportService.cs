using HomeFocus.Core.Data;

namespace HomeFocus.Core.Reports;

public sealed record AppTotal(string AppKey, string DisplayName, long CategoryId, TimeSpan Duration);

public sealed record CategoryTotal(long CategoryId, TimeSpan Duration);

/// <summary>A piece of the day's timeline in local time; CategoryId is null for idle time.</summary>
public sealed record TimelineSegment(DateTime StartLocal, DateTime EndLocal, long? CategoryId, string Label);

public sealed record DaySummary(
    DateOnly Day,
    TimeSpan Active,
    TimeSpan Idle,
    IReadOnlyList<CategoryTotal> Categories,
    IReadOnlyList<AppTotal> Apps,
    IReadOnlyList<TimelineSegment> Timeline);

public sealed record DayBar(DateOnly Day, IReadOnlyList<CategoryTotal> Categories)
{
    public TimeSpan Total => TimeSpan.FromTicks(Categories.Sum(c => c.Duration.Ticks));
}

public sealed record RangeSummary(
    DateOnly From,
    DateOnly ToExclusive,
    TimeSpan Active,
    IReadOnlyList<DayBar> Days,
    IReadOnlyList<CategoryTotal> Categories,
    IReadOnlyList<AppTotal> Apps);

/// <summary>Aggregates activity records into per-day and per-range summaries. Times are clipped to the range.</summary>
public sealed class ReportService(ActivityStore store, Func<Categorizer> categorizer, Func<ActivityRecord?>? live = null)
{
    public DaySummary GetDay(DateOnly day)
    {
        var (from, to) = Bounds(day, day.AddDays(1));
        var cat = categorizer();
        var records = Load(from, to);

        var timeline = new List<TimelineSegment>();
        foreach (var r in records)
        {
            var s = Max(r.StartUtc, from);
            var e = Min(r.EndUtc, to);
            if (e <= s) continue;
            long? c = r.Kind == ActivityKind.Idle ? null : r.CategoryOverride ?? cat.Resolve(r.AppKey);
            timeline.Add(new TimelineSegment(s.ToLocalTime(), e.ToLocalTime(), c, r.DisplayName));
        }

        var apps = AppTotals(records, from, to, cat, null);
        return new DaySummary(day,
            TimeSpan.FromTicks(apps.Sum(a => a.Duration.Ticks)),
            TimeSpan.FromTicks(records.Where(r => r.Kind == ActivityKind.Idle).Sum(r => Clip(r, from, to).Ticks)),
            CategoryTotals(apps), apps, timeline);
    }

    public RangeSummary GetRange(DateOnly fromDay, DateOnly toDayExclusive, long? categoryFilter = null)
    {
        var (from, to) = Bounds(fromDay, toDayExclusive);
        var cat = categorizer();
        var records = Load(from, to);

        var days = new List<DayBar>();
        for (var d = fromDay; d < toDayExclusive; d = d.AddDays(1))
        {
            var (df, dt) = Bounds(d, d.AddDays(1));
            days.Add(new DayBar(d, CategoryTotals(AppTotals(records, df, dt, cat, categoryFilter))));
        }

        var apps = AppTotals(records, from, to, cat, categoryFilter);
        return new RangeSummary(fromDay, toDayExclusive, TimeSpan.FromTicks(apps.Sum(a => a.Duration.Ticks)),
            days, CategoryTotals(apps), apps);
    }

    /// <summary>App keys used in the range that no rule matches.</summary>
    public List<AppTotal> GetUncategorized(DateOnly fromDay, DateOnly toDayExclusive)
    {
        var (from, to) = Bounds(fromDay, toDayExclusive);
        var cat = categorizer();
        return AppTotals(Load(from, to), from, to, cat, null)
            .Where(a => cat.TryMatch(a.AppKey) is null)
            .ToList();
    }

    private List<ActivityRecord> Load(DateTime from, DateTime to)
    {
        var list = store.Query(from, to);
        if (live?.Invoke() is { } cur && cur.StartUtc < to && cur.EndUtc > from)
        {
            var i = list.FindIndex(r => r.Id == cur.Id);
            if (i >= 0) list[i] = cur; else list.Add(cur);
        }
        return list;
    }

    private static List<AppTotal> AppTotals(List<ActivityRecord> records, DateTime from, DateTime to, Categorizer cat, long? filter)
    {
        var map = new Dictionary<string, (string Name, long Cat, long Ticks)>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            if (r.Kind != ActivityKind.App) continue;
            var t = Clip(r, from, to).Ticks;
            if (t <= 0) continue;
            var c = r.CategoryOverride ?? cat.Resolve(r.AppKey);
            if (filter is { } f && c != f) continue;
            map[r.AppKey] = map.TryGetValue(r.AppKey, out var cur) ? (r.DisplayName, cur.Cat, cur.Ticks + t) : (r.DisplayName, c, t);
        }
        return map.Select(kv => new AppTotal(kv.Key, kv.Value.Name, kv.Value.Cat, TimeSpan.FromTicks(kv.Value.Ticks)))
            .OrderByDescending(a => a.Duration).ToList();
    }

    private static List<CategoryTotal> CategoryTotals(IEnumerable<AppTotal> apps) =>
        apps.GroupBy(a => a.CategoryId)
            .Select(g => new CategoryTotal(g.Key, TimeSpan.FromTicks(g.Sum(a => a.Duration.Ticks))))
            .OrderByDescending(c => c.Duration).ToList();

    private static TimeSpan Clip(ActivityRecord r, DateTime from, DateTime to)
    {
        var s = Max(r.StartUtc, from);
        var e = Min(r.EndUtc, to);
        return e > s ? e - s : TimeSpan.Zero;
    }

    /// <summary>Local-midnight bounds of [from, to) in UTC (DST-safe).</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Bounds(DateOnly from, DateOnly toExclusive) => (
        from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime(),
        toExclusive.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime());

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}

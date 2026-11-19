using HomeFocus.Core;
using HomeFocus.Core.Data;
using HomeFocus.Core.Reports;

namespace HomeFocus.Tests;

public sealed class ReportServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"homefocus-test-{Guid.NewGuid():N}.db");
    private readonly Database _db;
    private readonly ActivityStore _store;
    private readonly string _device;
    private readonly long _work, _fun, _other;

    public ReportServiceTests()
    {
        _db = new Database(_path);
        _db.Initialize();
        _store = new ActivityStore(_db);
        _device = new SettingsStore(_db).DeviceId();
        var cats = new CategoryStore(_db).GetAll();
        _work = cats.Single(c => c.Name == "Práca").Id;
        _fun = cats.Single(c => c.Name == "Zábava").Id;
        _other = cats.Single(c => c.Name == "Ostatné").Id;
    }

    private ReportService Service() => new(_store, () => new Categorizer(new RuleStore(_db).GetAll(), _other));

    private static readonly DateOnly Day = new(2026, 11, 19);
    private static DateTime Local(DateOnly d, int h, int m = 0) =>
        d.ToDateTime(new TimeOnly(h, m), DateTimeKind.Local).ToUniversalTime();

    private void Add(string app, DateTime s, DateTime e, ActivityKind kind = ActivityKind.App) =>
        _store.Upsert(new ActivityRecord(Guid.NewGuid().ToString("N"), _device, kind, app, app, null, s, e));

    [Fact]
    public void Day_totals_by_app_and_category()
    {
        Add("devenv.exe", Local(Day, 9), Local(Day, 11));
        Add("steam.exe", Local(Day, 11), Local(Day, 11, 30));
        Add("devenv.exe", Local(Day, 12), Local(Day, 13));
        Add("idle", Local(Day, 13), Local(Day, 13, 20), ActivityKind.Idle);
        Add("mystery.exe", Local(Day, 14), Local(Day, 14, 10));

        var d = Service().GetDay(Day);

        Assert.Equal(TimeSpan.FromMinutes(220), d.Active);
        Assert.Equal(TimeSpan.FromMinutes(20), d.Idle);
        Assert.Equal(TimeSpan.FromHours(3), d.Apps.Single(a => a.AppKey == "devenv.exe").Duration);
        Assert.Equal(TimeSpan.FromHours(3), d.Categories.Single(c => c.CategoryId == _work).Duration);
        Assert.Equal(TimeSpan.FromMinutes(30), d.Categories.Single(c => c.CategoryId == _fun).Duration);
        Assert.Equal(_other, d.Apps.Single(a => a.AppKey == "mystery.exe").CategoryId);
        Assert.Contains(d.Timeline, s => s.CategoryId is null);
    }

    [Fact]
    public void Segment_across_midnight_is_split_between_days()
    {
        Add("steam.exe", Local(Day, 23), Local(Day.AddDays(1), 1));
        var s = Service();
        Assert.Equal(TimeSpan.FromHours(1), s.GetDay(Day).Active);
        Assert.Equal(TimeSpan.FromHours(1), s.GetDay(Day.AddDays(1)).Active);

        var week = s.GetRange(Day, Day.AddDays(7));
        Assert.Equal(TimeSpan.FromHours(2), week.Active);
        Assert.Equal(7, week.Days.Count);
    }

    [Fact]
    public void User_rule_recategorizes_history_and_clears_uncategorized()
    {
        Add("mystery.exe", Local(Day, 10), Local(Day, 11));
        var s = Service();
        Assert.Single(s.GetUncategorized(Day, Day.AddDays(1)));

        new RuleStore(_db).Set("mystery.exe", _fun);
        Assert.Empty(s.GetUncategorized(Day, Day.AddDays(1)));
        Assert.Equal(_fun, s.GetDay(Day).Apps.Single().CategoryId);
    }

    [Fact]
    public void Category_filter_limits_range()
    {
        Add("devenv.exe", Local(Day, 9), Local(Day, 10));
        Add("steam.exe", Local(Day, 10), Local(Day, 11));
        var r = Service().GetRange(Day, Day.AddDays(1), _fun);
        Assert.Equal(TimeSpan.FromHours(1), r.Active);
        Assert.All(r.Apps, a => Assert.Equal(_fun, a.CategoryId));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}

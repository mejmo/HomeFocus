using HomeFocus.Core;
using HomeFocus.Core.Browser;
using HomeFocus.Core.Data;
using HomeFocus.Core.Reports;

namespace HomeFocus.Tests;

public class SiteKeyTests
{
    [Theory]
    [InlineData("hovno.dennikn.sk", "dennikn.sk")]
    [InlineData("auto.dennikn.sk", "dennikn.sk")]
    [InlineData("dennikn.sk", "dennikn.sk")]
    [InlineData("a.b.c.example.com", "example.com")]
    [InlineData("news.bbc.co.uk", "bbc.co.uk")]
    [InlineData("mejmo.github.io", "mejmo.github.io")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("localhost", "localhost")]
    public void Subdomains_belong_to_their_site(string host, string site) => Assert.Equal(site, SiteKey.Registrable(host));

    [Fact]
    public void A_rule_for_a_subdomain_keeps_it_separate()
    {
        string[] rules = ["mail.google.com", "google.com", "youtube.com"];
        Assert.Equal("mail.google.com", SiteKey.For("mail.google.com", rules));
        Assert.Equal("mail.google.com", SiteKey.For("inbox.mail.google.com", rules));
        Assert.Equal("google.com", SiteKey.For("maps.google.com", rules));
        Assert.Equal("youtube.com", SiteKey.For("m.youtube.com", rules));
        Assert.Equal("dennikn.sk", SiteKey.For("hovno.dennikn.sk", rules));
    }
}

public sealed class SiteReportTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"homefocus-site-{Guid.NewGuid():N}.db");
    private readonly Database _db;

    public SiteReportTests()
    {
        _db = new Database(_path);
        _db.Initialize();
    }

    [Fact]
    public void History_with_subdomains_is_reported_per_site()
    {
        var store = new ActivityStore(_db);
        var dev = new SettingsStore(_db).DeviceId();
        var other = new CategoryStore(_db).GetAll().Single(c => c.Key == "other").Id;
        var t = DateTime.UtcNow.AddHours(-1);
        store.Upsert(new ActivityRecord("1", dev, ActivityKind.Web, "hovno.dennikn.sk", "hovno.dennikn.sk", null, t, t.AddMinutes(5)));
        store.Upsert(new ActivityRecord("2", dev, ActivityKind.Web, "auto.dennikn.sk", "auto.dennikn.sk", null, t.AddMinutes(5), t.AddMinutes(8)));
        store.Upsert(new ActivityRecord("3", dev, ActivityKind.Web, "mail.google.com", "mail.google.com", null, t.AddMinutes(8), t.AddMinutes(9)));

        var rules = new RuleStore(_db);
        var reports = new ReportService(store, () => new Categorizer(rules.GetAll(), other));
        var day = reports.GetDay(DateOnly.FromDateTime(t.ToLocalTime()));

        var dennikn = Assert.Single(day.Apps, a => a.AppKey == "dennikn.sk");
        Assert.Equal(TimeSpan.FromMinutes(8), dennikn.Duration);
        Assert.Contains(day.Apps, a => a.AppKey == "mail.google.com"); // default rule keeps Gmail separate
        Assert.DoesNotContain(day.Apps, a => a.AppKey.EndsWith(".dennikn.sk"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}

using HomeFocus.Core;
using HomeFocus.Core.Data;
using HomeFocus.Core.Reports;

namespace HomeFocus.Tests;

public sealed class SecurityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"homefocus-sec-{Guid.NewGuid():N}");

    public SecurityTests() => Directory.CreateDirectory(_dir);

    [Theory]
    [InlineData("=cmd|' /C calc'!A0")]
    [InlineData("+SUM(1,2)")]
    [InlineData("-2+3")]
    [InlineData("@HYPERLINK(\"http://x\")")]
    public void Csv_export_neutralizes_formulas_in_app_names(string evil)
    {
        var db = new Database(Path.Combine(_dir, "t.db"));
        db.Initialize();
        var store = new ActivityStore(db);
        var dev = new SettingsStore(db).DeviceId();
        var t = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
        store.Upsert(new ActivityRecord("r1", dev, ActivityKind.App, "evil.exe", evil, null, t, t.AddMinutes(5)));

        var csv = Path.Combine(_dir, "out.csv");
        CsvExporter.Export(store, new Categorizer([], 1), new Dictionary<long, string>(), t.AddHours(-1), t.AddHours(1), csv);

        var cells = File.ReadAllLines(csv)[1].Split(';');
        Assert.DoesNotContain(cells, c => c.TrimStart('"').Length > 0 && "=+-@".Contains(c.TrimStart('"')[0]));
    }

    [Fact]
    public void Pathological_pattern_does_not_hang()
    {
        var pattern = string.Concat(Enumerable.Repeat("*a", 30)) + "b";
        var value = new string('a', 5000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(Categorizer.Matches(pattern, value));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

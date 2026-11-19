using HomeFocus.Core;
using HomeFocus.Core.Ai;
using HomeFocus.Core.Data;
using HomeFocus.Core.Reports;

namespace HomeFocus.Tests;

public class OllamaClientTests
{
    private static readonly string[] Cats = ["Work", "Entertainment", "Other"];

    [Fact]
    public void Request_limits_the_answer_to_the_categories_and_includes_the_job()
    {
        var req = OllamaClient.BuildRequest("llama3.2", "Software developer", Cats, ActivityKind.Web, "github.com", "github.com");
        var json = req.ToJsonString();
        Assert.Contains("\"enum\":[\"Work\",\"Entertainment\",\"Other\"]", json);
        Assert.Contains("Software developer", json);
        Assert.Contains("Website: github.com", json);
        Assert.Equal(false, req["stream"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("{\"category\":\"Work\"}", "Work")]
    [InlineData("{\"category\":\"entertainment\"}", "Entertainment")]
    [InlineData("{\"category\":\"Banking\"}", null)]
    [InlineData("not json", null)]
    [InlineData("", null)]
    public void Answers_outside_the_list_are_ignored(string content, string? expected) =>
        Assert.Equal(expected, OllamaClient.ParseAnswer(content, Cats));

    [Theory]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://192.168.1.20:11434", false)]
    [InlineData("https://ollama.example.com", false)]
    public void Local_servers_are_recognized(string url, bool local) => Assert.Equal(local, OllamaClient.IsLocal(url));

    [Fact]
    public void Non_http_urls_are_rejected() =>
        Assert.Throws<ArgumentException>(() => OllamaClient.Normalize("file:///c:/windows"));
}

public sealed class AutoCategorizerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"homefocus-ai-{Guid.NewGuid():N}.db");
    private readonly Database _db;

    public AutoCategorizerTests()
    {
        _db = new Database(_path);
        _db.Initialize();
    }

    private sealed class FakeClassifier(Func<string, string?> answer) : IClassifier
    {
        public readonly List<string> Asked = [];
        public Task<string?> ClassifyAsync(string position, IReadOnlyList<string> categories, ActivityKind kind, string key, string displayName, CancellationToken ct)
        {
            Asked.Add(key);
            return Task.FromResult(answer(key));
        }
    }

    [Fact]
    public async Task Uncategorized_items_get_AI_rules_and_your_rules_win()
    {
        var store = new ActivityStore(_db);
        var dev = new SettingsStore(_db).DeviceId();
        var cats = new CategoryStore(_db).GetAll();
        var work = cats.Single(c => c.Key == "work").Id;
        var fun = cats.Single(c => c.Key == "entertainment").Id;
        var other = cats.Single(c => c.Key == "other").Id;
        var now = DateTime.UtcNow;
        store.Upsert(new ActivityRecord("1", dev, ActivityKind.App, "jira-desktop.exe", "Jira", null, now.AddMinutes(-30), now.AddMinutes(-20)));
        store.Upsert(new ActivityRecord("2", dev, ActivityKind.Web, "chess.com", "chess.com", null, now.AddMinutes(-20), now.AddMinutes(-10)));
        store.Upsert(new ActivityRecord("3", dev, ActivityKind.Web, "weird.example", "weird.example", null, now.AddMinutes(-10), now.AddMinutes(-5)));

        var rules = new RuleStore(_db);
        var reports = new ReportService(store, () => new Categorizer(rules.GetAll(), other));
        var auto = new AutoCategorizer(reports, rules);
        var ai = new FakeClassifier(k => k switch { "jira-desktop.exe" => "Work", "chess.com" => "Entertainment", _ => null });

        var n = await auto.RunAsync(ai, "Project manager", [(work, "Work"), (fun, "Entertainment"), (other, "Other")], 10, CancellationToken.None);

        Assert.Equal(2, n);
        var all = rules.GetAll();
        Assert.Contains(all, r => r.Pattern == "jira-desktop.exe" && r.CategoryId == work && r.IsAi && !r.IsUser);
        Assert.Contains(all, r => r.Pattern == "chess.com" && r.Kind == ActivityKind.Web && r.CategoryId == fun && r.IsAi);

        // an item the model could not place is not asked about again
        await auto.RunAsync(ai, "Project manager", [(work, "Work"), (fun, "Entertainment"), (other, "Other")], 10, CancellationToken.None);
        Assert.Single(ai.Asked, k => k == "weird.example");

        // changing an AI rule by hand makes it yours
        rules.Set("chess.com", work, ActivityKind.Web);
        var chess = rules.GetAll().Single(r => r.Pattern == "chess.com");
        Assert.True(chess.IsUser);
        Assert.False(chess.IsAi);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}

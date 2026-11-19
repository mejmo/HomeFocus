using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HomeFocus.Core;
using HomeFocus.Core.Browser;
using HomeFocus.Core.Data;
using HomeFocus.Core.Tracking;

namespace HomeFocus.Tests;

public class WebCategorizerTests
{
    [Theory]
    [InlineData("youtube.com", "youtube.com", true)]
    [InlineData("youtube.com", "m.youtube.com", true)]
    [InlineData("youtube.com", "notyoutube.com", false)]
    [InlineData("google.com", "mail.google.com", true)]
    public void Domain_rules_cover_subdomains(string pattern, string domain, bool expected) =>
        Assert.Equal(expected, Categorizer.DomainMatches(pattern, domain));

    [Fact]
    public void App_and_web_rules_do_not_mix()
    {
        var c = new Categorizer([new Rule(1, "youtube.com", 5, false, ActivityKind.Web), new Rule(2, "chrome.exe", 7, false)], 9);
        Assert.Equal(5, c.Resolve(ActivityKind.Web, "www.youtube.com".Replace("www.", "")));
        Assert.Equal(9, c.Resolve(ActivityKind.App, "youtube.com"));
        Assert.Equal(9, c.Resolve(ActivityKind.Web, "chrome.exe"));
        Assert.Equal(7, c.Resolve("chrome.exe"));
    }

    [Fact]
    public void More_specific_domain_wins()
    {
        var c = new Categorizer([new Rule(1, "google.com", 1, false, ActivityKind.Web), new Rule(2, "mail.google.com", 2, false, ActivityKind.Web)], 9);
        Assert.Equal(2, c.Resolve(ActivityKind.Web, "mail.google.com"));
        Assert.Equal(1, c.Resolve(ActivityKind.Web, "maps.google.com"));
    }

    [Theory]
    [InlineData("WWW.YouTube.com", "youtube.com")]
    [InlineData("docs.google.com.", "docs.google.com")]
    [InlineData("evil.com/path", null)]
    [InlineData("a b.com", null)]
    [InlineData("<script>", null)]
    [InlineData("", null)]
    public void Domains_are_normalized_and_validated(string input, string? expected) =>
        Assert.Equal(expected, BrowserState.NormalizeDomain(input));
}

public class WebTrackerTests
{
    private sealed class Src : IForegroundSource
    {
        public ForegroundApp? App = new("chrome.exe", "Google Chrome");
        public ForegroundApp? GetForeground() => App;
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
    }

    private sealed class Sink : IActivitySink
    {
        public readonly Dictionary<string, ActivityRecord> Rows = new();
        public void Upsert(ActivityRecord r) => Rows[r.Id] = r;
        public void Delete(string id) => Rows.Remove(id);
    }

    private static readonly DateTime T0 = new(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Browser_time_is_split_by_website()
    {
        var state = new BrowserState();
        var sink = new Sink();
        var t = new ActivityTracker(new Src(), sink, "pc", state);
        for (var s = 0; s <= 120; s++)
        {
            var now = T0.AddSeconds(s);
            state.Report("c1", "chrome.exe", s < 60 ? "github.com" : "youtube.com", true, now);
            t.Tick(now);
        }
        t.Stop(T0.AddSeconds(120));

        var rows = sink.Rows.Values.OrderBy(r => r.StartUtc).ToList();
        Assert.Equal([ActivityKind.Web, ActivityKind.Web], rows.Select(r => r.Kind));
        Assert.Equal(["github.com", "youtube.com"], rows.Select(r => r.AppKey));
    }

    [Fact]
    public void Without_a_fresh_report_the_browser_is_an_app()
    {
        var state = new BrowserState();
        state.Report("c1", "chrome.exe", "github.com", true, T0);
        Assert.Equal("github.com", state.ActiveDomain("chrome.exe", T0.AddSeconds(10)));
        Assert.Null(state.ActiveDomain("chrome.exe", T0 + BrowserState.Stale + TimeSpan.FromSeconds(1)));
        Assert.Null(state.ActiveDomain("msedge.exe", T0.AddSeconds(10)));

        state.Report("c1", "chrome.exe", "github.com", false, T0.AddSeconds(11)); // window lost focus
        Assert.Null(state.ActiveDomain("chrome.exe", T0.AddSeconds(12)));
    }
}

public sealed class BridgeTests : IDisposable
{
    private const string Id = "pdfhmbelenlkhfdhpbpnmmjndohlhdeb";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"homefocus-bridge-{Guid.NewGuid():N}");
    private readonly SettingsStore _settings;

    public BridgeTests()
    {
        Directory.CreateDirectory(_dir);
        var db = new Database(Path.Combine(_dir, "t.db"));
        db.Initialize();
        _settings = new SettingsStore(db);
    }

    private static Dictionary<string, string> Headers(string host, string? origin) => new()
    {
        ["upgrade"] = "websocket", ["sec-websocket-key"] = "dGhlIHNhbXBsZSBub25jZQ==", ["host"] = host,
        ["origin"] = origin ?? "",
    };

    [Fact]
    public void Handshake_requires_our_origin_and_loopback_host()
    {
        var allowed = new HashSet<string> { "chrome-extension://" + Id };
        const string line = "GET /homefocus HTTP/1.1";
        Assert.Equal(BrowserBridge.HandshakeCheck.Ok, BrowserBridge.Check(Headers("127.0.0.1:47615", "chrome-extension://" + Id), line, allowed));
        Assert.Equal(BrowserBridge.HandshakeCheck.Forbidden, BrowserBridge.Check(Headers("127.0.0.1:47615", "https://evil.com"), line, allowed));
        Assert.Equal(BrowserBridge.HandshakeCheck.Forbidden, BrowserBridge.Check(Headers("127.0.0.1:47615", "chrome-extension://otherextension"), line, allowed));
        Assert.Equal(BrowserBridge.HandshakeCheck.Forbidden, BrowserBridge.Check(Headers("127.0.0.1:47615", null), line, allowed));
        Assert.Equal(BrowserBridge.HandshakeCheck.Forbidden, BrowserBridge.Check(Headers("evil.com:47615", "chrome-extension://" + Id), line, allowed));
        Assert.Equal(BrowserBridge.HandshakeCheck.BadRequest, BrowserBridge.Check(Headers("127.0.0.1:47615", "chrome-extension://" + Id), "GET / HTTP/1.1", allowed));
    }

    [Fact]
    public void Tokens_are_checked_and_revocable()
    {
        var tokens = new PairingTokens(_settings);
        var t = tokens.Issue();
        Assert.True(tokens.IsValid(t));
        Assert.False(tokens.IsValid(t + "x"));
        Assert.False(tokens.IsValid(null));
        Assert.DoesNotContain(t, _settings.Get("bridge_tokens") ?? ""); // only hashes are stored
        tokens.RevokeAll();
        Assert.False(tokens.IsValid(t));
    }

    [Fact]
    public async Task Extension_pairs_once_then_reports_tabs()
    {
        var port = 47700 + Random.Shared.Next(200);
        var state = new BrowserState();
        var approvals = 0;
        using var bridge = new BrowserBridge(state, new PairingTokens(_settings), [Id], _ => { approvals++; return Task.FromResult(true); }, port);
        bridge.Start();
        Assert.True(bridge.IsListening);

        async Task<(ClientWebSocket Ws, JsonElement Reply)> Connect(string? token)
        {
            var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Origin", "chrome-extension://" + Id);
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/homefocus"), CancellationToken.None);
            await SendJson(ws, new { type = "hello", browser = "chrome", token });
            return (ws, await ReceiveJson(ws));
        }

        var (ws1, r1) = await Connect(null);
        Assert.Equal("paired", r1.GetProperty("type").GetString());
        var token = r1.GetProperty("token").GetString();
        await SendJson(ws1, new { type = "tab", domain = "WWW.GitHub.com", focused = true });
        await WaitFor(() => state.ActiveDomain("chrome.exe", DateTime.UtcNow) == "github.com");
        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

        var (ws2, r2) = await Connect(token);
        Assert.Equal("ready", r2.GetProperty("type").GetString());
        Assert.Equal(1, approvals);
        ws2.Dispose();
    }

    [Fact]
    public async Task Web_page_origin_is_rejected()
    {
        var port = 47900 + Random.Shared.Next(90);
        using var bridge = new BrowserBridge(new BrowserState(), new PairingTokens(_settings), [Id], _ => Task.FromResult(true), port);
        bridge.Start();
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "https://evil.example");
        await Assert.ThrowsAnyAsync<WebSocketException>(() => ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/homefocus"), CancellationToken.None));
    }

    private static Task SendJson(ClientWebSocket ws, object o) =>
        ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(o), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<JsonElement> ReceiveJson(ClientWebSocket ws)
    {
        var buf = new byte[4096];
        var r = await ws.ReceiveAsync(buf, new CancellationTokenSource(5000).Token);
        return JsonDocument.Parse(Encoding.UTF8.GetString(buf, 0, r.Count)).RootElement.Clone();
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        for (var i = 0; i < 50 && !cond(); i++) await Task.Delay(50);
        Assert.True(cond());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

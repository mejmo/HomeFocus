using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeFocus.Core.Data;

namespace HomeFocus.Core.Browser;

/// <summary>Pairing tokens the browser extension presents; only SHA-256 hashes are stored.</summary>
public sealed class PairingTokens(SettingsStore settings)
{
    private const string Key = "bridge_tokens";
    private readonly object _gate = new();

    public bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return false;
        var hash = Hash(token);
        lock (_gate) return Load().Any(h => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(h), Convert.FromHexString(hash)));
    }

    public string Issue()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        lock (_gate)
        {
            var list = Load();
            list.Add(Hash(token));
            settings.Set(Key, string.Join(',', list.TakeLast(16)));
        }
        return token;
    }

    public void RevokeAll()
    {
        lock (_gate) settings.Set(Key, "");
    }

    public int Count
    {
        get { lock (_gate) return Load().Count; }
    }

    private List<string> Load() =>
        (settings.Get(Key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Local WebSocket endpoint for the browser extension: ws://127.0.0.1:47615/homefocus.
/// Only the loopback interface is used, only requests whose Origin is one of our extension IDs are accepted,
/// the Host header must be the loopback address (DNS-rebinding guard), and a new browser must be approved
/// once by the user before it gets a pairing token. Nothing is ever sent back except pairing state.
/// </summary>
public sealed class BrowserBridge : IDisposable
{
    public const int Port = 47615;
    public const string PathName = "/homefocus";
    private const int MaxConnections = 8;
    private const int MaxMessage = 4096;

    private readonly BrowserState _state;
    private readonly PairingTokens _tokens;
    private readonly HashSet<string> _allowedOrigins;
    private readonly Func<string, Task<bool>> _approve;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _approval = new(1, 1);
    private readonly int _port;
    private TcpListener? _listener;
    private int _connections;
    private DateTime _deniedUntilUtc;

    /// <param name="extensionIds">Allowed extension IDs (development and Chrome Web Store).</param>
    /// <param name="approve">Asks the user whether a newly connecting browser may pair; gets the browser name.</param>
    public BrowserBridge(BrowserState state, PairingTokens tokens, IEnumerable<string> extensionIds, Func<string, Task<bool>> approve, int port = Port)
    {
        _port = port;
        _state = state;
        _tokens = tokens;
        _approve = approve;
        _allowedOrigins = extensionIds.Where(id => id.Length > 0).Select(id => "chrome-extension://" + id).ToHashSet(StringComparer.Ordinal);
    }

    public bool IsListening { get; private set; }

    /// <summary>Raised (on a background thread) when a browser connects, pairs or disconnects.</summary>
    public event EventHandler? Changed;

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            IsListening = true;
            _ = AcceptLoop(_cts.Token);
        }
        catch (SocketException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Browser bridge could not listen on {_port}: {ex.Message}");
        }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch { return; }
            if (Interlocked.Increment(ref _connections) > MaxConnections)
            {
                Interlocked.Decrement(ref _connections);
                client.Dispose();
                continue;
            }
            _ = Task.Run(async () =>
            {
                try { await Handle(client, ct); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Bridge connection: {ex.Message}"); }
                finally
                {
                    client.Dispose();
                    Interlocked.Decrement(ref _connections);
                }
            }, ct);
        }
    }

    /// <summary>Result of checking the HTTP upgrade request.</summary>
    public enum HandshakeCheck { Ok, BadRequest, Forbidden }

    /// <summary>Validates the upgrade request (path, Host, Origin, WebSocket headers). Public for tests.</summary>
    public static HandshakeCheck Check(IReadOnlyDictionary<string, string> headers, string requestLine, IReadOnlySet<string> allowedOrigins, int port = Port)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length != 3 || parts[0] != "GET" || parts[1] != PathName) return HandshakeCheck.BadRequest;
        if (!headers.TryGetValue("upgrade", out var up) || !up.Equals("websocket", StringComparison.OrdinalIgnoreCase)) return HandshakeCheck.BadRequest;
        if (!headers.TryGetValue("sec-websocket-key", out var key) || key.Length is < 16 or > 64) return HandshakeCheck.BadRequest;
        // DNS rebinding: a web page served from evil.com resolving to 127.0.0.1 would send Host: evil.com.
        if (!headers.TryGetValue("host", out var host) || (host != $"127.0.0.1:{port}" && host != $"localhost:{port}")) return HandshakeCheck.Forbidden;
        // Web pages cannot fake Origin; only our extension may connect.
        if (!headers.TryGetValue("origin", out var origin) || !allowedOrigins.Contains(origin)) return HandshakeCheck.Forbidden;
        return HandshakeCheck.Ok;
    }

    private async Task Handle(TcpClient client, CancellationToken ct)
    {
        client.ReceiveTimeout = 5000;
        var stream = client.GetStream();
        var (requestLine, headers) = await ReadRequest(stream, ct);
        var check = requestLine is null ? HandshakeCheck.BadRequest : Check(headers, requestLine, _allowedOrigins, _port);
        if (check != HandshakeCheck.Ok)
        {
            var status = check == HandshakeCheck.Forbidden ? "403 Forbidden" : "400 Bad Request";
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
            return;
        }

        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(headers["sec-websocket-key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n"), ct);

        using var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(30) });
        var id = Guid.NewGuid().ToString("N");
        try
        {
            await Session(ws, id, ct);
        }
        finally
        {
            _state.Remove(id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task Session(WebSocket ws, string id, CancellationToken ct)
    {
        // 1) hello: { type: "hello", browser: "chrome", token?: "..." }
        JsonElement? hello;
        using (var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10)); // don't let silent clients hold a slot
            hello = await Receive(ws, helloTimeout.Token);
        }
        if (hello is null || Str(hello.Value, "type") != "hello") return;
        var browserKey = BrowserState.ProcessFor(Str(hello.Value, "browser") ?? "");
        if (browserKey is null) return;

        if (!_tokens.IsValid(Str(hello.Value, "token")))
        {
            if (!await Approve(Str(hello.Value, "browser")!))
            {
                await Send(ws, new { type = "denied" }, ct);
                return;
            }
            await Send(ws, new { type = "paired", token = _tokens.Issue() }, ct);
        }
        else
        {
            await Send(ws, new { type = "ready" }, ct);
        }
        Changed?.Invoke(this, EventArgs.Empty);

        // 2) tab reports: { type: "tab", domain: "youtube.com" | null, focused: true } and pings
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var msg = await Receive(ws, ct);
            if (msg is null) return;
            switch (Str(msg.Value, "type"))
            {
                case "tab":
                    var focused = msg.Value.TryGetProperty("focused", out var f) && f.ValueKind == JsonValueKind.True;
                    _state.Report(id, browserKey, BrowserState.NormalizeDomain(Str(msg.Value, "domain")), focused, DateTime.UtcNow);
                    break;
                case "ping":
                    break;
                default:
                    return;
            }
        }
    }

    /// <summary>One approval dialog at a time; after a "no", new requests are refused for 10 minutes.</summary>
    private async Task<bool> Approve(string browser)
    {
        if (DateTime.UtcNow < _deniedUntilUtc) return false;
        await _approval.WaitAsync();
        try
        {
            if (DateTime.UtcNow < _deniedUntilUtc) return false;
            var ok = await _approve(browser);
            if (!ok) _deniedUntilUtc = DateTime.UtcNow.AddMinutes(10);
            return ok;
        }
        finally
        {
            _approval.Release();
        }
    }

    private static async Task<(string? RequestLine, Dictionary<string, string> Headers)> ReadRequest(NetworkStream s, CancellationToken ct)
    {
        var buf = new byte[8192];
        var len = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (len < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(len, 1), timeout.Token); // byte-wise: never read past the headers
            if (n == 0) break;
            len += n;
            if (len >= 4 && buf[len - 4] == '\r' && buf[len - 3] == '\n' && buf[len - 2] == '\r' && buf[len - 1] == '\n')
            {
                var lines = Encoding.ASCII.GetString(buf, 0, len).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1))
                {
                    var i = line.IndexOf(':');
                    if (i > 0) headers[line[..i].Trim().ToLowerInvariant()] = line[(i + 1)..].Trim();
                }
                return (lines.FirstOrDefault(), headers);
            }
        }
        return (null, new Dictionary<string, string>());
    }

    private static async Task<JsonElement?> Receive(WebSocket ws, CancellationToken ct)
    {
        var buf = new byte[MaxMessage];
        var len = 0;
        while (true)
        {
            var r = await ws.ReceiveAsync(buf.AsMemory(len), ct);
            if (r.MessageType == WebSocketMessageType.Close)
            {
                // complete the close handshake so the browser sees a clean disconnect
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct); } catch (WebSocketException) { }
                return null;
            }
            len += r.Count;
            if (r.EndOfMessage) break;
            if (len >= buf.Length) return null; // too big
        }
        try
        {
            using var doc = JsonDocument.Parse(buf.AsMemory(0, len));
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task Send(WebSocket ws, object payload, CancellationToken ct) =>
        ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload), WebSocketMessageType.Text, true, ct);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        IsListening = false;
    }
}

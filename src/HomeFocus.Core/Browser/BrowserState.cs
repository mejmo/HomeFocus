using System.Text.RegularExpressions;
using HomeFocus.Core.Tracking;

namespace HomeFocus.Core.Browser;

/// <summary>
/// The active tab of every connected browser window, as reported by the extension.
/// Thread-safe: the bridge writes, the tracker reads.
/// </summary>
public sealed partial class BrowserState : IBrowserActivity
{
    /// <summary>Reports older than this are ignored (the extension pings every 20 s).</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromSeconds(45);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byConnection = new();

    private sealed record Entry(string BrowserKey, string? Domain, bool Focused, DateTime SeenUtc, DateTime ChangedUtc);

    /// <summary>Browser name from the extension → process name the tracker sees.</summary>
    public static string? ProcessFor(string browser) => browser switch
    {
        "chrome" => "chrome.exe",
        "edge" => "msedge.exe",
        "brave" => "brave.exe",
        "opera" => "opera.exe",
        "vivaldi" => "vivaldi.exe",
        _ => null,
    };

    /// <summary>Lower-cases and validates a host name; strips a leading "www.". Returns null when it isn't one.</summary>
    public static string? NormalizeDomain(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253) return null;
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        return DomainRegex().IsMatch(host) ? host : null;
    }

    public void Report(string connectionId, string browserKey, string? domain, bool focused, DateTime nowUtc)
    {
        lock (_gate)
        {
            var changed = !_byConnection.TryGetValue(connectionId, out var old) || old.Focused != focused || old.Domain != domain
                ? nowUtc : old.ChangedUtc;
            _byConnection[connectionId] = new Entry(browserKey, domain, focused, nowUtc, changed);
        }
    }

    public void Remove(string connectionId)
    {
        lock (_gate) _byConnection.Remove(connectionId);
    }

    /// <summary>Connected browsers (process names), for the settings page.</summary>
    public IReadOnlyList<string> ConnectedBrowsers()
    {
        lock (_gate) return _byConnection.Values.Select(e => e.BrowserKey).Distinct().ToList();
    }

    public string? ActiveDomain(string browserAppKey, DateTime nowUtc)
    {
        lock (_gate)
        {
            // Several profiles/windows can report; the focused one that changed last is in front.
            return _byConnection.Values
                .Where(e => e.Focused && nowUtc - e.SeenUtc <= Stale &&
                            string.Equals(e.BrowserKey, browserAppKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.ChangedUtc)
                .FirstOrDefault()?.Domain;
        }
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex DomainRegex();
}

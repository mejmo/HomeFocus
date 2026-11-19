using System.Text.RegularExpressions;

namespace HomeFocus.Core;

/// <summary>Resolves an app ("devenv.exe") or a website ("youtube.com") to a category using the rules.</summary>
public sealed class Categorizer
{
    private readonly List<(Rule Rule, Regex? Regex)> _apps;
    private readonly List<(Rule Rule, Regex? Regex)> _webs;
    private readonly Dictionary<string, long> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Categorizer(IEnumerable<Rule> rules, long fallbackCategoryId)
    {
        FallbackCategoryId = fallbackCategoryId;
        var list = rules.ToList();
        _apps = Order(list.Where(r => r.Kind != ActivityKind.Web));
        _webs = Order(list.Where(r => r.Kind == ActivityKind.Web));
    }

    // User rules win; then exact patterns before wildcards; then longer (more specific) patterns first.
    private static List<(Rule, Regex?)> Order(IEnumerable<Rule> rules) => rules
        .OrderByDescending(r => r.IsUser)
        .ThenBy(r => HasWildcard(r.Pattern))
        .ThenByDescending(r => r.Pattern.Replace("*", "").Length)
        .Select(r => (r, HasWildcard(r.Pattern) ? ToRegex(r.Pattern) : null))
        .ToList();

    public long FallbackCategoryId { get; }

    public long Resolve(string appKey) => TryMatch(appKey) ?? FallbackCategoryId;

    public long Resolve(ActivityKind kind, string key) => TryMatch(kind, key) ?? FallbackCategoryId;

    /// <summary>The matching app rule's category, or null when no rule matches.</summary>
    public long? TryMatch(string appKey) => TryMatch(ActivityKind.App, appKey);

    /// <summary>The matching rule's category for an app or a website, or null when no rule matches.</summary>
    public long? TryMatch(ActivityKind kind, string key)
    {
        var web = kind == ActivityKind.Web;
        var cacheKey = (web ? "w:" : "a:") + key;
        if (_cache.TryGetValue(cacheKey, out var hit)) return hit < 0 ? null : hit;
        long? result = null;
        foreach (var (rule, regex) in web ? _webs : _apps)
        {
            var ok = regex is not null ? SafeMatch(regex, key)
                : web ? DomainMatches(rule.Pattern, key)
                : string.Equals(rule.Pattern, key, StringComparison.OrdinalIgnoreCase);
            if (ok) { result = rule.CategoryId; break; }
        }
        _cache[cacheKey] = result ?? -1;
        return result;
    }

    /// <summary>"youtube.com" matches youtube.com and m.youtube.com, but not notyoutube.com.</summary>
    public static bool DomainMatches(string pattern, string domain) =>
        string.Equals(pattern, domain, StringComparison.OrdinalIgnoreCase) ||
        domain.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase);

    public static bool Matches(string pattern, string value) =>
        HasWildcard(pattern) ? SafeMatch(ToRegex(pattern), value) : string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);

    // Patterns like "*a*a*a*..." could backtrack for a long time; a timeout just means "no match".
    private static bool SafeMatch(Regex r, string value)
    {
        try { return r.IsMatch(value); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static bool HasWildcard(string p) => p.Contains('*') || p.Contains('?');

    private static Regex ToRegex(string pattern) => new(
        "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}

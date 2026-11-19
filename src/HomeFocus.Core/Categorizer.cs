using System.Text.RegularExpressions;

namespace HomeFocus.Core;

/// <summary>Resolves an app key (e.g. "devenv.exe") to a category using the rules.</summary>
public sealed class Categorizer
{
    private readonly List<(Rule Rule, Regex? Regex)> _ordered;
    private readonly Dictionary<string, long> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Categorizer(IEnumerable<Rule> rules, long fallbackCategoryId)
    {
        FallbackCategoryId = fallbackCategoryId;
        // User rules win; then exact patterns before wildcards; then longer (more specific) patterns first.
        _ordered = rules
            .OrderByDescending(r => r.IsUser)
            .ThenBy(r => HasWildcard(r.Pattern))
            .ThenByDescending(r => r.Pattern.Replace("*", "").Length)
            .Select(r => (r, HasWildcard(r.Pattern) ? ToRegex(r.Pattern) : null))
            .ToList();
    }

    public long FallbackCategoryId { get; }

    public long Resolve(string appKey) => TryMatch(appKey) ?? FallbackCategoryId;

    /// <summary>The matching rule's category, or null when no rule matches.</summary>
    public long? TryMatch(string appKey)
    {
        if (_cache.TryGetValue(appKey, out var hit)) return hit < 0 ? null : hit;
        long? result = null;
        foreach (var (rule, regex) in _ordered)
        {
            var ok = regex is null ? string.Equals(rule.Pattern, appKey, StringComparison.OrdinalIgnoreCase) : SafeMatch(regex, appKey);
            if (ok) { result = rule.CategoryId; break; }
        }
        _cache[appKey] = result ?? -1;
        return result;
    }

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

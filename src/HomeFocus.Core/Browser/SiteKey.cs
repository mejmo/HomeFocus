namespace HomeFocus.Core.Browser;

/// <summary>
/// Groups host names into sites: every subdomain counts as its site, so auto.dennikn.sk and sport.dennikn.sk are both
/// "dennikn.sk". A rule written for a specific subdomain (e.g. mail.google.com) keeps that subdomain separate.
/// </summary>
public static class SiteKey
{
    // Two-part public suffixes (a short, practical subset of the Public Suffix List).
    private static readonly HashSet<string> MultiPartSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "ltd.uk", "plc.uk", "net.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au", "co.nz", "org.nz", "co.za", "co.jp", "ne.jp", "or.jp", "ac.jp",
        "co.kr", "or.kr", "com.br", "com.mx", "com.ar", "com.tr", "com.cn", "com.hk", "com.tw", "com.sg", "com.my",
        "co.in", "co.il", "com.ua", "com.pl", "co.at", "or.at",
        // hosting platforms where each subdomain is a different site
        "github.io", "gitlab.io", "netlify.app", "vercel.app", "pages.dev", "workers.dev", "herokuapp.com", "web.app",
        "firebaseapp.com", "blogspot.com", "azurewebsites.net", "cloudfront.net", "onrender.com", "fly.dev", "glitch.me",
        "wordpress.com", "substack.com", "notion.site", "carrd.co", "wixsite.com",
    };

    /// <summary>The site a host belongs to ("hovno.dennikn.sk" → "dennikn.sk", "x.github.io" stays).</summary>
    public static string Registrable(string host)
    {
        host = host.ToLowerInvariant().TrimEnd('.');
        if (System.Net.IPAddress.TryParse(host, out _)) return host;
        var labels = host.Split('.');
        if (labels.Length <= 2) return host;
        var lastTwo = labels[^2] + "." + labels[^1];
        var take = MultiPartSuffixes.Contains(lastTwo) ? 3 : 2;
        return string.Join('.', labels[^Math.Min(take, labels.Length)..]);
    }

    /// <summary>
    /// The key a visit is recorded under: the most specific rule pattern that is a subdomain of the site and covers
    /// the host (so mail.google.com stays apart from google.com), otherwise the site itself.
    /// </summary>
    public static string For(string host, IEnumerable<string> webRulePatterns)
    {
        host = host.ToLowerInvariant();
        var site = Registrable(host);
        string? best = null;
        foreach (var p in webRulePatterns)
        {
            if (p.Contains('*') || p.Length <= site.Length || !p.EndsWith("." + site, StringComparison.OrdinalIgnoreCase)) continue;
            if (!host.Equals(p, StringComparison.OrdinalIgnoreCase) && !host.EndsWith("." + p, StringComparison.OrdinalIgnoreCase)) continue;
            if (best is null || p.Length > best.Length) best = p.ToLowerInvariant();
        }
        return best ?? site;
    }
}

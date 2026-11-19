using System.Globalization;

namespace HomeFocus.App.Services;

public static class Format
{
    /// <summary>"5 h 39 min", "12 min", "40 s".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.FromMinutes(1)) return t.TotalSeconds < 1 ? "0 min" : $"{(int)t.TotalSeconds} s";
        var h = (int)t.TotalHours;
        var m = t.Minutes;
        return h > 0 ? (m > 0 ? $"{h} h {m} min" : $"{h} h") : $"{m} min";
    }

    /// <summary>Compact form for tight spots: "5:39 h", "12 min".</summary>
    public static string Short(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00} h" : $"{Math.Max(0, t.Minutes)} min";

    public static string Day(DateOnly d) => Capitalize(d.ToDateTime(TimeOnly.MinValue).ToString("D", HomeFocus.App.Localization.Loc.Culture));

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], HomeFocus.App.Localization.Loc.Culture) + s[1..];
}

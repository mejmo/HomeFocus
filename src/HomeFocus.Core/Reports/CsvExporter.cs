using System.Globalization;
using System.Text;
using HomeFocus.Core.Data;

namespace HomeFocus.Core.Reports;

public static class CsvExporter
{
    /// <summary>Writes every record overlapping [fromUtc, toUtc) as semicolon-separated CSV (Excel-friendly in most European locales).</summary>
    public static int Export(ActivityStore store, Categorizer cat, IReadOnlyDictionary<long, string> categoryNames,
        DateTime fromUtc, DateTime toUtc, string path)
    {
        var rows = store.Query(fromUtc, toUtc);
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine("start;end;duration_s;type;app;name;category;device");
        foreach (var r in rows)
        {
            var c = r.Kind == ActivityKind.Idle ? "" :
                categoryNames.GetValueOrDefault(r.CategoryOverride ?? cat.Resolve(r.Kind, r.AppKey), "");
            w.WriteLine(string.Join(';',
                r.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                r.EndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ((long)r.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                r.Kind switch { ActivityKind.Idle => "idle", ActivityKind.Web => "web", _ => "app" },
                Esc(r.AppKey), Esc(r.DisplayName), Esc(c), Esc(r.DeviceId)));
        }
        return rows.Count;
    }

    /// <summary>
    /// Quotes a cell. App names come from other programs' version info, so a value starting with
    /// = + - @ (or a tab / CR) is prefixed with ' to stop spreadsheets from running it as a formula.
    /// </summary>
    internal static string Esc(string s)
    {
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return s.IndexOfAny([';', ',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}

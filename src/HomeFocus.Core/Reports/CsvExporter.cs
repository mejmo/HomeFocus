using System.Globalization;
using System.Text;
using HomeFocus.Core.Data;

namespace HomeFocus.Core.Reports;

public static class CsvExporter
{
    /// <summary>Writes every record overlapping [fromUtc, toUtc) as semicolon-separated CSV (Excel-friendly for sk-SK).</summary>
    public static int Export(ActivityStore store, Categorizer cat, IReadOnlyDictionary<long, string> categoryNames,
        DateTime fromUtc, DateTime toUtc, string path)
    {
        var rows = store.Query(fromUtc, toUtc);
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine("zaciatok;koniec;trvanie_s;typ;aplikacia;nazov;kategoria;zariadenie");
        foreach (var r in rows)
        {
            var c = r.Kind == ActivityKind.Idle ? "" :
                categoryNames.GetValueOrDefault(r.CategoryOverride ?? cat.Resolve(r.AppKey), "");
            w.WriteLine(string.Join(';',
                r.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                r.EndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ((long)r.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                r.Kind == ActivityKind.Idle ? "necinnost" : "aplikacia",
                Esc(r.AppKey), Esc(r.DisplayName), Esc(c), Esc(r.DeviceId)));
        }
        return rows.Count;
    }

    private static string Esc(string s) =>
        s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}

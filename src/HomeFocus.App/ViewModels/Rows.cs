using System.Windows.Media;
using HomeFocus.App.Controls;
using HomeFocus.App.Services;
using HomeFocus.Core.Reports;

namespace HomeFocus.App.ViewModels;

public sealed record AppRow(int Rank, string Name, string AppKey, string Category, Brush Brush, Brush Tint, string Time, double Fraction, string Share)
{
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
}

public sealed record CategoryRow(string Name, Brush Brush, string Time, string Percent, double Fraction);

public sealed record LegendItem(string Name, Brush Brush);

public sealed record Option<T>(string Name, T Value)
{
    public override string ToString() => Name;
}

internal static class RowBuilder
{
    public static List<AppRow> Apps(IReadOnlyList<AppTotal> apps, int take)
    {
        var s = AppServices.Instance;
        var max = apps.Count > 0 ? apps[0].Duration.TotalSeconds : 1;
        var total = Math.Max(1, apps.Sum(a => a.Duration.TotalSeconds));
        return apps.Take(take).Select((a, i) =>
        {
            var c = s.Category(a.CategoryId);
            return new AppRow(i + 1, a.DisplayName, a.AppKey, c.Name, c.Brush, c.Tint, Format.Duration(a.Duration),
                a.Duration.TotalSeconds / max, $"{a.Duration.TotalSeconds / total * 100:0} %");
        }).ToList();
    }

    public static List<CategoryRow> Categories(IReadOnlyList<CategoryTotal> cats)
    {
        var s = AppServices.Instance;
        var total = Math.Max(1, cats.Sum(c => c.Duration.TotalSeconds));
        var max = cats.Count > 0 ? cats.Max(c => c.Duration.TotalSeconds) : 1;
        return cats.Select(ct =>
        {
            var c = s.Category(ct.CategoryId);
            return new CategoryRow(c.Name, c.Brush, Format.Duration(ct.Duration), $"{ct.Duration.TotalSeconds / total * 100:0} %",
                ct.Duration.TotalSeconds / max);
        }).ToList();
    }

    public static List<ShareItem> Shares(IReadOnlyList<CategoryTotal> cats) =>
        cats.Select(c => new ShareItem(AppServices.Instance.Category(c.CategoryId).Brush, c.Duration.TotalSeconds)).ToList();

    public static List<LegendItem> Legend() =>
        AppServices.Instance.CategoryList.Select(c => new LegendItem(c.Name, c.Brush)).ToList();
}

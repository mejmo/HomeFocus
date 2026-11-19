using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeFocus.App.Controls;
using HomeFocus.App.Services;

namespace HomeFocus.App.ViewModels;

public sealed partial class ReportViewModel : PageViewModel
{
    [ObservableProperty] private bool _isWeek = true;
    [ObservableProperty] private DateOnly _anchor = DateOnly.FromDateTime(DateTime.Now);
    [ObservableProperty] private Option<long?>? _filter;
    [ObservableProperty] private IReadOnlyList<Option<long?>> _filters = [];
    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _avgText = "";
    [ObservableProperty] private string _bestText = "";
    [ObservableProperty] private string _bestSub = "";
    [ObservableProperty] private string _appCountText = "";
    [ObservableProperty] private IReadOnlyList<BarItem> _bars = [];
    [ObservableProperty] private IReadOnlyList<AppRow> _apps = [];
    [ObservableProperty] private IReadOnlyList<CategoryRow> _categories = [];
    [ObservableProperty] private IReadOnlyList<ShareItem> _shares = [];
    [ObservableProperty] private IReadOnlyList<LegendItem> _legend = [];
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _isEmpty;

    private bool _loading;

    public bool IsMonth
    {
        get => !IsWeek;
        set => IsWeek = !value;
    }

    partial void OnIsWeekChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMonth));
        Refresh();
    }

    partial void OnAnchorChanged(DateOnly value) => Refresh();
    partial void OnFilterChanged(Option<long?>? value) { if (!_loading) Refresh(); }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private (DateOnly From, DateOnly To) Range()
    {
        if (IsWeek)
        {
            var offset = ((int)Anchor.DayOfWeek + 6) % 7; // Monday first
            var from = Anchor.AddDays(-offset);
            return (from, from.AddDays(7));
        }
        var m = new DateOnly(Anchor.Year, Anchor.Month, 1);
        return (m, m.AddMonths(1));
    }

    [RelayCommand] private void Prev() => Anchor = IsWeek ? Anchor.AddDays(-7) : Anchor.AddMonths(-1);
    [RelayCommand] private void Next() { if (CanGoNext) Anchor = IsWeek ? Anchor.AddDays(7) : Anchor.AddMonths(1); }

    [RelayCommand]
    private void Export()
    {
        var (from, to) = Range();
        ExportHelper.ExportCsv(from, to, $"homefocus-{from:yyyy-MM-dd}-{to.AddDays(-1):yyyy-MM-dd}.csv");
    }

    public override void Refresh()
    {
        var s = AppServices.Instance;
        _loading = true;
        var keep = Filter?.Value;
        Filters = new[] { new Option<long?>("Všetky kategórie", null) }
            .Concat(s.CategoryList.Select(c => new Option<long?>(c.Name, c.Id))).ToList();
        Filter = Filters.FirstOrDefault(f => f.Value == keep) ?? Filters[0];
        _loading = false;

        var (from, to) = Range();
        var r = s.Reports.GetRange(from, to, Filter?.Value);
        CanGoNext = to <= Today;
        RangeText = IsWeek
            ? $"{from.ToString("d. MMMM", Format.Sk)} – {to.AddDays(-1).ToString("d. MMMM yyyy", Format.Sk)}"
            : Format.Capitalize(from.ToString("MMMM yyyy", Format.Sk));

        var activeDays = r.Days.Count(d => d.Total > TimeSpan.Zero);
        TotalText = Format.Duration(r.Active);
        AvgText = activeDays > 0 ? Format.Duration(r.Active / activeDays) : "—";
        var best = r.Days.OrderByDescending(d => d.Total).FirstOrDefault();
        BestText = best is { } b && b.Total > TimeSpan.Zero ? Format.Capitalize(b.Day.ToString(IsWeek ? "dddd" : "d. MMMM", Format.Sk)) : "—";
        BestSub = best is { } bb && bb.Total > TimeSpan.Zero ? Format.Duration(bb.Total) : "";
        AppCountText = r.Apps.Count.ToString(Format.Sk);

        Bars = r.Days.Select(d =>
        {
            var label = IsWeek ? Format.Capitalize(d.Day.ToString("ddd d.", Format.Sk)) : d.Day.Day.ToString(Format.Sk);
            var parts = s.CategoryList
                .Select(c => new BarPart(c.Brush, d.Categories.FirstOrDefault(x => x.CategoryId == c.Id)?.Duration.TotalMinutes ?? 0))
                .Where(p => p.Minutes > 0).ToList();
            var tip = $"{Format.Capitalize(d.Day.ToString("dddd d. M.", Format.Sk))} · {Format.Duration(d.Total)}";
            return new BarItem(label, parts, tip);
        }).ToList();

        Apps = RowBuilder.Apps(r.Apps, 10);
        Categories = RowBuilder.Categories(r.Categories);
        Shares = RowBuilder.Shares(r.Categories);
        Legend = RowBuilder.Legend();
        IsEmpty = r.Apps.Count == 0;
    }
}

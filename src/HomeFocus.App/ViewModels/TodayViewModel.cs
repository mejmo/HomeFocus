using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeFocus.App.Controls;
using HomeFocus.App.Localization;
using HomeFocus.App.Services;

namespace HomeFocus.App.ViewModels;

public sealed partial class TodayViewModel : PageViewModel
{
    [ObservableProperty] private DateOnly _day = DateOnly.FromDateTime(DateTime.Now);
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _activeText = "";
    [ObservableProperty] private string _activeSub = "";
        [ObservableProperty] private string _topCategoryText = "—";
    [ObservableProperty] private string _topCategorySub = "";
    [ObservableProperty] private System.Windows.Media.Brush? _topCategoryBrush;
    [ObservableProperty] private string _idleText = "";
    [ObservableProperty] private string _idleSub = "";
    [ObservableProperty] private string _topAppText = "—";
    [ObservableProperty] private string _topAppSub = "";
    [ObservableProperty] private IReadOnlyList<TimelineItem> _timeline = [];
    [ObservableProperty] private double _timelineStart = 6;
    [ObservableProperty] private double? _nowHour;
    [ObservableProperty] private IReadOnlyList<AppRow> _apps = [];
    [ObservableProperty] private IReadOnlyList<CategoryRow> _categories = [];
    [ObservableProperty] private IReadOnlyList<ShareItem> _shares = [];
    [ObservableProperty] private IReadOnlyList<LegendItem> _legend = [];
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _canGoNext;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    partial void OnDayChanged(DateOnly value) => Refresh();

    [RelayCommand] private void PrevDay() => Day = Day.AddDays(-1);
    [RelayCommand] private void NextDay() { if (Day < Today) Day = Day.AddDays(1); }
    [RelayCommand] private void GoToday() => Day = Today;

    public override void Refresh()
    {
        var s = AppServices.Instance;
        var d = s.Reports.GetDay(Day);
        var prev = s.Reports.GetDay(Day.AddDays(-1));
        var isToday = Day == Today;

        Title = isToday ? Loc.T("today.title") : Day == Today.AddDays(-1) ? Loc.T("today.yesterday") : Format.Capitalize(Day.ToString("dddd", Loc.Culture));
        DateText = Format.Day(Day);
        CanGoNext = Day < Today;

        ActiveText = Format.Duration(d.Active);
        var diff = d.Active - prev.Active;
        ActiveSub = prev.Active == TimeSpan.Zero ? Loc.T("today.noPrev")
            : Math.Abs(diff.TotalMinutes) < 1 ? Loc.T("today.same")
            : Loc.F(diff > TimeSpan.Zero ? "today.more" : "today.less", Format.Duration(diff.Duration()));

        if (d.Categories.Count > 0)
        {
            var top = d.Categories[0];
            var c = s.Category(top.CategoryId);
            TopCategoryText = c.Name;
            TopCategoryBrush = c.Brush;
            TopCategorySub = Loc.F("today.share", Format.Duration(top.Duration), top.Duration.TotalSeconds / Math.Max(1, d.Active.TotalSeconds) * 100);
        }
        else
        {
            TopCategoryText = "—";
            TopCategoryBrush = null;
            TopCategorySub = Loc.T("today.nothing");
        }

        IdleText = Format.Duration(d.Idle);
        IdleSub = Loc.F("today.threshold", s.Tracking.IdleThreshold.TotalMinutes);
        TopAppText = d.Apps.Count > 0 ? d.Apps[0].DisplayName : "—";
        TopAppSub = d.Apps.Count > 0 ? Format.Duration(d.Apps[0].Duration) : Loc.T("today.nothing");

        var dayStart = Day.ToDateTime(TimeOnly.MinValue);
        double H(DateTime t) => (t - dayStart).TotalHours;
        Timeline = d.Timeline.Select(t =>
        {
            var name = t.CategoryId is { } id ? s.Category(id).Name : Loc.T("common.idle");
            return new TimelineItem(H(t.StartLocal), H(t.EndLocal), t.CategoryId is { } cid ? s.Category(cid).Brush : null,
                $"{t.StartLocal:HH:mm}–{t.EndLocal:HH:mm} · {(t.CategoryId is null ? name : $"{t.Label} · {name}")}");
        }).ToList();
        var first = Timeline.Count > 0 ? Timeline.Min(t => t.StartHour) : 8;
        TimelineStart = Math.Max(0, Math.Min(6, Math.Floor(first)));
        NowHour = isToday ? H(DateTime.Now) : null;

        Apps = RowBuilder.Apps(d.Apps, 8);
        Categories = RowBuilder.Categories(d.Categories);
        Shares = RowBuilder.Shares(d.Categories);
        Legend = RowBuilder.Legend();
        IsEmpty = d.Apps.Count == 0;
    }

    public override void OnTimer()
    {
        if (Day == Today) Refresh();
        else if (Day > Today) Day = Today;
    }
}

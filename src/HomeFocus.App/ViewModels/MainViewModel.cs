using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeFocus.App.Localization;
using HomeFocus.App.Services;
using HomeFocus.Core;

namespace HomeFocus.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _status = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _ticks;

    public MainViewModel()
    {
        Today = new TodayViewModel();
        Report = new ReportViewModel();
        Rules = new RulesViewModel();
        Settings = new SettingsViewModel();
        _current = Today;
        _status.Tick += (_, _) => OnTick();
        AppServices.Instance.LookupsChanged += (_, _) => { if (CurrentPage != Rules) CurrentPage.Refresh(); };
    }

    public TodayViewModel Today { get; }
    public ReportViewModel Report { get; }
    public RulesViewModel Rules { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private PageViewModel _current;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _statusText = Loc.T("status.active");
    [ObservableProperty] private string _nowText = "";

    public PageViewModel CurrentPage
    {
        get => Current;
        set
        {
            if (Current == value) return;
            Current = value;
            value.Refresh();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsToday));
            OnPropertyChanged(nameof(IsReport));
            OnPropertyChanged(nameof(IsRules));
            OnPropertyChanged(nameof(IsSettings));
        }
    }

    public bool IsToday { get => Current == Today; set { if (value) CurrentPage = Today; } }
    public bool IsReport { get => Current == Report; set { if (value) CurrentPage = Report; } }
    public bool IsRules { get => Current == Rules; set { if (value) CurrentPage = Rules; } }
    public bool IsSettings { get => Current == Settings; set { if (value) CurrentPage = Settings; } }

    public void Start()
    {
        Current.Refresh();
        OnTick();
        _status.Start();
    }

    /// <summary>Called when the window becomes visible again.</summary>
    public void Activate() => Current.Refresh();

    [RelayCommand] private void Pause(string minutes) => AppServices.Instance.Tracking.Pause(TimeSpan.FromMinutes(int.Parse(minutes)));
    [RelayCommand] private void Resume() => AppServices.Instance.Tracking.Resume();

    private void OnTick()
    {
        var t = AppServices.Instance.Tracking;
        IsPaused = t.IsPaused;
        if (IsPaused && t.PausedUntilUtc is { } u)
        {
            StatusText = Loc.T("status.paused");
            NowText = Loc.F("status.until", u.ToLocalTime());
        }
        else
        {
            StatusText = Loc.T("status.active");
            NowText = t.Current switch
            {
                { Kind: ActivityKind.App } c => Loc.F("status.now", c.DisplayName, Format.Duration(c.Duration)),
                { Kind: ActivityKind.Idle } c => Loc.F("status.idle", Format.Duration(c.Duration)),
                _ => Loc.T("status.waiting"),
            };
        }
        if (++_ticks % 10 == 0) Current.OnTimer();
    }
}

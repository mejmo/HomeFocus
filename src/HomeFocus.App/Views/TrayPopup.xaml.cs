using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using HomeFocus.App.Controls;
using HomeFocus.App.Localization;
using HomeFocus.App.Services;
using HomeFocus.App.ViewModels;
using HomeFocus.Core;

namespace HomeFocus.App.Views;

public sealed partial class TrayPopupViewModel : ObservableObject
{
    [ObservableProperty] private string _activeText = "";
    [ObservableProperty] private string _pillText = "";
    [ObservableProperty] private string _nowText = "";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private IReadOnlyList<ShareItem> _shares = [];
    [ObservableProperty] private IReadOnlyList<CategoryRow> _categories = [];

    public void Refresh()
    {
        var s = AppServices.Instance;
        var d = s.Reports.GetDay(DateOnly.FromDateTime(DateTime.Now));
        ActiveText = Format.Duration(d.Active);
        Shares = RowBuilder.Shares(d.Categories);
        Categories = RowBuilder.Categories(d.Categories).Take(4).ToList();
        IsPaused = s.Tracking.IsPaused;
        PillText = IsPaused && s.Tracking.PausedUntilUtc is { } u ? Loc.F("tray.pausedUntil", u.ToLocalTime()) : Loc.T("tray.tracking");
        NowText = s.Tracking.Current is { Kind: ActivityKind.App } c ? Loc.F("status.now", c.DisplayName, Format.Duration(c.Duration)) : "";
    }
}

/// <summary>Popup shown from the tray icon, anchored above the taskbar near the cursor.</summary>
public partial class TrayPopup : Window
{
    private readonly TrayPopupViewModel _vm = new();
    private readonly Action _open;
    private readonly Action _quit;

    public TrayPopup(Action open, Action quit)
    {
        InitializeComponent();
        DataContext = _vm;
        _open = open;
        _quit = quit;
        Deactivated += (_, _) => Hide();
    }

    public void ShowNear(Point cursorDip)
    {
        _vm.Refresh();
        Opacity = 0;
        Show();
        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        var left = Math.Clamp(cursorDip.X - ActualWidth / 2, wa.Left, wa.Right - ActualWidth);
        // Taskbar at the bottom (usual) → above it; at the top → below it.
        var top = cursorDip.Y > wa.Top + wa.Height / 2 ? wa.Bottom - ActualHeight : wa.Top;
        Left = left;
        Top = top;
        Opacity = 1;
        Activate();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _open();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        var minutes = sender == P15 ? 15 : sender == P30 ? 30 : 60;
        AppServices.Instance.Tracking.Pause(TimeSpan.FromMinutes(minutes));
        _vm.Refresh();
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Instance.Tracking.Resume();
        _vm.Refresh();
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _quit();
    }
}

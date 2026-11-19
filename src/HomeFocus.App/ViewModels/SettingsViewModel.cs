using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeFocus.App.Localization;
using HomeFocus.App.Services;
using HomeFocus.App.Views;

namespace HomeFocus.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    public IReadOnlyList<Option<int>> IdleOptions { get; } =
        [new("1 min", 1), new("2 min", 2), new("3 min", 3), new("5 min", 5), new("10 min", 10), new("15 min", 15)];

    public IReadOnlyList<Option<string>> ThemeOptions { get; } =
        [new(Loc.T("settings.themeSystem"), "system"), new(Loc.T("settings.themeLight"), "light"), new(Loc.T("settings.themeDark"), "dark")];

    [ObservableProperty] private Option<int>? _idle;
    [ObservableProperty] private Option<string>? _theme;
    [ObservableProperty] private bool _autostart;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private string _dbPath = "";
    [ObservableProperty] private string _dbInfo = "";

    private bool _loading;

    public override void Refresh()
    {
        _loading = true;
        var s = AppServices.Instance;
        var idle = s.Settings.GetInt(SettingKeys.IdleMinutes, 3);
        Idle = IdleOptions.FirstOrDefault(o => o.Value == idle) ?? IdleOptions[2];
        Theme = ThemeOptions.FirstOrDefault(o => o.Value == (s.Settings.Get(SettingKeys.Theme) ?? "system")) ?? ThemeOptions[0];
        Autostart = AutoStart.IsEnabled();
        CloseToTray = s.Settings.GetBool(SettingKeys.CloseToTray, true);
        DbPath = s.Db.Path;
        var size = new FileInfo(s.Db.Path).Exists ? new FileInfo(s.Db.Path).Length : 0;
        if (File.Exists(s.Db.Path + "-wal")) size += new FileInfo(s.Db.Path + "-wal").Length;
        DbInfo = Loc.F("settings.dbInfo", size / 1024.0 / 1024.0);
        _loading = false;
    }

    partial void OnIdleChanged(Option<int>? value)
    {
        if (_loading || value is null) return;
        var s = AppServices.Instance;
        s.Settings.SetInt(SettingKeys.IdleMinutes, value.Value);
        s.Tracking.IdleThreshold = TimeSpan.FromMinutes(value.Value);
    }

    partial void OnThemeChanged(Option<string>? value)
    {
        if (_loading || value is null) return;
        AppServices.Instance.Settings.Set(SettingKeys.Theme, value.Value);
        ThemeManager.Apply(value.Value);
    }

    partial void OnAutostartChanged(bool value)
    {
        if (_loading) return;
        AutoStart.Set(value);
        AppServices.Instance.Settings.SetBool(SettingKeys.Autostart, value);
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_loading) AppServices.Instance.Settings.SetBool(SettingKeys.CloseToTray, value);
    }

    [RelayCommand]
    private void OpenFolder() => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + DbPath } });

    [RelayCommand]
    private void ExportAll() =>
        ExportHelper.ExportCsv(new DateOnly(2000, 1, 1), DateOnly.FromDateTime(DateTime.Now).AddDays(1), "homefocus-all.csv");

    [RelayCommand]
    private void DeleteHistory()
    {
        if (!MessageDialog.Show(Loc.T("settings.clearTitle"), Loc.T("settings.clearConfirm"),
                Loc.T("settings.clearAction"), Loc.T("common.cancel"), danger: true))
            return;
        AppServices.Instance.Activities.DeleteAll();
        Refresh();
    }
}

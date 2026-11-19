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
    [ObservableProperty] private string _extensionStatus = "";
    [ObservableProperty] private bool _hasPairedBrowsers;
    public bool HasStoreLink => ExtensionInfo.StoreUrl.Length > 0;
    [ObservableProperty] private bool _showEnableWeb;

    // Automatic categories with a local Ollama model
    [ObservableProperty] private bool _aiEnabled;
    [ObservableProperty] private string _aiPosition = "";
    [ObservableProperty] private string _aiUrl = "";
    [ObservableProperty] private IReadOnlyList<string> _aiModels = [];
    [ObservableProperty] private string? _aiModel;
    [ObservableProperty] private string _aiStatus = "";
    [ObservableProperty] private bool _aiBusy;
    [ObservableProperty] private bool _aiRemoteWarning;

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
        RefreshExtension();
        AiEnabled = s.Settings.GetBool(SettingKeys.AiEnabled, false);
        AiPosition = s.Settings.Get(SettingKeys.AiPosition) ?? "";
        AiUrl = AiService.Url;
        AiModel = s.Settings.Get(SettingKeys.AiModel);
        if (AiModel is { Length: > 0 } && !AiModels.Contains(AiModel)) AiModels = [AiModel];
        AiRemoteWarning = !Core.Ai.OllamaClient.IsLocal(AiUrl);
        AiStatus = s.Ai?.Status ?? "";
        if (s.Ai is { } ai) { ai.StatusChanged -= OnAiStatus; ai.StatusChanged += OnAiStatus; }
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

    public override void OnTimer() => RefreshExtension();

    private void RefreshExtension()
    {
        var s = AppServices.Instance;
        var connected = s.Browser.ConnectedBrowsers();
        HasPairedBrowsers = s.Pairing.Count > 0;
        ShowEnableWeb = HasStoreLink && !HasPairedBrowsers;
        ExtensionStatus = s.Bridge is not { IsListening: true } ? Loc.T("settings.extPortBusy")
            : connected.Count > 0 ? Loc.F("settings.extConnected", string.Join(", ", connected.Select(BrowserName)))
            : HasPairedBrowsers ? Loc.T("settings.extPairedOffline")
            : Loc.T("settings.extNone");
    }

    private static string BrowserName(string process) => process switch
    {
        "msedge.exe" => "Edge", "brave.exe" => "Brave", "opera.exe" => "Opera", "vivaldi.exe" => "Vivaldi", _ => "Chrome",
    };

    [RelayCommand]
    private void GetExtension() => WebTracking.OpenStore();

    [RelayCommand]
    private void DisconnectBrowsers()
    {
        if (!MessageDialog.Show(Loc.T("settings.extDisconnectTitle"), Loc.T("settings.extDisconnectText"),
                Loc.T("settings.extDisconnect"), Loc.T("common.cancel")))
            return;
        AppServices.Instance.Pairing.RevokeAll();
        RefreshExtension();
    }

    private void OnAiStatus(object? sender, EventArgs e) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => AiStatus = AppServices.Instance.Ai?.Status ?? "");

    partial void OnAiEnabledChanged(bool value) { if (!_loading) AppServices.Instance.Settings.SetBool(SettingKeys.AiEnabled, value); }
    partial void OnAiPositionChanged(string value) { if (!_loading) AppServices.Instance.Settings.Set(SettingKeys.AiPosition, value.Trim()); }
    partial void OnAiModelChanged(string? value) { if (!_loading && value is not null) AppServices.Instance.Settings.Set(SettingKeys.AiModel, value); }

    partial void OnAiUrlChanged(string value)
    {
        AiRemoteWarning = !Core.Ai.OllamaClient.IsLocal(value);
        if (!_loading) AppServices.Instance.Settings.Set(SettingKeys.AiUrl, value.Trim());
    }

    /// <summary>Checks the Ollama server and fills the model list.</summary>
    [RelayCommand]
    private async Task AiConnect()
    {
        if (AppServices.Instance.Ai is not { } ai) return;
        AiBusy = true;
        try
        {
            var models = await ai.ListModelsAsync(AiUrl);
            var keep = AiModel;
            AiModels = models;
            AiModel = models.Contains(keep ?? "") ? keep : models.FirstOrDefault();
            AiStatus = models.Count > 0 ? Loc.F("ai.connected", models.Count) : Loc.T("ai.noModels");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or ArgumentException)
        {
            AiStatus = Loc.F("ai.statusError", ex.Message);
        }
        finally
        {
            AiBusy = false;
        }
    }

    /// <summary>Categorizes uncategorized apps and websites right away.</summary>
    [RelayCommand]
    private async Task AiRunNow()
    {
        if (AppServices.Instance.Ai is not { } ai) return;
        if (!AiService.IsConfigured(out _, out _))
        {
            AiStatus = Loc.T("ai.needSetup");
            return;
        }
        AiBusy = true;
        AiStatus = Loc.T("ai.running");
        try { await ai.RunAsync(manual: true); }
        finally { AiBusy = false; }
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

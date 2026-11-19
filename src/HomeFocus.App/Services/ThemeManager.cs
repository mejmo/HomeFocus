using System.Windows;
using Microsoft.Win32;

namespace HomeFocus.App.Services;

/// <summary>Swaps the Light/Dark color dictionary; "system" follows the Windows app theme live.</summary>
public static class ThemeManager
{
    private static string _mode = "system";
    private static bool _hooked;

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(string mode)
    {
        _mode = mode;
        if (!_hooked)
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _mode == "system")
                    Application.Current?.Dispatcher.BeginInvoke(() => Apply("system"));
            };
            _hooked = true;
        }

        var dark = mode switch { "dark" => true, "light" => false, _ => SystemIsDark() };
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute);
        var existing = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Themes/Light.xaml") == true ||
                                                 d.Source?.OriginalString.Contains("/Themes/Dark.xaml") == true);
        if (existing is not null && IsDark == dark && existing.Source == uri) return;
        var fresh = new ResourceDictionary { Source = uri };
        if (existing is null) dicts.Insert(0, fresh);
        else dicts[dicts.IndexOf(existing)] = fresh;
        IsDark = dark;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}

using Microsoft.Win32;

namespace HomeFocus.App.Services;

/// <summary>Autostart through HKCU\...\Run; the app starts minimized to the tray.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "HomeFocus";
    public const string MinimizedArg = "--minimized";

    private static string Command => $"\"{Environment.ProcessPath}\" {MinimizedArg}";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is string;
    }

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue(Name, Command);
        else k.DeleteValue(Name, false);
    }

    /// <summary>Re-points an existing entry at the current exe (the app may have moved).</summary>
    public static void Refresh()
    {
        if (IsEnabled()) Set(true);
    }
}

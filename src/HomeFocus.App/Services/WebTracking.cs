using System.Diagnostics;

namespace HomeFocus.App.Services;

/// <summary>Offering the browser extension to people who haven't connected a browser yet.</summary>
public static class WebTracking
{
    /// <summary>True when no browser has ever been paired, none is connected and the offer wasn't dismissed.</summary>
    public static bool ShouldOffer()
    {
        var s = AppServices.Instance;
        return ExtensionInfo.StoreUrl.Length > 0
               && s.Pairing.Count == 0
               && s.Browser.ConnectedBrowsers().Count == 0
               && !s.Settings.GetBool(SettingKeys.HideWebBanner, false);
    }

    /// <summary>Opens the extension's Chrome Web Store page in the default browser.</summary>
    public static void OpenStore()
    {
        if (ExtensionInfo.StoreUrl.Length == 0) return;
        Process.Start(new ProcessStartInfo(ExtensionInfo.StoreUrl) { UseShellExecute = true });
    }
}

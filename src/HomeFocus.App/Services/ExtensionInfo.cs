namespace HomeFocus.App.Services;

/// <summary>Identity of the HomeFocus browser extension.</summary>
public static class ExtensionInfo
{
    /// <summary>ID of the unpacked build in <c>extension/</c> (fixed by the "key" in its manifest).</summary>
    public const string DevId = "pdfhmbelenlkhfdhpbpnmmjndohlhdeb";

    /// <summary>ID assigned by the Chrome Web Store (unlisted item).</summary>
    public const string StoreId = "hgibjbcmbjphclgadjlagjeioaanochk";

    public static string StoreUrl => StoreId.Length > 0 ? $"https://chromewebstore.google.com/detail/{StoreId}" : "";
}

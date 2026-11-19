using System.Windows.Media;
using HomeFocus.Core;
using HomeFocus.Core.Browser;
using HomeFocus.Core.Data;
using HomeFocus.Core.Reports;
using HomeFocus.Core.Tracking;

namespace HomeFocus.App.Services;

/// <summary>Category as the UI shows it.</summary>
/// <param name="TextBrush">The color for text (chips, initials): the color itself on dark, a darker shade on light so it stays readable.</param>
public sealed record CategoryInfo(long Id, string Name, string Hex, SolidColorBrush Brush, SolidColorBrush Tint, bool IsBuiltIn, SolidColorBrush TextBrush)
{
    public override string ToString() => Name;
}

/// <summary>Composition root: the database, stores, tracker and shared lookups.</summary>
public sealed class AppServices : IDisposable
{
    public static AppServices Instance { get; private set; } = null!;

    private Categorizer? _categorizer;
    private List<CategoryInfo> _categories = [];

    public AppServices(string dbPath)
    {
        Db = new Database(dbPath);
        Db.Initialize();
        Activities = new ActivityStore(Db);
        Categories = new CategoryStore(Db);
        Rules = new RuleStore(Db);
        Settings = new SettingsStore(Db);
        DeviceId = Settings.DeviceId();

        ReloadLookups();
        ThemeManager.Changed += (_, _) => ReloadLookups(); // text shades depend on light/dark
        Browser = new BrowserState();
        Pairing = new PairingTokens(Settings);
        var tracker = new ActivityTracker(new Win32ForegroundSource(), Activities, DeviceId, Browser, host => Categorizer.SiteFor(host))
        {
            IdleThreshold = TimeSpan.FromMinutes(Settings.GetInt(SettingKeys.IdleMinutes, 3)),
        };
        Tracking = new TrackingService(tracker);
        Reports = new ReportService(Activities, () => Categorizer, () => Tracking.Current);
        Instance = this;
    }

    public Database Db { get; }
    public ActivityStore Activities { get; }
    public CategoryStore Categories { get; }
    public RuleStore Rules { get; }
    public SettingsStore Settings { get; }
    public TrackingService Tracking { get; }
    public ReportService Reports { get; }
    public string DeviceId { get; }
    public BrowserState Browser { get; }
    public PairingTokens Pairing { get; }
    public BrowserBridge? Bridge { get; private set; }
    public AiService? Ai { get; private set; }

    public void StartAi() => Ai = new AiService(this);

    /// <summary>Extension IDs allowed to connect: the unpacked development build and the Chrome Web Store item.</summary>
    public static readonly string[] ExtensionIds = [ExtensionInfo.DevId, ExtensionInfo.StoreId];

    /// <summary>Starts the local endpoint for the browser extension; <paramref name="approve"/> asks the user about new browsers.</summary>
    public void StartBridge(Func<string, Task<bool>> approve)
    {
        Bridge = new BrowserBridge(Browser, Pairing, ExtensionIds, approve);
        Bridge.Start();
    }

    public Categorizer Categorizer => _categorizer!;
    public IReadOnlyList<CategoryInfo> CategoryList => _categories;
    public long FallbackCategoryId { get; private set; }

    /// <summary>Raised (on the UI thread) after categories or rules change.</summary>
    public event EventHandler? LookupsChanged;

    public CategoryInfo Category(long id) =>
        _categories.FirstOrDefault(c => c.Id == id) ?? _categories.First(c => c.Id == FallbackCategoryId);

    public void ReloadLookups()
    {
        var cats = Categories.GetAll();
        // The fallback is the built-in category sorted last ("Other").
        FallbackCategoryId = cats.Where(c => c.IsBuiltIn).OrderBy(c => c.SortOrder).Last().Id;
        _categories = cats.Select(c => new CategoryInfo(c.Id, DisplayName(c), c.Color, Brush(c.Color, 1),
            Brush(c.Color, ThemeManager.IsDark ? 0.16 : 0.22), c.IsBuiltIn, TextBrush(c.Color))).ToList();
        _categorizer = new Categorizer(Rules.GetAll(), FallbackCategoryId);
        LookupsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Built-in categories are shown in the UI language; user categories keep their own name.</summary>
    private static string DisplayName(Category c) =>
        c.Key is { } k ? HomeFocus.App.Localization.Loc.T("cat." + k) : c.Name;

    private static SolidColorBrush TextBrush(string hex)
    {
        if (ThemeManager.IsDark) return Brush(hex, 1);
        var c = (Color)ColorConverter.ConvertFromString(hex);
        const double k = 0.55; // keep 55 % of the color: pastel → readable on white
        var b = new SolidColorBrush(Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)));
        b.Freeze();
        return b;
    }

    private static SolidColorBrush Brush(string hex, double alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public void Dispose()
    {
        Bridge?.Dispose();
        Ai?.Dispose();
        Tracking.Dispose();
    }
}

public static class SettingKeys
{
    public const string IdleMinutes = "idle_minutes";
    public const string Autostart = "autostart";
    public const string CloseToTray = "close_to_tray";
    public const string Theme = "theme"; // system | light | dark
    public const string FirstRunDone = "first_run_done";
    public const string HideWebBanner = "hide_web_banner";
    public const string AiEnabled = "ai_enabled";
    public const string AiPosition = "ai_position";
    public const string AiUrl = "ai_url";
    public const string AiModel = "ai_model";
}

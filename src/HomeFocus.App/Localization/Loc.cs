using System.Globalization;
using System.Windows.Markup;

namespace HomeFocus.App.Localization;

/// <summary>
/// UI text. English is the default; Slovak is used when Windows' display language is Slovak.
/// HOMEFOCUS_LANG=en|sk overrides the detection (handy for testing).
/// </summary>
public static class Loc
{
    public static bool IsSlovak { get; private set; }

    /// <summary>Culture for dates and numbers, matching the UI language.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public static void Init()
    {
        var forced = Environment.GetEnvironmentVariable("HOMEFOCUS_LANG");
        IsSlovak = forced is not null
            ? forced.StartsWith("sk", StringComparison.OrdinalIgnoreCase)
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "sk";
        Culture = IsSlovak
            ? CultureInfo.GetCultureInfo("sk-SK")
            : CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "en" ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }

    public static string T(string key) =>
        Strings.All.TryGetValue(key, out var s) ? (IsSlovak ? s.Sk : s.En) : key;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);
}

/// <summary>XAML: <c>Text="{l:T today.title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}

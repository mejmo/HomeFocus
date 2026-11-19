using System.Net.Http;
using System.Windows;
using HomeFocus.App.Localization;
using HomeFocus.Core.Ai;

namespace HomeFocus.App.Services;

/// <summary>Runs the Ollama-based categorizer in the background every few minutes while it is enabled.</summary>
public sealed class AiService : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly AutoCategorizer _auto;
    private readonly Timer _timer;
    private readonly CancellationTokenSource _cts = new();

    public AiService(AppServices s)
    {
        _auto = new AutoCategorizer(s.Reports, s.Rules);
        _timer = new Timer(_ => _ = RunAsync(), null, TimeSpan.FromSeconds(20), Every);
    }

    /// <summary>Last result for the settings page (UI language).</summary>
    public string Status { get; private set; } = "";

    public event EventHandler? StatusChanged;

    public static string Url => AppServices.Instance.Settings.Get(SettingKeys.AiUrl) is { Length: > 0 } u ? u : OllamaClient.DefaultUrl;

    public static bool IsConfigured(out string position, out string model)
    {
        var st = AppServices.Instance.Settings;
        position = st.Get(SettingKeys.AiPosition) ?? "";
        model = st.Get(SettingKeys.AiModel) ?? "";
        return st.GetBool(SettingKeys.AiEnabled, false) && position.Trim().Length > 0 && model.Length > 0;
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(string url) =>
        await new OllamaClient(_http, url, "").ListModelsAsync(_cts.Token);

    /// <summary>Categorizes uncategorized items (all of them when <paramref name="manual"/>); does nothing when the feature is off.</summary>
    public async Task<int> RunAsync(bool manual = false)
    {
        if (!IsConfigured(out var position, out var model)) return 0;
        var s = AppServices.Instance;
        var cats = s.CategoryList.Select(c => (c.Id, c.Name)).ToList();
        try
        {
            var n = await _auto.RunAsync(new OllamaClient(_http, Url, model), position, cats, manual ? 50 : 10, _cts.Token, manual);
            SetStatus(n > 0 ? Loc.F("ai.statusDone", n, DateTime.Now) : Loc.F("ai.statusNothing", DateTime.Now));
            if (n > 0) await Application.Current.Dispatcher.InvokeAsync(s.ReloadLookups);
            return n;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            SetStatus(Loc.F("ai.statusError", ex.Message));
            return 0;
        }
    }

    private void SetStatus(string text)
    {
        Status = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _http.Dispose();
    }
}

using HomeFocus.Core.Data;
using HomeFocus.Core.Reports;

namespace HomeFocus.Core.Ai;

/// <summary>
/// Gives uncategorized apps and websites a category using the classifier, and saves each answer as an "AI" rule.
/// Your own rules always win over AI rules, and an AI rule can be changed like any other.
/// </summary>
public sealed class AutoCategorizer(ReportService reports, RuleStore rules)
{
    /// <summary>Items with less time than this in the last 7 days are left alone.</summary>
    public static readonly TimeSpan MinTime = TimeSpan.FromMinutes(1);

    private readonly HashSet<string> _gaveUp = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>
    /// Categorizes up to <paramref name="max"/> items. Returns how many got a rule.
    /// <paramref name="categories"/> are (id, display name) pairs offered to the model.
    /// </summary>
    public async Task<int> RunAsync(IClassifier classifier, string position, IReadOnlyList<(long Id, string Name)> categories,
        int max, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(position) || categories.Count == 0) return 0;
        if (!await _running.WaitAsync(0, ct)) return 0; // a run is already in progress
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var todo = reports.GetUncategorized(today.AddDays(-6), today.AddDays(1))
                .Where(a => a.Duration >= MinTime && !_gaveUp.Contains(Id(a)))
                .Take(max)
                .ToList();
            var names = categories.Select(c => c.Name).ToList();
            var done = 0;
            foreach (var item in todo)
            {
                ct.ThrowIfCancellationRequested();
                string? answer;
                try
                {
                    answer = await classifier.ClassifyAsync(position, names, item.Kind, item.AppKey, item.DisplayName, ct);
                }
                catch (HttpRequestException)
                {
                    throw; // server unreachable: stop the run, try again later
                }
                if (answer is null)
                {
                    _gaveUp.Add(Id(item));
                    continue;
                }
                var cat = categories.First(c => c.Name == answer);
                rules.SetAi(item.AppKey, cat.Id, item.Kind);
                done++;
            }
            return done;
        }
        finally
        {
            _running.Release();
        }
    }

    private static string Id(AppTotal a) => $"{a.Kind}:{a.AppKey}";
}

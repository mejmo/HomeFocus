using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeFocus.App.Localization;
using HomeFocus.App.Services;
using HomeFocus.App.Views;
using HomeFocus.Core;

namespace HomeFocus.App.ViewModels;

public sealed partial class CategoryFilterRow(RulesViewModel owner, CategoryInfo? category, string name, Brush brush, int count) : ObservableObject
{
    public CategoryInfo? Category { get; } = category;
    public string Name { get; } = name;
    public Brush Brush { get; } = brush;
    public int Count { get; } = count;
    public bool CanDelete => Category is { IsBuiltIn: false };

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) { if (value) owner.Select(this); }

    [RelayCommand] private void Delete() => owner.DeleteCategory(this);
}

public sealed partial class RuleRow(RulesViewModel owner, long id, string pattern, CategoryInfo category, bool isUser, ActivityKind kind,
    string usage = "", bool isAi = false) : ObservableObject
{
    /// <summary>Time spent in the last 30 days in apps/websites this rule matches ("" when none).</summary>
    public string Usage { get; } = usage;
    public long Id { get; } = id;
    public string Pattern { get; } = pattern;
    public ActivityKind Kind { get; } = kind;
    public string KindText => Loc.T(Kind == ActivityKind.Web ? "rules.kindWeb" : "rules.kindApp");
    public string Source => Loc.T(isUser ? "rules.user" : isAi ? "rules.ai" : "rules.default");
    public IReadOnlyList<CategoryInfo> Choices => AppServices.Instance.CategoryList;

    [ObservableProperty] private CategoryInfo _category = category;

    partial void OnCategoryChanged(CategoryInfo value) => owner.SetRule(Pattern, value, Kind);

    [RelayCommand] private void Delete() => owner.DeleteRule(this);
}

public sealed partial class UncategorizedRow(RulesViewModel owner, string appKey, string name, string time, ActivityKind kind) : ObservableObject
{
    public string AppKey { get; } = appKey;
    public ActivityKind Kind { get; } = kind;
    public string Name { get; } = name;
    public string Time { get; } = time;
    public IReadOnlyList<CategoryInfo> Choices => AppServices.Instance.CategoryList;

    [ObservableProperty] private CategoryInfo? _category;

    partial void OnCategoryChanged(CategoryInfo? value) { if (value is not null) owner.SetRule(AppKey, value, Kind); }
}

public sealed partial class RulesViewModel : PageViewModel
{
    public static readonly string[] Palette =
        ["#3B82F6", "#14B8A6", "#F97316", "#EC4899", "#8B5CF6", "#22C55E", "#EAB308", "#EF4444", "#06B6D4", "#94A3B8"];

    [ObservableProperty] private IReadOnlyList<CategoryFilterRow> _categoryRows = [];
    [ObservableProperty] private IReadOnlyList<RuleRow> _rules = [];
    [ObservableProperty] private IReadOnlyList<UncategorizedRow> _uncategorized = [];
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _rulesTitle = "";
    [ObservableProperty] private string _newPattern = "";
    [ObservableProperty] private CategoryInfo? _newPatternCategory;
    [ObservableProperty] private string _newCategoryName = "";
    [ObservableProperty] private string _newCategoryColor = Palette[5];
    [ObservableProperty] private bool _isAddingCategory;
    [ObservableProperty] private bool _showAll;
    [ObservableProperty] private string _showAllText = "";
    [ObservableProperty] private bool _canShowMore;

    /// <summary>Rules shown before "Show all": the most used ones.</summary>
    private const int TopCount = 5;

    private long? _selected;
    private bool _suppress;
    private Dictionary<long, TimeSpan> _usage = new();

    public IReadOnlyList<CategoryInfo> Choices => AppServices.Instance.CategoryList;
    public IReadOnlyList<string> Colors => Palette;

    partial void OnSearchChanged(string value) => LoadRules();

    private List<Rule> _allRules = [];

    public override void Refresh()
    {
        _suppress = true;
        var s = AppServices.Instance;
        var rules = _allRules = s.Rules.GetAll();
        if (_selected is { } sel && s.CategoryList.All(c => c.Id != sel)) _selected = null;
        _usage = Usage(rules);
        var rows = new List<CategoryFilterRow>
        {
            new(this, null, Loc.T("rules.all"), (Brush)System.Windows.Application.Current.FindResource("Brush.Text"), rules.Count),
        };
        rows.AddRange(s.CategoryList.Select(c => new CategoryFilterRow(this, c, c.Name, c.Brush, rules.Count(r => r.CategoryId == c.Id))));
        foreach (var r in rows) r.IsSelected = r.Category?.Id == _selected;
        CategoryRows = rows;
        OnPropertyChanged(nameof(Choices));
        NewPatternCategory ??= s.CategoryList.FirstOrDefault();

        var today = DateOnly.FromDateTime(DateTime.Now);
        Uncategorized = s.Reports.GetUncategorized(today.AddDays(-6), today.AddDays(1))
            .Take(8).Select(a => new UncategorizedRow(this, a.AppKey, a.DisplayName, Loc.F("rules.last7", Format.Duration(a.Duration)), a.Kind)).ToList();
        _suppress = false;
        LoadRules();
    }

    private void LoadRules()
    {
        _suppress = true;
        var s = AppServices.Instance;
        var q = Search.Trim();
        var matching = _allRules
            .Where(r => _selected is null || r.CategoryId == _selected)
            .Where(r => q.Length == 0 || r.Pattern.Contains(q, StringComparison.OrdinalIgnoreCase))
            // most used first, then your own rules, then alphabetical
            .OrderByDescending(r => _usage.GetValueOrDefault(r.Id))
            .ThenByDescending(r => r.IsUser)
            .ThenBy(r => r.Pattern, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // A search shows every match; otherwise only the top few until "Show all".
        var limit = ShowAll || q.Length > 0 ? matching.Count : TopCount;
        Rules = matching.Take(limit)
            .Select(r => new RuleRow(this, r.Id, r.Pattern, s.Category(r.CategoryId), r.IsUser, r.Kind,
                _usage.TryGetValue(r.Id, out var u) && u >= TimeSpan.FromMinutes(1) ? Loc.F("rules.usage", Format.Duration(u)) : "", r.IsAi))
            .ToList();
        CanShowMore = q.Length == 0 && matching.Count > TopCount;
        ShowAllText = ShowAll ? Loc.T("rules.showFewer") : Loc.F("rules.showAll", matching.Count);
        RulesTitle = Loc.F("rules.rulesFor", _selected is { } id ? s.Category(id).Name : Loc.T("rules.all"));
        _suppress = false;
    }

    [RelayCommand]
    private void ToggleShowAll()
    {
        ShowAll = !ShowAll;
        LoadRules();
    }

    /// <summary>Time per rule over the last 30 days: apps/websites whose key the rule's pattern matches.</summary>
    private static Dictionary<long, TimeSpan> Usage(List<Rule> rules)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var apps = AppServices.Instance.Reports.GetRange(today.AddDays(-29), today.AddDays(1)).Apps;
        var result = new Dictionary<long, TimeSpan>();
        foreach (var r in rules)
        {
            var total = TimeSpan.Zero;
            foreach (var a in apps)
            {
                if (a.Kind != r.Kind && !(a.Kind == ActivityKind.App && r.Kind == ActivityKind.App)) continue;
                var hit = r.Kind == ActivityKind.Web && !r.Pattern.Contains('*')
                    ? Categorizer.DomainMatches(r.Pattern, a.AppKey)
                    : Categorizer.Matches(r.Pattern, a.AppKey);
                if (hit) total += a.Duration;
            }
            if (total > TimeSpan.Zero) result[r.Id] = total;
        }
        return result;
    }

    internal void Select(CategoryFilterRow row)
    {
        if (_suppress) return;
        ShowAll = false;
        _selected = row.Category?.Id;
        foreach (var r in CategoryRows) if (r != row) r.IsSelected = false;
        LoadRules();
    }

    internal void SetRule(string pattern, CategoryInfo category, ActivityKind kind)
    {
        if (_suppress) return;
        AppServices.Instance.Rules.Set(pattern, category.Id, kind);
        // Rebuild after the combo box that triggered this has closed, so the click feels instant.
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            AppServices.Instance.ReloadLookups();
            Refresh();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    internal void DeleteRule(RuleRow row)
    {
        AppServices.Instance.Rules.Delete(row.Id);
        AppServices.Instance.ReloadLookups();
        Refresh();
    }

    internal void DeleteCategory(CategoryFilterRow row)
    {
        if (row.Category is not { IsBuiltIn: false } c) return;
        var s = AppServices.Instance;
        if (!MessageDialog.Show(Loc.F("rules.deleteTitle", c.Name),
                Loc.F("rules.deleteText", row.Count, s.Category(s.FallbackCategoryId).Name), Loc.T("common.delete"), Loc.T("common.cancel"), danger: true))
            return;
        s.Categories.Delete(c.Id, s.FallbackCategoryId);
        if (_selected == c.Id) _selected = null;
        s.ReloadLookups();
        Refresh();
    }

    [RelayCommand]
    private void AddRule()
    {
        var p = NewPattern.Trim();
        if (p.Length == 0 || NewPatternCategory is null) return;
        // "figma" or "steam*.exe" is an app; anything else with a dot ("youtube.com") is a website.
        if (!p.Contains('.') && !p.EndsWith('*')) p += ".exe";
        var kind = p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ActivityKind.App : ActivityKind.Web;
        if (kind == ActivityKind.Web)
        {
            p = p.Replace("https://", "").Replace("http://", "").Split('/')[0];
            if (p.StartsWith("www.")) p = p[4..];
        }
        AppServices.Instance.Rules.Set(p, NewPatternCategory.Id, kind);
        NewPattern = "";
        AppServices.Instance.ReloadLookups();
        Refresh();
    }

    [RelayCommand] private void StartAddCategory() => IsAddingCategory = true;
    [RelayCommand] private void CancelAddCategory() { IsAddingCategory = false; NewCategoryName = ""; }
    [RelayCommand] private void PickColor(string color) => NewCategoryColor = color;

    [RelayCommand]
    private void AddCategory()
    {
        var n = NewCategoryName.Trim();
        if (n.Length == 0) return;
        AppServices.Instance.Categories.Add(n, NewCategoryColor);
        NewCategoryName = "";
        IsAddingCategory = false;
        AppServices.Instance.ReloadLookups();
        Refresh();
    }
}

using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeFocus.App.Localization;
using HomeFocus.App.Services;
using HomeFocus.App.Views;

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

public sealed partial class RuleRow(RulesViewModel owner, long id, string pattern, CategoryInfo category, bool isUser) : ObservableObject
{
    public long Id { get; } = id;
    public string Pattern { get; } = pattern;
    public string Source => Loc.T(isUser ? "rules.user" : "rules.default");
    public IReadOnlyList<CategoryInfo> Choices => AppServices.Instance.CategoryList;

    [ObservableProperty] private CategoryInfo _category = category;

    partial void OnCategoryChanged(CategoryInfo value) => owner.SetRule(Pattern, value);

    [RelayCommand] private void Delete() => owner.DeleteRule(this);
}

public sealed partial class UncategorizedRow(RulesViewModel owner, string appKey, string name, string time) : ObservableObject
{
    public string AppKey { get; } = appKey;
    public string Name { get; } = name;
    public string Time { get; } = time;
    public IReadOnlyList<CategoryInfo> Choices => AppServices.Instance.CategoryList;

    [ObservableProperty] private CategoryInfo? _category;

    partial void OnCategoryChanged(CategoryInfo? value) { if (value is not null) owner.SetRule(AppKey, value); }
}

public sealed partial class RulesViewModel : PageViewModel
{
    public static readonly string[] Palette =
        ["#2F6FEB", "#0E8F80", "#E07A2E", "#C9437A", "#7452D1", "#3C9A3C", "#C2A21A", "#D9534F", "#1F8FB8", "#8A919E"];

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

    private long? _selected;
    private bool _suppress;

    public IReadOnlyList<CategoryInfo> Choices => AppServices.Instance.CategoryList;
    public IReadOnlyList<string> Colors => Palette;

    partial void OnSearchChanged(string value) => LoadRules();

    public override void Refresh()
    {
        _suppress = true;
        var s = AppServices.Instance;
        var rules = s.Rules.GetAll();
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
            .Take(8).Select(a => new UncategorizedRow(this, a.AppKey, a.DisplayName, Loc.F("rules.last7", Format.Duration(a.Duration)))).ToList();
        _suppress = false;
        LoadRules();
    }

    private void LoadRules()
    {
        _suppress = true;
        var s = AppServices.Instance;
        var q = Search.Trim();
        Rules = s.Rules.GetAll()
            .Where(r => _selected is null || r.CategoryId == _selected)
            .Where(r => q.Length == 0 || r.Pattern.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(r => new RuleRow(this, r.Id, r.Pattern, s.Category(r.CategoryId), r.IsUser)).ToList();
        RulesTitle = Loc.F("rules.rulesFor", _selected is { } id ? s.Category(id).Name : Loc.T("rules.all"));
        _suppress = false;
    }

    internal void Select(CategoryFilterRow row)
    {
        if (_suppress) return;
        _selected = row.Category?.Id;
        foreach (var r in CategoryRows) if (r != row) r.IsSelected = false;
        LoadRules();
    }

    internal void SetRule(string pattern, CategoryInfo category)
    {
        if (_suppress) return;
        AppServices.Instance.Rules.Set(pattern, category.Id);
        AppServices.Instance.ReloadLookups();
        Refresh();
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
        if (!p.Contains('.') && !p.EndsWith('*')) p += ".exe";
        AppServices.Instance.Rules.Set(p, NewPatternCategory.Id);
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

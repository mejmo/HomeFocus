using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HomeFocus.App.Services;
using HomeFocus.App.ViewModels;

namespace HomeFocus.App.Views;

public partial class RulesView : UserControl
{
    public RulesView() => InitializeComponent();

    /// <summary>
    /// One menu, built on demand, instead of a combo box in each of the ~100 rule rows
    /// (a hundred combo boxes made the page slow to open).
    /// </summary>
    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RuleRow row } button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, MinWidth = button.ActualWidth };
        var template = (DataTemplate)FindResource("CategoryChoice");
        foreach (var category in AppServices.Instance.CategoryList)
        {
            var item = new MenuItem
            {
                Header = category,
                HeaderTemplate = template,
                FontWeight = category.Id == row.Category.Id ? FontWeights.SemiBold : FontWeights.Normal,
            };
            item.Click += (_, _) => row.Category = category;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}

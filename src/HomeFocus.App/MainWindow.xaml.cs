using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using HomeFocus.App.Services;
using HomeFocus.App.ViewModels;

namespace HomeFocus.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        StateChanged += (_, _) => OnStateChanged();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) vm.Activate(); };
    }

    /// <summary>Set when the app really exits, so Closing doesn't just hide the window.</summary>
    public bool AllowClose { get; set; }

    private void OnStateChanged()
    {
        // A maximized chrome-less window overhangs the screen by the resize border.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxIcon.Data = (System.Windows.Media.Geometry)FindResource(WindowState == WindowState.Maximized ? "Icon.Restore" : "Icon.Maximize");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose && AppServices.Instance.Settings.GetBool(SettingKeys.CloseToTray, true))
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!AllowClose) Application.Current.Shutdown();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = PauseButton.ContextMenu!;
        menu.PlacementTarget = PauseButton;
        menu.Placement = PlacementMode.Top;
        menu.DataContext = DataContext;
        menu.IsOpen = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}

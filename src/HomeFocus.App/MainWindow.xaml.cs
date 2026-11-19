using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
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
        // Every page opens scrolled to the top.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Current) or nameof(MainViewModel.CurrentPage)) Scroller.ScrollToTop();
        };
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) vm.Activate(); };
        SourceInitialized += (_, _) => ApplyFrame();
        ThemeManager.Changed += (_, _) => ApplyFrame();
    }

    private static readonly bool IsWindows11 = Environment.OSVersion.Version.Build >= 22000;

    /// <summary>
    /// Gives the chrome-less window a visible edge: on Windows 11 the system border (theme color, rounded corners),
    /// on Windows 10 a 1 px WPF border. A maximized window has no border.
    /// </summary>
    private void ApplyFrame()
    {
        var maximized = WindowState == WindowState.Maximized;
        if (IsWindows11)
        {
            Frame.BorderThickness = new Thickness(0);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var corner = maximized ? 1 /* DWMWCP_DONOTROUND */ : 2 /* DWMWCP_ROUND */;
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corner, sizeof(int));
            var c = (TryFindResource("Brush.WindowBorder") as SolidColorBrush)?.Color ?? Colors.Gray;
            var colorRef = c.R | (c.G << 8) | (c.B << 16);
            DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref colorRef, sizeof(int));
        }
        else
        {
            Frame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Set when the app really exits, so Closing doesn't just hide the window.</summary>
    public bool AllowClose { get; set; }

    private void OnStateChanged()
    {
        // A maximized chrome-less window overhangs the screen by the resize border.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxIcon.Data = (Geometry)FindResource(WindowState == WindowState.Maximized ? "Icon.Restore" : "Icon.Maximize");
        ApplyFrame();
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

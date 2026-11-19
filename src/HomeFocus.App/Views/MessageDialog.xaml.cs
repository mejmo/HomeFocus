using System.Windows;
using System.Windows.Input;

namespace HomeFocus.App.Views;

/// <summary>Themed replacement for MessageBox.</summary>
public partial class MessageDialog : Window
{
    private MessageDialog() => InitializeComponent();

    /// <summary>Shows the dialog; returns true when the primary button was chosen.</summary>
    public static bool Show(string title, string message, string? primary, string secondary, bool danger = false, bool topmost = false)
    {
        var d = new MessageDialog();
        d.TitleText.Text = title;
        d.MessageText.Text = message;
        d.SecondaryButton.Content = secondary;
        if (primary is null) d.PrimaryButton.Visibility = Visibility.Collapsed;
        else d.PrimaryButton.Content = primary;
        if (danger) d.PrimaryButton.Style = (Style)d.FindResource("Button.DangerSolid");
        d.Topmost = topmost;
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != d)
                    ?? Application.Current.MainWindow;
        if (owner is { IsVisible: true }) d.Owner = owner;
        else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        d.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) d.DragMove(); };
        return d.ShowDialog() == true;
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

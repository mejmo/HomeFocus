using System.Windows;
using System.Windows.Media;
using HomeFocus.App.Localization;
using HomeFocus.App.Views;
using Forms = System.Windows.Forms;

namespace HomeFocus.App.Services;

/// <summary>Notification-area icon; both mouse buttons open the themed <see cref="TrayPopup"/>.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly TrayPopup _popup;
    private readonly System.Windows.Threading.DispatcherTimer _tooltip = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateTime _lastHide;

    public TrayIcon(Action open, Action quit)
    {
        _popup = new TrayPopup(open, quit);
        _popup.IsVisibleChanged += (_, e) => { if (!(bool)e.NewValue) _lastHide = DateTime.UtcNow; };

        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!;
        using var stream = res.Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "HomeFocus",
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button is not (Forms.MouseButtons.Left or Forms.MouseButtons.Right)) return;
            // A click on the icon first deactivates (hides) an open popup; don't reopen it right away.
            if (DateTime.UtcNow - _lastHide < TimeSpan.FromMilliseconds(300)) return;
            ShowPopup();
        };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) { _popup.Hide(); open(); } };
        _tooltip.Tick += (_, _) => UpdateTooltip();
        _tooltip.Start();
        UpdateTooltip();
    }

    private void ShowPopup()
    {
        var p = Forms.Cursor.Position;
        var dpi = VisualTreeHelper.GetDpi(_popup);
        _popup.ShowNear(new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY));
    }

    private void UpdateTooltip()
    {
        try
        {
            var d = AppServices.Instance.Reports.GetDay(DateOnly.FromDateTime(DateTime.Now));
            var t = Loc.F("tray.tooltip", Format.Duration(d.Active));
            if (AppServices.Instance.Tracking.IsPaused) t += Loc.T("tray.tooltipPaused");
            _icon.Text = t.Length > 63 ? t[..63] : t;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    public void Dispose()
    {
        _tooltip.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _popup.Close();
    }
}

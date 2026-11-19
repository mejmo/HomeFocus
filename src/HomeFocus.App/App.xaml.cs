using System.Windows;
using System.Windows.Threading;
using HomeFocus.App.Services;
using HomeFocus.App.ViewModels;
using HomeFocus.App.Views;
using HomeFocus.Core.Data;

namespace HomeFocus.App;

public partial class App : Application
{
    private const string MutexName = @"Local\HomeFocus.SingleInstance";
    private const string ShowEventName = @"Local\HomeFocus.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppServices? _services;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            // Already running: ask that instance to show its window.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        try
        {
            _services = new AppServices(Environment.GetEnvironmentVariable("HOMEFOCUS_DB") ?? Database.DefaultPath);
        }
        catch (Exception ex)
        {
            MessageDialog.Show("HomeFocus sa nepodarilo spustiť", $"Databázu sa nepodarilo otvoriť: {ex.Message}", null, "Zavrieť");
            Shutdown(1);
            return;
        }

        ThemeManager.Apply(_services.Settings.Get(SettingKeys.Theme) ?? "system");
        FirstRun();
        _services.Tracking.Start();

        var vm = new MainViewModel();
        _window = new MainWindow(vm);
        MainWindow = _window;
        vm.Start();
        _tray = new TrayIcon(ShowMain, Quit);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var waiter = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(ShowMain);
        }) { IsBackground = true, Name = "HomeFocus show listener" };
        waiter.Start();

        if (!e.Args.Contains(AutoStart.MinimizedArg)) ShowMain();
    }

    private void FirstRun()
    {
        var s = _services!.Settings;
        if (s.GetBool(SettingKeys.FirstRunDone, false))
        {
            AutoStart.Refresh();
            return;
        }
#if !DEBUG
        // Autostart is on by default for installed builds; debug builds never touch the Run key.
        AutoStart.Set(true);
#endif
        s.SetBool(SettingKeys.FirstRunDone, true);
    }

    private void ShowMain() => _window?.ShowAndActivate();

    private void Quit()
    {
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _services?.Dispose(); // closes the open activity segment
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(e.Exception);
        e.Handled = true;
        MessageDialog.Show("Niečo sa pokazilo", e.Exception.Message, null, "Zavrieť");
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HomeFocus.Core.Tracking;

/// <summary>Reads the foreground window's process and the idle time through the Win32 API.</summary>
public sealed partial class Win32ForegroundSource : IForegroundSource
{
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    // Shell surfaces that mean "nothing in particular is in front".
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "lockapp.exe", "logonui.exe", "searchhost.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe",
    };

    public ForegroundApp? GetForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        string? path = null;
        string name;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
            path = QueryPath(pid);
        }
        catch (ArgumentException)
        {
            return null; // exited meanwhile
        }

        var key = (path is null ? name + ".exe" : Path.GetFileName(path)).ToLowerInvariant();
        if (Ignored.Contains(key)) return null;
        if (key == "explorer.exe" && IsDesktop(hwnd)) return null;
        return new ForegroundApp(key, DisplayName(key, path, name));
    }

    public TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var ms = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(ms);
    }

    private string DisplayName(string key, string? path, string fallback)
    {
        if (_names.TryGetValue(key, out var n)) return n;
        n = fallback;
        if (path is not null)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) n = vi.FileDescription.Trim();
                else if (!string.IsNullOrWhiteSpace(vi.ProductName)) n = vi.ProductName.Trim();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (key == "explorer.exe") n = "Prieskumník";
        return _names[key] = n;
    }

    private static string? QueryPath(uint pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            unsafe
            {
                var buf = stackalloc char[1024];
                uint size = 1024;
                return QueryFullProcessImageName(h, 0, buf, ref size) ? new string(buf, 0, (int)size) : null;
            }
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private static bool IsDesktop(IntPtr hwnd)
    {
        unsafe
        {
            var buf = stackalloc char[64];
            var len = GetClassName(hwnd, buf, 64);
            var cls = new string(buf, 0, Math.Max(0, len));
            return cls is "Progman" or "WorkerW" or "Shell_TrayWnd";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(IntPtr hWnd, char* buf, int max);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(IntPtr h, uint flags, char* buf, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr h);
}

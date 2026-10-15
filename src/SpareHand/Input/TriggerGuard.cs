using System.Diagnostics;
using System.Runtime.InteropServices;
using static SpareHand.Native.Win32;

namespace SpareHand.Input;

/// <summary>
/// Decides whether the trigger may fire right now. Notch-app users' top "gets in the way" complaints were UI over
/// fullscreen apps and games (boring.notch #119, 14 reactions; docs/research.md), so Spare Hand stays out of those, plus any
/// app the user excludes.
/// </summary>
internal static class TriggerGuard
{
    private const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    /// <summary>Returns a reason to stay quiet, or null when the trigger may fire.</summary>
    public static string? WhyNot(Settings settings)
    {
        var fg = GetForegroundWindow();
        if (settings.ExcludedApps.Count > 0 && ProcessName(fg) is { } name &&
            settings.ExcludedApps.Any(e => string.Equals(Path.GetFileNameWithoutExtension(e), name, StringComparison.OrdinalIgnoreCase)))
        {
            return $"excluded app {name}";
        }

        if (!settings.PauseInFullscreen)
        {
            return null;
        }

        // Windows' own signal: D3D fullscreen (games), "busy" fullscreen apps, presentation mode.
        if (SHQueryUserNotificationState(out var state) == 0 && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
        {
            return $"fullscreen (notification state {state})";
        }

        // Borderless fullscreen windows (video players, browsers in F11) don't always set that state: compare rects.
        // Maximized windows are excluded: Windows draws them a few pixels past the monitor edge (Terminal and Chrome
        // sit at -9,-9 on a 1920x1080 screen), which made the first version treat every maximized window as fullscreen.
        if (fg != 0 && !IsZoomed(fg) && ClassOf(fg) is not ("Progman" or "WorkerW" or "Shell_TrayWnd") && GetWindowRect(fg, out var r))
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            var mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            if (GetMonitorInfo(mon, ref mi) && r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom)
            {
                return "borderless fullscreen window";
            }
        }

        return null;
    }

    private static string? ProcessName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
}

using SpareHand.Tray;
using WinUIEx;
using static SpareHand.Native.Win32;

namespace SpareHand.Shell;

/// <summary>
/// A 1×1 transparent, activated tool window at the cursor, used as the owner of UI that appears after a drop (the
/// Share sheet, the folder picker). The tray is cloaked by then, and the Share sheet shows nothing for a hidden owner.
/// </summary>
internal static class DropHost
{
    private static WindowEx? _host;
    private static nint _hwnd;

    public static nint Show()
    {
        if (_host is null)
        {
            _host = new WindowEx { Title = "Spare Hand" };
            _hwnd = PopupChrome.Apply(_host, noActivate: false);
        }

        GetCursorPos(out var at);
        SetWindowPos(_hwnd, HWND_TOPMOST, at.X, at.Y, 1, 1, SWP_SHOWWINDOW);
        _host.Activate();
        SetForegroundWindow(_hwnd);
        return _hwnd;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);
}

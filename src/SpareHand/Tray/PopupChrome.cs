using Microsoft.UI.Windowing;
using WinUIEx;
using static SpareHand.Native.Win32;

namespace SpareHand.Tray;

/// <summary>PowerToys TransparentWindow chrome: no frame, no DWM border or rounding, tool window, always on top.</summary>
internal static class PopupChrome
{
    public static nint Apply(WindowEx window, bool noActivate)
    {
        var hwnd = window.GetWindowHandle();
        window.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        window.IsShownInSwitchers = false;
        window.IsResizable = false;
        window.IsMaximizable = false;
        window.IsMinimizable = false;
        window.IsAlwaysOnTop = true;
        window.SystemBackdrop = new TransparentTintBackdrop();
        HwndExtensions.ToggleWindowStyle(hwnd, false, WindowStyle.TiledWindow);

        uint none = 0xFFFFFFFE; // DWMWA_COLOR_NONE: no 1px Windows 11 border.
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(uint));
        var doNotRound = 1; // the card draws its own corners
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));

        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        if (noActivate)
        {
            ex |= WS_EX_NOACTIVATE;
        }

        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
        return hwnd;
    }
}

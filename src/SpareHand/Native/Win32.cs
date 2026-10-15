using System.Runtime.InteropServices;

namespace SpareHand.Native;

internal static partial class Win32
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;
    public const int VK_ESCAPE = 0x1B;
    public const uint LLKHF_INJECTED = 0x10;
    public const int SM_CXDRAG = 68, SM_CYDRAG = 69;

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly nint HWND_TOPMOST = -1;
    public const int SW_HIDE = 0, SW_SHOWNA = 8;
    public const int DWMWA_CLOAK = 13, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int ASFW_ANY = -1;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    public delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] public static partial nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint threadId);
    [LibraryImport("user32.dll")] public static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] public static partial int GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] public static partial uint GetDoubleClickTime();
    [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindow(string? cls, string? title);
    [LibraryImport("user32.dll")] public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT p);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool ShowWindow(nint hwnd, int cmd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AllowSetForegroundWindow(int processId);
    [LibraryImport("user32.dll")] public static partial nint MonitorFromPoint(POINT pt, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetMonitorInfo(nint mon, ref MONITORINFO info);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)] public static partial int GetClassName(nint hwnd, [Out] char[] buf, int max);
    [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(nint mon, int type, out uint dpiX, out uint dpiY);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref uint value, int size);

    [LibraryImport("user32.dll")] public static partial nint WindowFromPoint(POINT p);
    [LibraryImport("user32.dll")] public static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    /// <summary>True when a screen point is over a window of another process (i.e. a click "outside" us).</summary>
    public static bool IsOverOtherProcess(POINT p)
    {
        var root = GetAncestor(WindowFromPoint(p), 2 /* GA_ROOT */);
        GetWindowThreadProcessId(root, out var pid);
        return pid != (uint)Environment.ProcessId;
    }

    public static string ClassOf(nint hwnd)
    {
        if (hwnd == 0)
        {
            return string.Empty;
        }

        var buf = new char[256];
        var n = GetClassName(hwnd, buf, buf.Length);
        return new string(buf, 0, n);
    }
}

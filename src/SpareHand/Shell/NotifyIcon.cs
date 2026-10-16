using System.Runtime.InteropServices;

namespace SpareHand.Shell;

/// <summary>
/// The notification-area icon: left-click opens the Hold library, right-click shows a native menu.
/// Runs on its own thread with a message-only window. Re-adds itself when Explorer restarts (TaskbarCreated) and
/// reloads the icon at the right size when scaling changes.
/// </summary>
internal sealed class NotifyIcon
{
    private const uint WM_APP_NOTIFY = 0x8001, WM_CONTEXTMENU = 0x007B, WM_LBUTTONUP = 0x0202, NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    private const uint WM_SETTINGCHANGE = 0x001A, WM_DPICHANGED = 0x02E0, WM_CLOSE = 0x0010, WM_DESTROY = 0x0002;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const int CmdLibrary = 1, CmdSettings = 2, CmdPause = 3, CmdQuit = 4, CmdGuide = 5;

    // One window class for the process, so one static WndProc that is never collected. The first version kept the
    // delegate per instance: after the icon was turned off and on, the second instance reused the class registered
    // with the first instance's (collected) delegate and the process fail-fast crashed on the next message.
    private static readonly WndProc StaticProc = Dispatch;
    private static readonly Dictionary<nint, NotifyIcon> Instances = new();
    private static bool _classRegistered;

    private readonly Func<bool> _isPaused;
    private nint _hwnd;
    private nint _icon;
    private uint _taskbarCreated;

    public NotifyIcon(Func<bool> isPaused)
    {
        _isPaused = isPaused;
    }

    public event Action? LibraryRequested;

    public event Action? SettingsRequested;

    public event Action? GuideRequested;

    public event Action? PauseToggled;

    public event Action? QuitRequested;

    public void Start()
    {
        var t = new Thread(Run) { IsBackground = true, Name = "SpareHand notify icon" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public void Remove()
    {
        if (_hwnd != 0)
        {
            PostMessage(_hwnd, WM_CLOSE, 0, 0);
        }
    }

    /// <summary>Refresh the tooltip (e.g. after pausing).</summary>
    public void Update()
    {
        if (_hwnd != 0)
        {
            var data = Data(NIF_TIP | NIF_SHOWTIP);
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
    }

    private void Run()
    {
        var instance = GetModuleHandle(null);
        lock (Instances)
        {
            if (!_classRegistered)
            {
                var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(StaticProc), lpszClassName = "SpareHandNotifyIcon", hInstance = instance };
                _classRegistered = RegisterClassEx(ref wc) != 0;
            }
        }

        _hwnd = CreateWindowEx(0, "SpareHandNotifyIcon", "Spare Hand", 0, 0, 0, 0, 0, -3 /* HWND_MESSAGE */, 0, instance, 0);
        lock (Instances)
        {
            Instances[_hwnd] = this;
        }

        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        Add();

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private void Add()
    {
        _icon = RenderIcon();
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        var added = Shell_NotifyIcon(NIM_ADD, ref data);
        data.uVersion = 4; // NOTIFYICON_VERSION_4: richer callbacks, keyboard support
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
        Log.Write($"notify icon added={added}");
    }

    private NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_NOTIFY,
        hIcon = _icon,
        szTip = _isPaused() ? "Spare Hand (paused)" : "Spare Hand",
    };

    private static nint Dispatch(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        NotifyIcon? target;
        lock (Instances)
        {
            Instances.TryGetValue(hwnd, out target);
        }

        // Never let an exception escape into Windows: it would fail-fast the whole process.
        try
        {
            return target?.Proc(hwnd, msg, wParam, lParam) ?? DefWindowProc(hwnd, msg, wParam, lParam);
        }
        catch (Exception e)
        {
            Log.Write($"notify icon message 0x{msg:X} failed: {e.Message}");
            return 0;
        }
    }

    private nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_APP_NOTIFY)
        {
            var evt = (uint)(lParam & 0xFFFF);

            // NOTIFYICON_VERSION_4 sends both WM_LBUTTONUP and NIN_SELECT for one left-click. Reacting to both made
            // every click open the library and immediately toggle it shut. NIN_SELECT (mouse) and NIN_KEYSELECT
            // (keyboard) are the documented "activate" events.
            if (evt is NIN_SELECT or NIN_KEYSELECT)
            {
                LibraryRequested?.Invoke();
            }
            else if (evt == WM_CONTEXTMENU)
            {
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
            }

            return 0;
        }

        if (msg == _taskbarCreated)
        {
            // Explorer restarted: the icon is gone until we add it again.
            Add();
            return 0;
        }

        if (msg is WM_SETTINGCHANGE or WM_DPICHANGED)
        {
            // Taskbar theme or scaling changed: redraw the glyph.
            var old = _icon;
            _icon = RenderIcon();
            var data = Data(NIF_ICON);
            Shell_NotifyIcon(NIM_MODIFY, ref data);
            if (old != 0)
            {
                DestroyIcon(old);
            }
        }

        if (msg == WM_CLOSE)
        {
            var data = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            DestroyWindow(hwnd);
            return 0;
        }

        if (msg == WM_DESTROY)
        {
            lock (Instances)
            {
                Instances.Remove(hwnd);
            }

            Log.Write("notify icon removed");
            PostQuitMessage(0);
            return 0;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, CmdLibrary, "Open Hold library");
        AppendMenu(menu, 0, CmdSettings, "Settings");
        AppendMenu(menu, 0, CmdGuide, "Show the guide");
        AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, null);
        AppendMenu(menu, _isPaused() ? 0x8u /* MF_CHECKED */ : 0, CmdPause, "Pause");
        AppendMenu(menu, 0x800, 0, null);
        AppendMenu(menu, 0, CmdQuit, "Quit Spare Hand");
        SetMenuDefaultItem(menu, CmdLibrary, 0);

        // Required so the menu closes when the user clicks elsewhere (documented TrackPopupMenu quirk).
        SetForegroundWindow(_hwnd);
        var cmd = TrackPopupMenuEx(menu, 0x0100 /* TPM_RETURNCMD */ | 0x0080 /* TPM_NONOTIFY */ | 0x0020 /* TPM_BOTTOMALIGN */, x, y, _hwnd, 0);
        DestroyMenu(menu);

        switch (cmd)
        {
            case CmdLibrary: LibraryRequested?.Invoke(); break;
            case CmdSettings: SettingsRequested?.Invoke(); break;
            case CmdGuide: GuideRequested?.Invoke(); break;
            case CmdPause: PauseToggled?.Invoke(); break;
            case CmdQuit: QuitRequested?.Invoke(); break;
        }
    }

    /// <summary>
    /// The colour app icon at the current small-icon size. PowerToys' tray icon is its colour logo by default
    /// (monochrome is an opt-in "theme adaptive" setting), and the mitten reads clearly at 16 px.
    /// </summary>
    private static nint RenderIcon()
    {
        var size = GetSystemMetricsForDpi(49 /* SM_CXSMICON */, GetDpiForSystem());
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "SpareHand.ico");
        var icon = LoadImage(0, path, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
        if (icon == 0)
        {
            Log.Write($"notify icon: couldn't load {path}");
        }

        return icon;
    }

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public int x, y;
    }




    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nint id, string? text);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(nint menu, uint item, uint byPos);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint inst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}

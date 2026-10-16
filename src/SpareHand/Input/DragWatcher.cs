using System.Diagnostics;
using static SpareHand.Native.Win32;

namespace SpareHand.Input;

/// <summary>
/// Watches global mouse + keyboard on its own message-loop thread (so a busy UI thread can never get the
/// low-level hooks timed out). Raises <see cref="Summoned"/> when the trigger key is double-tapped during a
/// real shell drag. Logic proven in the spike (docs/research.md).
/// </summary>
internal sealed class DragWatcher
{
    private const double MaxTapMs = 250;

    private readonly Settings _settings;

    // Fields so the GC never collects delegates Windows is calling.
    private HookProc? _mouseProc, _keyProc;

    // Written by the hook thread, and by the UI thread in NoteOwnDrag.
    private volatile bool _buttonDown, _dragCandidate, _dragConfirmed;
    private POINT _downAt;
    private long _keyDownAt = -1, _lastTapAt = -1;
    private bool _keyIsDown, _otherKeyDuringTrigger;

    public DragWatcher(Settings settings)
    {
        _settings = settings;
    }

    /// <summary>Paused from the notification-area menu: the trigger does nothing until resumed.</summary>
    public bool Paused { get; set; }

    /// <summary>Raised on the hook thread when a shell drag is confirmed (good moment to prefetch targets).</summary>
    public event Action<nint>? DragStarted;

    /// <summary>Raised on the hook thread when the trigger is double-tapped during a confirmed drag.</summary>
    public event Action? Summoned;

    /// <summary>Raised on the hook thread for every mouse button press anywhere (for click-outside-to-close).</summary>
    public event Action<POINT>? MouseDown;

    /// <summary>Raised on the hook thread for a double-tap with no drag, or the library hotkey.</summary>
    public event Action? LibraryRequested;

    /// <summary>Raised on the hook thread when the drag ends (button up) or is cancelled (Esc).</summary>
    public event Action<string>? DragEnded;

    public void Start()
    {
        var t = new Thread(Run) { IsBackground = true, Name = "SpareHand hooks" };
        t.Start();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    private uint _threadId;

    private void RegisterLibraryHotkey(int id)
    {
        if (_settings.ParseLibraryHotkey() is { } hk)
        {
            Log.Write($"library hotkey {_settings.LibraryHotkey}: registered={RegisterHotKey(0, id, hk.Modifiers, hk.Key)}");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hwnd, int id);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private void Run()
    {
        // Wrapped: an exception escaping a hook callback into Windows fail-fasts the whole process
        // (learned the hard way from the notification icon's window procedure).
        _mouseProc = (c, w, l) => Safe(MouseProc, c, w, l);
        _keyProc = (c, w, l) => Safe(KeyProc, c, w, l);
        var m = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, 0, 0);
        var k = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, 0, 0);
        Log.Write($"hooks installed mouse={m != 0} key={k != 0} trigger={_settings.Trigger}");

        // The library hotkey posts WM_HOTKEY to this thread's queue (no window needed). Settings changes post
        // WM_APP_REREGISTER here so a new shortcut works without a restart.
        const int HotkeyId = 1;
        const uint WM_APP_REREGISTER = 0x8002;
        _threadId = GetCurrentThreadId();
        RegisterLibraryHotkey(HotkeyId);
        _settings.Changed += () => PostThreadMessage(_threadId, WM_APP_REREGISTER, 0, 0);

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == 0x0312 /* WM_HOTKEY */ && msg.wParam == HotkeyId && !Paused && TriggerGuard.WhyNot(_settings) is null)
            {
                LibraryRequested?.Invoke();
            }
            else if (msg.message == WM_APP_REREGISTER)
            {
                UnregisterHotKey(0, HotkeyId);
                RegisterLibraryHotkey(HotkeyId);
            }
        }
    }

    /// <summary>
    /// A drag that starts in one of Spare Hand's own windows (the guide's practice file). WinUI drags don't create
    /// the shell's SysDragImage window, so the hook can't confirm them by itself.
    /// </summary>
    public void NoteOwnDrag()
    {
        if (_buttonDown && !_dragConfirmed)
        {
            _dragCandidate = true;
            _dragConfirmed = true;
            Log.Write("drag started in Spare Hand's own window");
            DragStarted?.Invoke(0);
        }
    }

    private static nint Safe(HookProc proc, int code, nint wParam, nint lParam)
    {
        try
        {
            return proc(code, wParam, lParam);
        }
        catch (Exception e)
        {
            Log.Write($"hook callback failed: {e.Message}");
            return CallNextHookEx(0, code, wParam, lParam);
        }
    }

    private unsafe nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = *(MSLLHOOKSTRUCT*)lParam;
            switch ((int)wParam)
            {
                case WM_RBUTTONDOWN or WM_MBUTTONDOWN:
                    MouseDown?.Invoke(info.pt);
                    break;

                case WM_LBUTTONDOWN:
                    MouseDown?.Invoke(info.pt);
                    _buttonDown = true;
                    _dragCandidate = false;
                    _dragConfirmed = false;
                    _downAt = info.pt;
                    break;

                case WM_MOUSEMOVE when _buttonDown:
                    if (!_dragCandidate &&
                        (Math.Abs(info.pt.X - _downAt.X) > GetSystemMetrics(SM_CXDRAG) ||
                         Math.Abs(info.pt.Y - _downAt.Y) > GetSystemMetrics(SM_CYDRAG)))
                    {
                        _dragCandidate = true;
                    }

                    // The shell's drag-image window only exists during a real OLE drag, which rules out
                    // window moves, text selection and rubber-band selection.
                    if (_dragCandidate && !_dragConfirmed && FindWindow("SysDragImage", null) != 0)
                    {
                        _dragConfirmed = true;
                        DragStarted?.Invoke(GetForegroundWindow());
                    }

                    break;

                case WM_LBUTTONUP:
                    if (_dragConfirmed)
                    {
                        DragEnded?.Invoke("button up");
                    }

                    _buttonDown = false;
                    _dragCandidate = false;
                    _dragConfirmed = false;
                    break;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private unsafe nint KeyProc(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        var info = *(KBDLLHOOKSTRUCT*)lParam;
        if ((info.flags & LLKHF_INJECTED) != 0)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        var msg = (int)wParam;
        var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        var (left, right) = _settings.TriggerVirtualKeys();
        var isTrigger = info.vkCode == left || info.vkCode == right;

        if (!isTrigger)
        {
            if (isDown)
            {
                // Any other key resets the sequence, so typing never fires it.
                _lastTapAt = -1;
                if (_keyIsDown)
                {
                    _otherKeyDuringTrigger = true;
                }

                if (info.vkCode == VK_ESCAPE && _dragConfirmed)
                {
                    DragEnded?.Invoke("escape");
                }
            }

            return CallNextHookEx(0, code, wParam, lParam);
        }

        var now = Stopwatch.GetTimestamp();
        if (isDown && !_keyIsDown)
        {
            _keyIsDown = true;
            _otherKeyDuringTrigger = false;
            _keyDownAt = now;
        }
        else if (!isDown && _keyIsDown)
        {
            _keyIsDown = false;
            var held = Stopwatch.GetElapsedTime(_keyDownAt, now).TotalMilliseconds;
            if (held < MaxTapMs && !_otherKeyDuringTrigger)
            {
                var window = _settings.DoubleTapMs > 0 ? _settings.DoubleTapMs : (int)GetDoubleClickTime();
                var gap = _lastTapAt < 0 ? double.MaxValue : Stopwatch.GetElapsedTime(_lastTapAt, now).TotalMilliseconds;
                if (gap <= window)
                {
                    _lastTapAt = -1;
                    var quiet = Paused ? "paused" : TriggerGuard.WhyNot(_settings);
                    if (quiet is not null)
                    {
                        Log.Write($"trigger ignored: {quiet}");
                    }
                    else if (_dragConfirmed)
                    {
                        Summoned?.Invoke();
                    }
                    else if (!_buttonDown && _settings.LibraryOnDoubleTap)
                    {
                        LibraryRequested?.Invoke();
                    }
                }
                else
                {
                    _lastTapAt = now;
                }
            }
            else
            {
                _lastTapAt = -1;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }
}

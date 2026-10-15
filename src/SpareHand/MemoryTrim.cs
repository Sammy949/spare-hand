using System.Runtime.InteropServices;

namespace SpareHand;

/// <summary>
/// Hands memory back to Windows while Spare Hand sits idle, the way PowerToys Quick Access does
/// (src/settings-ui/QuickAccess.UI/QuickAccessXAML/MainWindow.xaml.cs: ScheduleMemoryTrim/TrimMemory):
/// wait 2 s, skip if anything opens meanwhile, then GC twice and SetProcessWorkingSetSize(-1, -1).
/// Pages come back on demand when the tray next opens.
/// Surfaces report Opened/Closed so a trim never pages out something on screen; background work (the LocalSend
/// scan pulled idle memory from 12 MB back to 17 MB) calls Idle() when it finishes.
/// </summary>
internal static class MemoryTrim
{
    private static readonly object Gate = new();
    private static CancellationTokenSource? _pending;
    private static int _openSurfaces;

    /// <summary>A surface (tray, library, picker, settings) became visible.</summary>
    public static void Opened()
    {
        lock (Gate)
        {
            _openSurfaces++;
            _pending?.Cancel();
            _pending = null;
        }
    }

    /// <summary>A surface hid. Trims once nothing has been open for 2 s.</summary>
    public static void Closed(string reason)
    {
        lock (Gate)
        {
            _openSurfaces = Math.Max(0, _openSurfaces - 1);
        }

        Idle(reason);
    }

    /// <summary>Background work finished (or the app just started): trim if nothing is on screen.</summary>
    public static void Idle(string reason)
    {
        CancellationTokenSource cts;
        lock (Gate)
        {
            if (_openSurfaces > 0)
            {
                return;
            }

            _pending?.Cancel();
            _pending = cts = new CancellationTokenSource();
        }

        // Delayed so quick open/close toggles don't trigger aggressive GC (PowerToys' reasoning).
        _ = Task.Delay(2000, cts.Token).ContinueWith(
            _ =>
            {
                lock (Gate)
                {
                    if (cts.IsCancellationRequested || _openSurfaces > 0)
                    {
                        return;
                    }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
                Log.Write($"memory trimmed ({reason})");
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(nint process, nint min, nint max);
}

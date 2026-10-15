using static SpareHand.Native.Win32;

namespace SpareHand.Shell;

/// <summary>The folders currently open in File Explorer, most recently used window first.</summary>
internal static class ExplorerFolders
{
    /// <summary>Runs Shell.Application on its own STA thread; returns an empty list on any failure or timeout.</summary>
    public static Task<IReadOnlyList<string>> GetOpenFoldersAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(Enumerate());
            }
            catch (Exception e)
            {
                Log.Write($"explorer folders failed: {e.Message}");
                tcs.TrySetResult([]);
            }
        })
        { IsBackground = true, Name = "SpareHand explorer folders" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task.WaitAsync(timeout).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : (IReadOnlyList<string>)[], TaskScheduler.Default);
    }

    private static List<string> Enumerate()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application missing");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var found = new List<(string Path, nint Hwnd)>();
        foreach (dynamic window in shell.Windows())
        {
            try
            {
                string path = window.Document.Folder.Self.Path;
                nint hwnd = (nint)(long)window.HWND;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    found.Add((path, hwnd));
                }
            }
            catch
            {
                // Non-filesystem views (This PC, Control Panel, search results) have no usable path.
            }
        }

        // Order by z-order: the window nearest the top was used most recently.
        var rank = new Dictionary<nint, int>();
        var i = 0;
        for (var h = GetTopWindowZ(); h != 0; h = GetNextWindowZ(h))
        {
            rank.TryAdd(h, i++);
        }

        return found
            .OrderBy(f => rank.TryGetValue(f.Hwnd, out var r) ? r : int.MaxValue)
            .Select(f => f.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetTopWindow")]
    private static extern nint GetTopWindowNative(nint hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindow")]
    private static extern nint GetWindowNative(nint hwnd, uint cmd);

    private static nint GetTopWindowZ() => GetTopWindowNative(0);

    private static nint GetNextWindowZ(nint h) => GetWindowNative(h, 2 /* GW_HWNDNEXT */);
}

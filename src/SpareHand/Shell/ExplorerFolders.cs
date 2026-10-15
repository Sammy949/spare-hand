using System.Runtime.InteropServices;
using static SpareHand.Native.Win32;

namespace SpareHand.Shell;

/// <summary>
/// The folders currently open in File Explorer, most recently used window first.
/// Typed COM (ShellWindows → IShellBrowser → IFolderView → IPersistFolder2) rather than `dynamic` over
/// Shell.Application: late binding relies on runtime reflection that trimmed releases remove.
/// </summary>
internal static class ExplorerFolders
{
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    /// <summary>Runs on its own STA thread; returns an empty list on any failure or timeout.</summary>
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
        var shellWindows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellWindows, throwOnError: true)!)!;
        var found = new List<(string Path, nint Hwnd)>();
        var count = shellWindows.Count;
        for (var i = 0; i < count; i++)
        {
            try
            {
                if (shellWindows.Item(i) is not IServiceProvider provider)
                {
                    continue;
                }

                var sid = SidTopLevelBrowser;
                var iid = typeof(IShellBrowser).GUID;
                if (provider.QueryService(ref sid, ref iid, out var browserPtr) != 0 || browserPtr == 0)
                {
                    continue;
                }

                var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);
                Marshal.Release(browserPtr);
                browser.GetWindow(out var hwnd);
                browser.QueryActiveShellView(out var view);
                if (view is not IFolderView folderView)
                {
                    continue;
                }

                var persistIid = typeof(IPersistFolder2).GUID;
                folderView.GetFolder(ref persistIid, out var folderObj);
                if (folderObj is not IPersistFolder2 folder)
                {
                    continue;
                }

                folder.GetCurFolder(out var pidl);
                try
                {
                    var buffer = new char[1024];
                    if (SHGetPathFromIDListW(pidl, buffer))
                    {
                        var path = new string(buffer, 0, Array.IndexOf(buffer, '\0') is var n and >= 0 ? n : buffer.Length);
                        if (path.Length > 0 && Directory.Exists(path))
                        {
                            found.Add((path, GetAncestor(hwnd, 2 /* GA_ROOT */)));
                        }
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            catch
            {
                // Non-filesystem views (This PC, Control Panel, search results) have no usable path.
            }
        }

        // Order by z-order: the window nearest the top was used most recently.
        var rank = new Dictionary<nint, int>();
        var z = 0;
        for (var h = GetTopWindow(0); h != 0; h = GetWindow(h, 2 /* GW_HWNDNEXT */))
        {
            rank.TryAdd(h, z++);
        }

        return found
            .OrderBy(f => rank.TryGetValue(f.Hwnd, out var r) ? r : int.MaxValue)
            .Select(f => f.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(nint pidl, [Out] char[] path);

    [DllImport("user32.dll")]
    private static extern nint GetTopWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint cmd);

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? Item([MarshalAs(UnmanagedType.Struct)] object index);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, out nint ppv);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow
        void GetWindow(out nint hwnd);

        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);

        // IShellBrowser, in vtable order; only QueryActiveShellView is called.
        void InsertMenusSB(nint hmenu, nint widths);

        void SetMenuSB(nint hmenu, nint holemenu, nint hwndActive);

        void RemoveMenusSB(nint hmenu);

        void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);

        void EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);

        void TranslateAcceleratorSB(nint msg, ushort id);

        void BrowseObject(nint pidl, uint flags);

        void GetViewStateStream(uint mode, out nint stream);

        void GetControlWindow(uint id, out nint hwnd);

        void SendControlMsg(uint id, uint msg, nint wParam, nint lParam, out nint result);

        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode(out uint mode);

        void SetCurrentViewMode(uint mode);

        void GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object folder);
    }

    [ComImport, Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFolder2
    {
        // IPersist
        void GetClassID(out Guid clsid);

        // IPersistFolder
        void Initialize(nint pidl);

        // IPersistFolder2
        void GetCurFolder(out nint pidl);
    }
}

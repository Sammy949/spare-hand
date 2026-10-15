using System.Runtime.InteropServices;

namespace SpareHand.Shell;

/// <summary>
/// Drives an Explorer context-menu verb (IExplorerCommand) on a set of files, exactly as right-click does.
/// Used for Quick Share ("Send with Quick Share"). See docs/research.md (Sharing).
/// </summary>
internal static class ExplorerCommand
{
    private const uint CLSCTX_INPROC_SERVER = 0x1, CLSCTX_LOCAL_SERVER = 0x4;

    /// <summary>The verb's title, or null if the COM class is not available.</summary>
    public static string? TryGetTitle(Guid clsid)
    {
        if (CoCreateInstance(clsid, 0, CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER, typeof(IExplorerCommand).GUID, out var obj) != 0)
        {
            return null;
        }

        try
        {
            ((IExplorerCommand)obj).GetTitle(null, out var title);
            var text = Marshal.PtrToStringUni(title);
            Marshal.FreeCoTaskMem(title);
            return text;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(obj);
        }
    }

    /// <summary>Invokes the verb on its own STA thread. Explorer grants the handler foreground rights; so do we.</summary>
    public static Task<bool> InvokeAsync(Guid clsid, IReadOnlyList<string> paths)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var hr = CoCreateInstance(clsid, 0, CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER, typeof(IExplorerCommand).GUID, out var obj);
                Marshal.ThrowExceptionForHR(hr);
                var cmd = (IExplorerCommand)obj;
                var items = CreateItemArray(paths);
                var allowed = Native.Win32.AllowSetForegroundWindow(Native.Win32.ASFW_ANY);
                cmd.Invoke(items, 0);
                Log.Write($"explorer command {clsid} invoked on {paths.Count} item(s), foreground grant={allowed}");

                // Out-of-proc handlers pick the call up asynchronously; keep our references alive briefly.
                Thread.Sleep(3000);
                Marshal.ReleaseComObject(cmd);
                tcs.TrySetResult(true);
            }
            catch (Exception e)
            {
                Log.Write($"explorer command {clsid} failed: {e.Message}");
                tcs.TrySetResult(false);
            }
        })
        { IsBackground = true, Name = "SpareHand explorer command" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static IShellItemArray CreateItemArray(IReadOnlyList<string> paths)
    {
        var pidls = paths.Select(p => ILCreateFromPathW(p)).ToArray();
        try
        {
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out var array));
            return array;
        }
        finally
        {
            foreach (var pidl in pidls)
            {
                ILFree(pidl);
            }
        }
    }

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(in Guid clsid, nint outer, uint ctx, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint ILCreateFromPathW(string path);
    [DllImport("shell32.dll")] private static extern void ILFree(nint pidl);
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromIDLists(uint count, nint[] pidls, out IShellItemArray array);

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
    }

    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerCommand
    {
        void GetTitle(IShellItemArray? items, out nint name);
        void GetIcon(IShellItemArray? items, out nint icon);
        void GetToolTip(IShellItemArray? items, out nint tip);
        void GetCanonicalName(out Guid guid);
        void GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);
        void Invoke(IShellItemArray items, nint bindCtx);
        void GetFlags(out uint flags);
        void EnumSubCommands(out nint e);
    }
}

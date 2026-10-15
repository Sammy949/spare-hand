using System.Runtime.InteropServices;

// Usage: ShareProbe <clsid> [--invoke <file>]
// Creates the IExplorerCommand behind a context-menu verb, prints its title/state, optionally invokes it on a file.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var clsid = Guid.Parse(args[0]);
        var fileArg = args.Skip(1).LastOrDefault(a => !a.StartsWith("--"));
        var file = fileArg is null ? null : Path.GetFullPath(fileArg);
        var invoke = args.Contains("--invoke");

        var hr = CoCreateInstance(clsid, IntPtr.Zero, 0x4 | 0x1 /* LOCAL_SERVER | INPROC */, typeof(IExplorerCommand).GUID, out var obj);
        Console.WriteLine($"CoCreateInstance hr=0x{hr:X8}");
        if (hr != 0) return 1;
        var cmd = (IExplorerCommand)obj;

        IShellItemArray? items = null;
        if (file != null)
        {
            SHCreateItemFromParsingName(file, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
            SHCreateShellItemArrayFromShellItem(item, typeof(IShellItemArray).GUID, out items);
        }

        try { cmd.GetTitle(items, out var title); Console.WriteLine($"title: {Marshal.PtrToStringUni(title)}"); Marshal.FreeCoTaskMem(title); }
        catch (Exception e) { Console.WriteLine($"GetTitle failed: {e.Message}"); }
        try { cmd.GetState(items, false, out var state); Console.WriteLine($"state: {state} (0=enabled)"); }
        catch (Exception e) { Console.WriteLine($"GetState failed: {e.Message}"); }

        if (args.Contains("--init"))
        {
            // Explorer calls IInitializeCommand::Initialize(verbId, propertyBag) for manifest-registered verbs.
            if (obj is IInitializeCommand init)
            {
                try { init.Initialize("Share", IntPtr.Zero); Console.WriteLine("Initialize(\"Share\") ok"); }
                catch (Exception e) { Console.WriteLine($"Initialize failed: {e.Message}"); }
            }
            else
            {
                Console.WriteLine("no IInitializeCommand");
            }
        }

        if (args.Contains("--allowfg"))
        {
            Console.WriteLine($"AllowSetForegroundWindow(ASFW_ANY) = {AllowSetForegroundWindow(-1)}");
        }

        if (invoke && items != null)
        {
            try { cmd.Invoke(items, IntPtr.Zero); Console.WriteLine("invoked"); }
            catch (Exception e) { Console.WriteLine($"Invoke failed: {e.Message}"); }
            Thread.Sleep(8000); // give an out-of-proc server time to pick it up before we tear down COM
        }

        return 0;
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint ctx, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] private static extern void SHCreateItemFromParsingName(string path, IntPtr bc, in Guid iid, out IShellItem item);
    [DllImport("shell32.dll", PreserveSig = false)] private static extern void SHCreateShellItemArrayFromShellItem(IShellItem item, in Guid iid, out IShellItemArray array);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray { }

    [ComImport, Guid("85075acf-231f-40ea-9610-d26b7b58f638"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeCommand
    {
        void Initialize([MarshalAs(UnmanagedType.LPWStr)] string commandName, IntPtr propertyBag);
    }

    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerCommand
    {
        void GetTitle(IShellItemArray? items, out IntPtr name);
        void GetIcon(IShellItemArray? items, out IntPtr icon);
        void GetToolTip(IShellItemArray? items, out IntPtr tip);
        void GetCanonicalName(out Guid guid);
        void GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);
        void Invoke(IShellItemArray items, IntPtr bindCtx);
        void GetFlags(out uint flags);
        void EnumSubCommands(out IntPtr e);
    }
}

using System.Runtime.InteropServices;

namespace SpareHand.Shell;

/// <summary>
/// Explorer-equivalent move/copy: the shell's own progress, conflict and undo handling (Ctrl+Z in Explorer works).
/// </summary>
internal static class FileOps
{
    private const uint FO_MOVE = 0x1, FO_COPY = 0x2;
    private const ushort FOF_ALLOWUNDO = 0x40;

    public static bool SameVolume(IEnumerable<string> sources, string destination)
    {
        var destRoot = Path.GetPathRoot(Path.GetFullPath(destination));
        return sources.All(s => string.Equals(Path.GetPathRoot(Path.GetFullPath(s)), destRoot, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs on its own STA thread so the caller (and the tray) never waits on the shell.</summary>
    public static Task<bool> TransferAsync(IReadOnlyList<string> sources, string destination, bool move)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = move ? FO_MOVE : FO_COPY,
                pFrom = string.Join('\0', sources) + "\0\0",
                pTo = destination + "\0\0",
                fFlags = FOF_ALLOWUNDO,
            };
            var result = SHFileOperation(ref op);
            Log.Write($"{(move ? "move" : "copy")} {sources.Count} item(s) -> {Path.GetFileName(destination)}: result=0x{result:X} aborted={op.fAnyOperationsAborted}");
            tcs.TrySetResult(result == 0 && !op.fAnyOperationsAborted);
        })
        { IsBackground = true, Name = "SpareHand file op" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
}

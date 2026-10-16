using System.Runtime.InteropServices;

namespace SpareHand.Shell;

/// <summary>Known folders .NET's SpecialFolder doesn't cover, resolved by Windows so OneDrive and moved folders work.</summary>
internal static class KnownFolders
{
    private static readonly Guid ScreenshotsId = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    /// <summary>Where Win+PrtScn and Snipping Tool save, normally Pictures\Screenshots. Null if Windows has none.</summary>
    public static string? Screenshots => Get(ScreenshotsId);

    private static string? Get(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, 0, out var path) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
}

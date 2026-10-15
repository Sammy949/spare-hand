using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace SpareHand.Shell;

/// <summary>
/// Real shell icons and thumbnails via IShellItemImageFactory, the same source Explorer and PowerToys Peek use
/// (Peek ThumbnailHelper, docs/design.md). Call on the UI thread.
/// </summary>
internal static class ShellIcons
{
    private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="iconOnly">True for the item's icon; false lets files show a content thumbnail.</param>
    public static async Task<ImageSource?> ForPathAsync(string path, int size = 64, bool iconOnly = true)
    {
        var key = $"{path}|{size}|{iconOnly}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = Extract(path, size, iconOnly ? SIIGBF_ICONONLY : SIIGBF_BIGGERSIZEOK);
            if (bitmap is null)
            {
                return null;
            }

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            Cache[key] = source;
            return source;
        }
        catch (Exception e)
        {
            Log.Write($"icon failed for {Path.GetFileName(path)}: {e.GetType().Name} {e.Message}");
            return null;
        }
    }

    private static SoftwareBitmap? Extract(string path, int size, int flags)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, 0, ref iid, out var factory) != 0 || factory is null)
        {
            return null;
        }

        try
        {
            if (factory.GetImage(new SIZE { cx = size, cy = size }, flags, out var hbitmap) != 0 || hbitmap == 0)
            {
                return null;
            }

            try
            {
                return ToSoftwareBitmap(hbitmap);
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>32bpp shell bitmap (premultiplied BGRA) → top-down SoftwareBitmap.</summary>
    private static SoftwareBitmap? ToSoftwareBitmap(nint hbitmap)
    {
        var bm = default(BITMAP);
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), ref bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0)
        {
            return null;
        }

        var info = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = bm.bmWidth,
            biHeight = -bm.bmHeight, // negative = top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[bm.bmWidth * bm.bmHeight * 4];
        var dc = GetDC(0);
        try
        {
            if (GetDIBits(dc, hbitmap, 0, (uint)bm.bmHeight, pixels, ref info, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(0, dc);
        }

        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, bm.bmWidth, bm.bmHeight, BitmapAlphaMode.Premultiplied);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx, cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindCtx, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

    [DllImport("gdi32.dll")] private static extern int GetObject(nint h, int size, ref BITMAP bm);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint h);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
}

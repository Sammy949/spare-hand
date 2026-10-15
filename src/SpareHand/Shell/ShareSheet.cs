using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static SpareHand.Native.Win32;

namespace SpareHand.Shell;

/// <summary>
/// The Windows Share sheet for a desktop app, via IDataTransferManagerInterop
/// (learn.microsoft.com: "Display WinRT UI objects that depend on CoreWindow").
/// The sheet attaches to its owner window and shows nothing if the owner is hidden. The tray is cloaked by the
/// time a drop is handled (first test: "share sheet shown", nothing on screen), so the sheet is owned by DropHost.
/// </summary>
internal static class ShareSheet
{
    private static readonly Guid DataTransferManagerIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    public static async Task ShowAsync(IReadOnlyList<string> paths)
    {
        var files = new List<IStorageItem>();
        foreach (var path in paths)
        {
            files.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
        }

        var hwnd = DropHost.Show();
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var iid = DataTransferManagerIid;
        var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(hwnd, ref iid));

        void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            sender.DataRequested -= OnDataRequested;
            args.Request.Data.Properties.Title = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} items";
            args.Request.Data.SetStorageItems(files);
        }

        void OnTargetChosen(DataTransferManager sender, TargetApplicationChosenEventArgs args)
        {
            sender.TargetApplicationChosen -= OnTargetChosen;
            Log.Write($"share sheet: chose {args.ApplicationName}");
        }

        manager.DataRequested += OnDataRequested;
        manager.TargetApplicationChosen += OnTargetChosen;
        interop.ShowShareUIForWindow(hwnd);
        Log.Write($"share sheet requested for {paths.Count} item(s), host foreground={GetForegroundWindow() == hwnd}");
    }

    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        nint GetForWindow([In] nint appWindow, [In] ref Guid riid);

        void ShowShareUIForWindow(nint appWindow);
    }
}

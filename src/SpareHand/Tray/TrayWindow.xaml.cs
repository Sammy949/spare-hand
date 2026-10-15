using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using SpareHand.Hold;
using SpareHand.Input;
using SpareHand.LocalSend;
using SpareHand.Shell;
using SpareHand.Targets;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;
using WinUIEx;
using static SpareHand.Native.Win32;

namespace SpareHand.Tray;

/// <summary>
/// The tray: a long-lived, never-activated, cloaked-when-hidden window (CmdPal / PowerToys model, docs/design.md + 04).
/// Shown near the cursor mid-drag, it hosts one tile per target.
/// </summary>
public sealed partial class TrayWindow : WindowEx, ITileHost
{
    private const int ShadowPad = 24;
    private const int GapDip = 56;
    private const int TileSize = 84;
    private const int TileHeight = 76;

    private readonly DispatcherQueue _ui;
    private readonly Settings _settings;
    private readonly HoldStore _hold;
    private readonly LocalSendService _localSend;
    private readonly bool _quickShareAvailable;
    private readonly string? _quickShareExe;
    private readonly HoldTarget _holdTarget;
    private readonly string? _localSendExe;

    // Created on first use, not at startup: each WinUI window carries its own composition tree, and most sessions
    // never open the picker or the library (idle-memory work, roadmap #9).
    private LocalSendPicker? _pickerInstance;
    private HoldLibrary? _libraryInstance;
    private ElementTheme? _themeOverride;

    private nint _hwnd;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _closeTimer;
    private bool _open;
    private bool _dropped;
    private DragPayload? _payload;
    private Task<IReadOnlyList<string>> _openFolders = Task.FromResult<IReadOnlyList<string>>([]);

    internal TrayWindow(Settings settings, HoldStore hold, LocalSendService localSend)
    {
        _settings = settings;
        _hold = hold;
        _localSend = localSend;
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();

        _quickShareAvailable = ExplorerCommand.TryGetTitle(QuickShareTarget.VerbClsid) is not null;
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "NearbyShare", "nearby_share.exe");
        _quickShareExe = File.Exists(exe) ? exe : null;
        Log.Write($"quick share available={_quickShareAvailable}");

        _holdTarget = new HoldTarget(hold) { Title = "Hold", Glyph = "" };
        HoldSlot.Child = new TrayTile(_holdTarget, this) { VerticalAlignment = VerticalAlignment.Stretch };
        hold.Changed += () => _ui.TryEnqueue(RefreshHold);
        RefreshHold();

        _localSendExe = LocalSendService.FindAppExe();
    }

    private LocalSendPicker Picker
    {
        get
        {
            if (_pickerInstance is null)
            {
                _pickerInstance = new LocalSendPicker(_localSend);
                if (_themeOverride is { } theme)
                {
                    _pickerInstance.ApplyTheme(theme);
                }
            }

            return _pickerInstance;
        }
    }

    private HoldLibrary Library
    {
        get
        {
            if (_libraryInstance is null)
            {
                _libraryInstance = new HoldLibrary(_hold, _settings, _quickShareAvailable, paths =>
                    Picker.Open(new DragPayload(paths, DataPackageOperation.Copy), GetCursorPos(out var at) ? at : default));
                if (_themeOverride is { } theme)
                {
                    _libraryInstance.ApplyTheme(theme);
                }
            }

            return _libraryInstance;
        }
    }

    /// <summary>Show once so XAML paints and the drop target registers, then cloak and park off-screen.</summary>
    internal void Prepare(DragWatcher watcher)
    {
        _hwnd = this.GetWindowHandle();

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        IsShownInSwitchers = false;
        IsResizable = false;
        IsMaximizable = false;
        IsMinimizable = false;
        IsAlwaysOnTop = true;
        SystemBackdrop = new TransparentTintBackdrop();
        HwndExtensions.ToggleWindowStyle(_hwnd, false, WindowStyle.TiledWindow);

        uint none = 0xFFFFFFFE; // DWMWA_COLOR_NONE: no 1px Windows 11 border.
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(uint));
        var doNotRound = 1; // the card draws its own corners
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));

        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)((long)ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST));

        Activate();
        Cloak(true);
        SetWindowPos(_hwnd, 0, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
        ShowWindow(_hwnd, SW_HIDE);
        ShowWindow(_hwnd, SW_SHOWNA);

        watcher.DragStarted += source => _ui.TryEnqueue(OnDragStarted);
        watcher.Summoned += () => _ui.TryEnqueue(() => Summon(null));
        watcher.DragEnded += reason => _ui.TryEnqueue(() => OnDragEnded(reason));
        watcher.LibraryRequested += () => _ui.TryEnqueue(ToggleLibrary);
        watcher.MouseDown += p => _ui.TryEnqueue(() =>
        {
            // Only windows that exist can be open; don't create them just to ask.
            _libraryInstance?.OnMouseDownAnywhere(p);
            _pickerInstance?.OnMouseDownAnywhere(p);
        });
        Log.Write("tray prepared");
        MemoryTrim.Idle("startup");
    }

    internal void ApplyTheme(ElementTheme theme)
    {
        _themeOverride = theme;
        Root.RequestedTheme = theme;
        _pickerInstance?.ApplyTheme(theme);
        _libraryInstance?.ApplyTheme(theme);
    }

    internal void ToggleLibrary() => Library.Toggle();

    internal void OpenLibraryForTest()
    {
        HoldLibrary.KeepOpenForTest = true;
        Library.Toggle();
    }

    internal void OpenPickerForTest(string file)
    {
        LocalSendPicker.KeepOpenForTest = true;
        Picker.Open(new DragPayload([file], DataPackageOperation.Copy), new POINT { X = 900, Y = 700 });
    }

    /// <summary>Opens the tray. <paramref name="at"/> overrides the cursor (self-test screenshots only).</summary>
    internal async void Summon(POINT? at)
    {
        if (_open)
        {
            return;
        }

        try
        {
            await SummonCore(at);
        }
        catch (Exception e)
        {
            Log.Write($"summon failed: {e}");
            Dismiss("error");
        }
    }

    private async Task SummonCore(POINT? at)
    {
        MemoryTrim.Opened();
        _open = true;
        _dropped = false;
        _payload = null;

        var folders = await _openFolders.WaitAsync(TimeSpan.FromMilliseconds(400)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : [], TaskScheduler.Default);
        BuildSendRow();
        BuildMoveRow(folders);
        RefreshHold();

        var cursor = at ?? (GetCursorPos(out var p) ? p : default);
        var (x, y, w, h, side) = Place(cursor);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Cloak(false);
        Pop();
        Log.Write($"tray shown {side} at {x},{y} {w}x{h} (send={SendRow.Children.Count} move={MoveRow.Children.Count})");
        if (Environment.GetCommandLineArgs().Contains("--hot") && SendRow.Children.FirstOrDefault() is TrayTile hot)
        {
            hot.ShowHotForTest();
        }
    }

    internal void Dismiss(string reason)
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        Cloak(true);
        SetWindowPos(_hwnd, 0, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
        foreach (var tile in MoveRow.Children.OfType<TrayTile>())
        {
            tile.Target.IsDimmed = false;
        }

        Log.Write($"tray hidden ({reason})");
        MemoryTrim.Closed("tray hidden");
    }

    async Task<DragPayload?> ITileHost.ResolvePayloadAsync(DragEventArgs e)
    {
        if (_payload is not null)
        {
            return _payload;
        }

        var paths = new List<string>();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                foreach (var item in await e.DataView.GetStorageItemsAsync())
                {
                    if (!string.IsNullOrEmpty(item.Path))
                    {
                        paths.Add(item.Path);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"reading dragged items failed: {ex.Message}");
        }

        _payload = new DragPayload(paths, e.AllowedOperations);
        Log.Write($"payload: {paths.Count} item(s), allowed={e.AllowedOperations}, formats=[{string.Join(",", e.DataView.AvailableFormats.Take(6))}]");

        // A folder the files already live in can't be a destination.
        var parents = paths.Select(p => Path.GetDirectoryName(p.TrimEnd('\\'))).Where(d => d is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in MoveRow.Children.OfType<TrayTile>())
        {
            if (tile.Target is FolderTarget f && (parents.Contains(f.FolderPath) || paths.Contains(f.FolderPath, StringComparer.OrdinalIgnoreCase)))
            {
                f.IsDimmed = true;
            }
        }

        return _payload;
    }

    void ITileHost.OnTileDropped(TrayTarget target, DragPayload payload, DragDropModifiers modifiers)
    {
        _dropped = true;
        Log.Write($"drop on {target.Title}: {payload.Paths.Count} item(s)");

        // Close first, then act: the drop must feel instant, and long work (LocalSend) continues in the background.
        _ui.TryEnqueue(async () =>
        {
            Dismiss("drop");
            try
            {
                await target.ExecuteAsync(payload, modifiers, _hwnd);
            }
            catch (Exception e)
            {
                Log.Write($"{target.Title} failed: {e}");
                Notifier.Show($"{target.Title} didn't work", e.Message);
            }
        });
    }

    private void OnDragStarted()
    {
        // Prefetch while the user is still dragging, so the tray opens complete.
        _openFolders = ExplorerFolders.GetOpenFoldersAsync(TimeSpan.FromSeconds(2));
        if (_settings.ShowLocalSend)
        {
            _localSend.Refresh();
        }
    }

    private void OnDragEnded(string reason)
    {
        if (!_open)
        {
            return;
        }

        // The drop event can land just after button-up; give it a moment before closing.
        // Field, not local: an unreferenced DispatcherQueueTimer can be collected before it fires.
        _closeTimer?.Stop();
        _closeTimer = _ui.CreateTimer();
        _closeTimer.Interval = TimeSpan.FromMilliseconds(reason == "escape" ? 1 : 400);
        _closeTimer.IsRepeating = false;
        _closeTimer.Tick += (_, _) =>
        {
            if (!_dropped)
            {
                Dismiss(reason);
            }
        };
        _closeTimer.Start();
    }

    private void RefreshHold()
    {
        var count = _hold.Items.Count;
        _holdTarget.Subtitle = count == 0 ? null : $"{count} held";
        var latest = _hold.Items.FirstOrDefault(i => File.Exists(i.Path) || Directory.Exists(i.Path));
        if (latest is null)
        {
            _holdTarget.Icon = null;
            return;
        }

        _ = LoadIconAsync(_holdTarget, latest.Path, iconOnly: false);
    }

    private void BuildSendRow()
    {
        SendRow.Children.Clear();
        if (_quickShareAvailable && _settings.ShowQuickShare)
        {
            var qs = new QuickShareTarget { Title = "Quick Share", Glyph = "" };
            if (_quickShareExe is not null)
            {
                _ = LoadIconAsync(qs, _quickShareExe, iconOnly: true);
            }

            SendRow.Children.Add(Tile(qs));
        }

        if (_settings.ShowLocalSend)
        {
            var ls = new LocalSendTarget(payload => Picker.Open(payload, GetCursorPos(out var at) ? at : default)) { Title = "LocalSend", Glyph = "\uE72D" };
            if (_localSendExe is not null)
            {
                _ = LoadIconAsync(ls, _localSendExe, iconOnly: true);
            }

            SendRow.Children.Add(Tile(ls));
        }

        if (_settings.ShowShareSheet)
        {
            SendRow.Children.Add(Tile(new ShareSheetTarget { Title = "More", Glyph = "" }));
        }

        var any = SendRow.Children.Count > 0;
        SendCaption.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        SendRow.Visibility = SendCaption.Visibility;
    }

    private void BuildMoveRow(IReadOnlyList<string> openFolders)
    {
        MoveRow.Children.Clear();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var folders = new List<string> { desktop };
        folders.AddRange(openFolders.Where(f => !string.Equals(f, desktop, StringComparison.OrdinalIgnoreCase)).Take(Math.Max(0, _settings.MaxFolders)));

        foreach (var folder in folders)
        {
            var title = string.Equals(folder, desktop, StringComparison.OrdinalIgnoreCase) ? "Desktop" : (Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } n ? n : folder);
            var target = new FolderTarget { Title = title, FolderPath = folder, Glyph = "" };
            _ = LoadIconAsync(target, folder, iconOnly: true);
            MoveRow.Children.Add(Tile(target));
        }
    }

    // 84 × 76: icon 32 + gap 6 + two 15px label lines = 68, plus 4+4 padding. Taller tiles left uneven air below the rows.
    private TrayTile Tile(TrayTarget target) => new(target, this) { Width = TileSize, Height = TileHeight };

    private static async Task LoadIconAsync(TrayTarget target, string path, bool iconOnly) =>
        target.Icon = await ShellIcons.ForPathAsync(path, 64, iconOnly);

    private (int X, int Y, int W, int H, string Side) Place(POINT cursor)
    {
        var scale = Placement.ScaleAt(cursor);
        Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardW = (int)Math.Ceiling(Card.DesiredSize.Width * scale);
        var cardH = (int)Math.Ceiling(Card.DesiredSize.Height * scale);
        var pad = (int)Math.Round(ShadowPad * scale);
        var (x, y, side) = Placement.NearCursor(cursor, cardW, cardH, (int)Math.Round(GapDip * scale));
        return (x - pad, y - pad, cardW + (2 * pad), cardH + (2 * pad), side);
    }

    /// <summary>PowerToys "Pop": scale 0.96 → 1 over 150 ms. Content is visible from frame one; no fade-in.</summary>
    private void Pop()
    {
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(Card);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3((float)(Card.ActualWidth / 2), (float)(Card.ActualHeight / 2), 0);
        var anim = compositor.CreateVector3KeyFrameAnimation();
        var easeOut = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        anim.InsertKeyFrame(0f, new Vector3(0.96f, 0.96f, 1f));
        anim.InsertKeyFrame(1f, Vector3.One, easeOut);
        anim.Duration = TimeSpan.FromMilliseconds(150);
        visual.StartAnimation("Scale", anim);
    }

    private void Cloak(bool on)
    {
        var v = on ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_CLOAK, ref v, sizeof(int));
    }
}

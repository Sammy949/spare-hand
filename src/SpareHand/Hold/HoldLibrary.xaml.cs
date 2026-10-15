using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpareHand.Shell;
using SpareHand.Tray;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using WinUIEx;
using static SpareHand.Native.Win32;

namespace SpareHand.Hold;

/// <summary>Everything Hold has, ready to drag back out. Design and evidence: docs/research.md.</summary>
public sealed partial class HoldLibrary : WindowEx
{
    private const int ShadowPad = 24;
    private const int GapDip = 16;

    private readonly HoldStore _store;
    private readonly Settings _settings;
    private readonly Action<IReadOnlyList<string>> _sendLocalSend;
    private readonly bool _quickShareAvailable;
    private readonly DispatcherQueue _ui;
    private readonly nint _hwnd;
    private readonly Dictionary<string, IStorageItem> _storage = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherQueueTimer? _undoTimer;
    private POINT _anchor;
    private (int X, int Y)? _placed;
    private bool _open;
    private long _closedByOutsideClickAt = long.MinValue / 2;
    private bool _busy; // dragging out, or a picker/dialog is up: don't close on deactivate

    /// <summary>Self-test only: keep open when focus moves (the screenshot tool steals focus).</summary>
    internal static bool KeepOpenForTest { get; set; }

    internal HoldLibrary(HoldStore store, Settings settings, bool quickShareAvailable, Action<IReadOnlyList<string>> sendLocalSend)
    {
        _store = store;
        _settings = settings;
        _quickShareAvailable = quickShareAvailable;
        _sendLocalSend = sendLocalSend;
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();
        _hwnd = PopupChrome.Apply(this, noActivate: false);

        store.Changed += () => _ui.TryEnqueue(() =>
        {
            if (_open)
            {
                Rebuild();
            }
        });

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && _open && !_busy && !KeepOpenForTest)
            {
                Close("deactivated");
            }
        };

        AppWindow.Hide();
    }

    internal void ApplyTheme(ElementTheme theme) => Root.RequestedTheme = theme;

    /// <summary>
    /// Click outside closes, like Peek, but without depending on focus: activation isn't guaranteed (e.g. right
    /// after a drop Explorer can keep it), and an unfocused window never gets "Deactivated".
    /// </summary>
    internal void OnMouseDownAnywhere(POINT p)
    {
        if (!_open || _busy || KeepOpenForTest || !IsOverOtherProcess(p))
        {
            return;
        }

        _closedByOutsideClickAt = Environment.TickCount64;
        Close("click outside");
    }

    /// <summary>Opens at the cursor; a second request while open closes it (a toggle, like Win+V).</summary>
    internal void Toggle()
    {
        if (_open)
        {
            Close("toggle");
            return;
        }

        // Clicking the notification icon while the library is open: mouse-down closes it as an outside click, then
        // the icon's select event arrives a moment later. That click meant "close", so don't reopen.
        if (Environment.TickCount64 - _closedByOutsideClickAt < 600)
        {
            Log.Write("hold library: ignoring reopen right after an outside-click close");
            return;
        }

        MemoryTrim.Opened();
        GetCursorPos(out _anchor);
        _placed = null;
        _open = true;
        Filter.Text = string.Empty;
        UndoBar.Visibility = Visibility.Collapsed;
        Rebuild();
        BuildSendMenu();
        Reposition();
        AppWindow.Show(true);
        Activate();
        SetForegroundWindow(_hwnd);
        Filter.Focus(FocusState.Programmatic);
        GetWindowRect(_hwnd, out var r);
        Log.Write($"hold library open ({_store.Items.Count} item(s)) at {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}");
    }

    private void Close(string reason)
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        AppWindow.Hide();
        Log.Write($"hold library closed ({reason})");
        MemoryTrim.Closed("library closed");
    }

    private void Rebuild()
    {
        var keep = SelectedPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filter = Filter.Text.Trim();
        var items = _store.Items
            .Where(i => filter.Length == 0 || Path.GetFileName(i.Path).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Items.Items.Clear();
        foreach (var item in items)
        {
            var tile = ItemTile(item);
            Items.Items.Add(tile);
            if (keep.Contains(item.Path))
            {
                Items.SelectedItems.Add(tile);
            }
        }

        var total = _store.Items.Count;
        CountText.Text = total == 1 ? "1 item" : $"{total} items";
        EmptyText.Text = total == 0
            ? $"Nothing held yet. While dragging a file, double-tap {_settings.Trigger} and drop it on Hold."
            : "Nothing matches the filter.";
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Items.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DragAll.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
        SendButton.IsEnabled = ClearButton.IsEnabled = total > 0;
        UpdateDragAllText();
        Reposition();
    }

    private FrameworkElement ItemTile(HoldItem item)
    {
        var exists = item.Exists;
        var tile = new Grid { Width = 92, Height = 104, Padding = new Thickness(4, 8, 4, 6), RowSpacing = 6, Tag = item };
        tile.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        tile.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var image = new Image { Width = 56, Height = 56, Stretch = Stretch.Uniform };
        var glyph = new FontIcon
        {
            Glyph = exists ? "" : "",
            FontSize = 28,
            Style = Style(exists ? "SpareHandSecondaryIcon" : "SpareHandCautionIcon"),
            Visibility = Visibility.Collapsed,
        };
        var art = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        art.Children.Add(image);
        art.Children.Add(glyph);
        tile.Children.Add(art);

        // Name always shows, so you can tell what went missing; "Missing" is a caution line under it.
        var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        label.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(item.Path.TrimEnd('\\')),
            FontSize = 12,
            LineHeight = 15,
            MaxLines = exists ? 2 : 1,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            // One line for missing items: wrapping + MaxLines=1 cut "gone-quarterly-report.pdf" down to "gone…".
            TextWrapping = exists ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = Style(exists ? "SpareHandPrimaryText" : "SpareHandSecondaryText"),
        });
        if (!exists)
        {
            label.Children.Add(new TextBlock
            {
                Text = "Missing",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = Style("SpareHandCautionText"),
            });
        }

        Grid.SetRow(label, 1);
        tile.Children.Add(label);

        ToolTipService.SetToolTip(tile, exists ? item.Path : $"Missing: {item.Path}\nRight-click to locate or remove it.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, exists ? Path.GetFileName(item.Path) : $"Missing {Path.GetFileName(item.Path)}");

        if (exists)
        {
            _ = LoadArtAsync(item.Path, image, glyph);
            _ = CacheStorageItemAsync(item.Path);
        }
        else
        {
            glyph.Visibility = Visibility.Visible;
        }

        return tile;
    }

    private static async Task LoadArtAsync(string path, Image image, FontIcon fallback)
    {
        var art = await ShellIcons.ForPathAsync(path, 96, iconOnly: false);
        if (art is null)
        {
            fallback.Visibility = Visibility.Visible;
            return;
        }

        image.Source = art;
    }

    private async Task CacheStorageItemAsync(string path)
    {
        if (_storage.ContainsKey(path))
        {
            return;
        }

        try
        {
            _storage[path] = Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path);
        }
        catch (Exception e)
        {
            Log.Write($"hold: can't prepare {Path.GetFileName(path)} for drag-out: {e.Message}");
        }
    }

    // ---- Drag out ----

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<FrameworkElement>().Select(t => t.Tag).OfType<HoldItem>().Where(i => i.Exists).Select(i => i.Path).ToList();
        var storage = paths.Where(_storage.ContainsKey).Select(p => _storage[p]).ToList();
        if (storage.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        PrepareDragOut(e.Data, storage);
        e.Data.Properties.Add("SpareHandPaths", string.Join('\n', paths));
        _busy = true;
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        _busy = false;
        var paths = e.Items.OfType<FrameworkElement>().Select(t => t.Tag).OfType<HoldItem>().Select(i => i.Path).ToList();
        AfterDragOut(paths, e.DropResult);
    }

    private async void OnDragAllStarting(UIElement sender, DragStartingEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var paths = DragAllPaths();
            foreach (var p in paths)
            {
                await CacheStorageItemAsync(p);
            }

            var storage = paths.Where(_storage.ContainsKey).Select(p => _storage[p]).ToList();
            if (storage.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            PrepareDragOut(e.Data, storage);
            e.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Link;
            _busy = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnDragAllCompleted(UIElement sender, DropCompletedEventArgs e)
    {
        _busy = false;
        AfterDragOut(DragAllPaths(), e.DropResult);
    }

    /// <summary>Always offer Copy, never Move: a target that "moves" would take the original away (NotchNook bug).</summary>
    private static void PrepareDragOut(DataPackage data, IReadOnlyList<IStorageItem> storage)
    {
        data.SetStorageItems(storage, readOnly: true);
        data.RequestedOperation = DataPackageOperation.Copy;
    }

    private void AfterDragOut(IReadOnlyList<string> paths, DataPackageOperation result)
    {
        Log.Write($"hold: dragged out {paths.Count} item(s), result={result}");
        if (result != DataPackageOperation.None && _settings.RemoveAfterDragOut)
        {
            RemoveWithUndo(paths);
        }
    }

    private List<string> DragAllPaths()
    {
        var selected = SelectedPaths().ToList();
        return (selected.Count > 0 ? selected : _store.Items.Select(i => i.Path)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDragAllText();

    private void UpdateDragAllText()
    {
        var n = Items.SelectedItems.Count;
        DragAllText.Text = n > 0 ? $"Drag {n} selected" : "Drag all";
    }

    // ---- Keyboard ----

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close("escape");
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_open)
        {
            Rebuild();
        }
    }

    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Down or VirtualKey.Enter && Items.Items.Count > 0)
        {
            e.Handled = true;
            Items.SelectedIndex = 0;
            (Items.ContainerFromIndex(0) as Control)?.Focus(FocusState.Keyboard);
        }
    }

    private void OnItemsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        var alt = IsDown(VirtualKey.Menu);
        switch (e.Key)
        {
            case VirtualKey.Enter:
                OpenPaths(SelectedPaths());
                break;
            case VirtualKey.Delete:
                RemoveWithUndo(SelectedPaths().ToList());
                break;
            case VirtualKey.C when ctrl:
                CopyFiles(SelectedPaths().ToList());
                break;
            case >= VirtualKey.Number1 and <= VirtualKey.Number9 when alt:
                // Alt+1..9 picks a slot, like the PowerToys Command Palette Drawer.
                var index = e.Key - VirtualKey.Number1;
                if (index < Items.Items.Count)
                {
                    Items.SelectedIndex = index;
                    (Items.ContainerFromIndex(index) as Control)?.Focus(FocusState.Keyboard);
                }

                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnItemsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ItemFrom(e.OriginalSource) is { } item)
        {
            OpenPaths([item.Path]);
        }
    }

    // ---- Context menu ----

    private void OnItemsRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ItemFrom(e.OriginalSource) is not { } item)
        {
            return;
        }

        // Right-clicking outside the selection acts on that item alone, as in Explorer.
        var tile = Items.Items.OfType<FrameworkElement>().First(t => ReferenceEquals(t.Tag, item));
        if (!Items.SelectedItems.Contains(tile))
        {
            Items.SelectedItems.Clear();
            Items.SelectedItems.Add(tile);
        }

        var paths = SelectedPaths().ToList();
        var single = paths.Count == 1;
        var menu = new MenuFlyout();

        if (!item.Exists && single)
        {
            menu.Items.Add(MenuItem("Locate…", "", () => LocateAsync(item.Path)));
        }
        else
        {
            menu.Items.Add(MenuItem("Open", "", () => OpenPaths(paths)));
            if (single && File.Exists(item.Path))
            {
                menu.Items.Add(MenuItem("Open with…", "", () => Shell("openas", item.Path)));
            }

            if (single)
            {
                menu.Items.Add(MenuItem("Show in folder", "", () => Process.Start("explorer.exe", $"/select,\"{item.Path}\"")));
            }

            menu.Items.Add(MenuItem("Copy", "", () => CopyFiles(paths)));
            menu.Items.Add(MenuItem(single ? "Copy path" : "Copy paths", "", () => CopyText(string.Join(Environment.NewLine, paths))));
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var send in SendItems(() => paths))
            {
                menu.Items.Add(send);
            }
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(single ? "Remove from Hold" : $"Remove {paths.Count} from Hold", "", () => RemoveWithUndo(paths)));
        menu.ShowAt(Items, e.GetPosition(Items));
        e.Handled = true;
    }

    private void BuildSendMenu()
    {
        SendMenu.Items.Clear();
        foreach (var item in SendItems(DragAllPaths))
        {
            SendMenu.Items.Add(item);
        }
    }

    private IEnumerable<MenuFlyoutItem> SendItems(Func<IReadOnlyList<string>> paths)
    {
        if (_quickShareAvailable)
        {
            yield return MenuItem("Quick Share", "", () => ExplorerCommand.InvokeAsync(Targets.QuickShareTarget.VerbClsid, paths()));
        }

        if (_settings.ShowLocalSend)
        {
            yield return MenuItem("LocalSend", "", () =>
            {
                var files = paths().Where(File.Exists).ToList();
                Close("send");
                _sendLocalSend(files);
            });
        }

        yield return MenuItem("More…", "", () => ShareSheet.ShowAsync(paths()));
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Func<Task> action) => MenuItem(text, glyph, () => { _ = action(); });

    // ---- Actions ----

    private void OnClearClick(object sender, RoutedEventArgs e) => RemoveWithUndo(_store.Items.Select(i => i.Path).ToList());

    private void RemoveWithUndo(IReadOnlyList<string> paths)
    {
        var removed = _store.Remove(paths);
        if (removed == 0)
        {
            return;
        }

        UndoText.Text = removed == 1 ? "Removed 1 item" : $"Removed {removed} items";
        UndoBar.Visibility = Visibility.Visible;
        _undoTimer?.Stop();
        _undoTimer = _ui.CreateTimer();
        _undoTimer.Interval = TimeSpan.FromSeconds(10);
        _undoTimer.IsRepeating = false;
        _undoTimer.Tick += (_, _) => UndoBar.Visibility = Visibility.Collapsed;
        _undoTimer.Start();
        Reposition();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        _store.UndoRemove();
        UndoBar.Visibility = Visibility.Collapsed;
        Reposition();
    }

    private async Task LocateAsync(string missingPath)
    {
        _busy = true;
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add(Path.GetExtension(missingPath) is { Length: > 0 } ext ? ext : "*");
            picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFileAsync() is { } found)
            {
                _store.Relocate(missingPath, found.Path);
            }
        }
        finally
        {
            _busy = false;
            Activate();
        }
    }

    private static void OpenPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(p => File.Exists(p) || Directory.Exists(p)).Take(10))
        {
            Shell("open", path);
        }
    }

    private static void Shell(string verb, string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = verb });
        }
        catch (Exception e)
        {
            Log.Write($"hold: {verb} failed for {Path.GetFileName(path)}: {e.Message}");
        }
    }

    private void CopyFiles(IReadOnlyList<string> paths)
    {
        var storage = paths.Where(_storage.ContainsKey).Select(p => _storage[p]).ToList();
        if (storage.Count == 0)
        {
            return;
        }

        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetStorageItems(storage, readOnly: true);
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    private static void CopyText(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    // ---- Helpers ----

    private IEnumerable<string> SelectedPaths() =>
        Items.SelectedItems.OfType<FrameworkElement>().Select(t => t.Tag).OfType<HoldItem>().Select(i => i.Path);

    private static HoldItem? ItemFrom(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { Tag: HoldItem item })
            {
                return item;
            }
        }

        return null;
    }

    private static Style Style(string key) => (Style)Application.Current.Resources[key];

    private void Reposition()
    {
        if (!_open)
        {
            return;
        }

        var scale = Placement.ScaleAt(_anchor);
        Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardW = (int)Math.Ceiling(Card.DesiredSize.Width * scale);
        var cardH = (int)Math.Ceiling(Card.DesiredSize.Height * scale);
        var pad = (int)Math.Round(ShadowPad * scale);

        // Place once per open, then only resize: a library that jumps as items come and go is hard to aim at.
        _placed ??= Placement.NearCursor(_anchor, cardW, cardH, (int)Math.Round(GapDip * scale)) is var (x, y, _) ? (x, y) : default;
        SetWindowPos(_hwnd, HWND_TOPMOST, _placed.Value.X - pad, _placed.Value.Y - pad, cardW + (2 * pad), cardH + (2 * pad), 0);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);
}

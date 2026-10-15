using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpareHand.Shell;
using WinUIEx;

namespace SpareHand;

/// <summary>Every control writes straight to Settings and saves; running parts pick changes up live.</summary>
public sealed partial class SettingsWindow : WindowEx
{
    private readonly Settings _settings;
    private readonly Func<bool> _isPaused;
    private readonly Action<bool> _setPaused;
    // While Load() fills the controls, their change events must not write back: one handler reads several
    // controls, and half-loaded values flipped LocalSend and More off in memory on the first version.
    private bool _loading;

    internal SettingsWindow(Settings settings, Func<bool> isPaused, Action<bool> setPaused)
    {
        _settings = settings;
        _isPaused = isPaused;
        _setPaused = setPaused;
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        FitToScreen();
        Load();
    }

    /// <summary>First open landed partly below the screen: size to the work area (in DIPs) and centre.</summary>
    private void FitToScreen()
    {
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var scale = this.GetDpiForWindow() / 96.0;
        Width = Math.Min(900, (area.Width / scale) - 48);
        Height = Math.Min(760, (area.Height / scale) - 48);
        this.CenterOnScreen();
    }

    internal void Bring()
    {
        Load();
        Activate();
        this.SetForegroundWindow();
    }

    private void Load()
    {
        _loading = true;
        Listening.IsOn = !_isPaused();
        Trigger.SelectedIndex = (int)_settings.Trigger;
        TapSystem.Content = $"Windows double-click speed ({GetDoubleClickTime()} ms)";
        TapSpeed.SelectedIndex = _settings.DoubleTapMs switch { 300 => 1, 700 => 2, _ => 0 };
        LibraryOnDoubleTap.IsOn = _settings.LibraryOnDoubleTap;
        LibraryHotkey.Text = _settings.LibraryHotkey;

        var quickShare = ExplorerCommand.TryGetTitle(Targets.QuickShareTarget.VerbClsid) is not null;
        QuickShareCard.Description = quickShare ? "Opens Quick Share with the file ready; you pick the device there." : "Quick Share isn't installed on this PC.";
        QuickShareCard.IsEnabled = quickShare;
        ShowQuickShare.IsOn = _settings.ShowQuickShare;
        ShowLocalSend.IsOn = _settings.ShowLocalSend;
        ShowShareSheet.IsOn = _settings.ShowShareSheet;
        MaxFolders.Value = _settings.MaxFolders;
        RemoveAfterDragOut.IsOn = _settings.RemoveAfterDragOut;
        PauseInFullscreen.IsOn = _settings.PauseInFullscreen;
        StartWithWindows.IsOn = SystemIntegration.StartsWithWindows;
        ShowNotifyIcon.IsOn = _settings.ShowNotifyIcon;
        VersionText.Text = $"Spare Hand {typeof(App).Assembly.GetName().Version?.ToString(3)} · no account, no telemetry, works offline.";
        RenderExcluded();
        UpdateTriggerNotes();
        _loading = false;
    }

    private void Save()
    {
        if (!_loading)
        {
            _settings.Save();
        }
    }

    private void OnListeningToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _setPaused(!Listening.IsOn);
        }
    }

    private void OnTriggerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (Trigger.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<TriggerKey>(tag, out var key))
        {
            _settings.Trigger = key;
            UpdateTriggerNotes();
            Save();
        }
    }

    private void UpdateTriggerNotes()
    {
        CtrlClash.IsOpen = _settings.Trigger == TriggerKey.Ctrl && SystemIntegration.FindMyMouseUsesDoubleCtrl();
        ShiftNote.IsOpen = _settings.Trigger == TriggerKey.Shift;
    }

    private void OnTapSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (TapSpeed.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _settings.DoubleTapMs = int.Parse(tag);
            Save();
        }
    }

    private void OnLibraryToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.LibraryOnDoubleTap = LibraryOnDoubleTap.IsOn;
        Save();
    }

    private void OnHotkeyCommitted(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var previous = _settings.LibraryHotkey;
        _settings.LibraryHotkey = LibraryHotkey.Text.Trim();
        var valid = _settings.LibraryHotkey.Length == 0 || _settings.ParseLibraryHotkey() is not null;
        HotkeyError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (!valid)
        {
            _settings.LibraryHotkey = previous;
            return;
        }

        Save();
    }

    private void OnTilesChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.ShowQuickShare = ShowQuickShare.IsOn;
        _settings.ShowLocalSend = ShowLocalSend.IsOn;
        _settings.ShowShareSheet = ShowShareSheet.IsOn;
        Save();
    }

    private void OnMaxFoldersChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }

        if (!double.IsNaN(args.NewValue))
        {
            _settings.MaxFolders = (int)Math.Clamp(args.NewValue, 0, 6);
            Save();
        }
    }

    private void OnRemoveAfterDragOutToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.RemoveAfterDragOut = RemoveAfterDragOut.IsOn;
        Save();
    }

    private void OnFullscreenToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.PauseInFullscreen = PauseInFullscreen.IsOn;
        Save();
    }

    private void OnExcludeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            OnExcludeAdd(sender, e);
        }
    }

    private void OnExcludeAdd(object sender, RoutedEventArgs e)
    {
        var name = ExcludeInput.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name += ".exe";
        }

        if (!_settings.ExcludedApps.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ExcludedApps.Add(name);
            Save();
        }

        ExcludeInput.Text = string.Empty;
        RenderExcluded();
    }

    private void RenderExcluded()
    {
        ExcludedList.Children.Clear();
        ExcludedExpander.Description = _settings.ExcludedApps.Count == 0
            ? "The trigger never fires while these apps are in front. None yet."
            : $"The trigger never fires while these apps are in front: {_settings.ExcludedApps.Count}.";
        foreach (var app in _settings.ExcludedApps.ToList())
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = app, VerticalAlignment = VerticalAlignment.Center, MinWidth = 220 });
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) =>
            {
                _settings.ExcludedApps.Remove(app);
                Save();
                RenderExcluded();
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            ExcludedList.Children.Add(row);
        }
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            SystemIntegration.StartsWithWindows = StartWithWindows.IsOn;
        }
    }

    private void OnNotifyIconToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.ShowNotifyIcon = ShowNotifyIcon.IsOn;
        Save();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start("explorer.exe", $"\"{AppPaths.Root}\"");

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SpareHand.Hold;
using SpareHand.Input;
using SpareHand.Shell;
using SpareHand.Targets;
using SpareHand.Tray;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinUIEx;

namespace SpareHand.Guide;

/// <summary>
/// First-run guide. Three steps, each finished by doing the thing for real: hold the practice file through the
/// tray, open the Hold library, then a short summary. Every step can be skipped.
/// </summary>
public sealed partial class GuideWindow : WindowEx
{
    private enum Stage
    {
        HoldIdle,
        HoldDragging,
        HoldTrayOpen,
        HoldDone,
        BackIdle,
        BackDone,
        Summary,
    }

    private static readonly string PracticePath = Path.Combine(AppPaths.Root, "practice", "Practice file.txt");

    private readonly Settings _settings;
    private readonly HoldStore _hold;
    private readonly DragWatcher _watcher;
    private readonly TrayWindow _tray;
    private readonly DispatcherQueue _ui;
    private readonly bool _animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private readonly string _key;
    private Stage _stage;
    private bool _loading;

    internal GuideWindow(Settings settings, HoldStore hold, DragWatcher watcher, TrayWindow tray)
    {
        _settings = settings;
        _hold = hold;
        _watcher = watcher;
        _tray = tray;
        _key = settings.Trigger.ToString();
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();

        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "SpareHand.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        IsResizable = false;
        IsMaximizable = false;
        IsMinimizable = false;
        Width = 560;
        Height = 560;
        this.CenterOnScreen();

        KeycapText.Text = _key;
        var others = string.Join(" or ", Enum.GetNames<TriggerKey>().Where(k => k != _key));
        SettingsLine.Text = $"Right-click the hand in the notification area. {_key} can become {others} there.";
        EnsurePracticeFile();
        _ = LoadPracticeIconAsync();

        _watcher.DragStarted += OnDragStartedAnywhere;
        _watcher.DragEnded += OnDragEndedAnywhere;
        _tray.Shown += OnTrayShown;
        _tray.Dropped += OnDropped;
        _tray.LibraryOpened += OnLibraryOpened;
        Closed += OnClosed;

        Show(Stage.HoldIdle);
        Log.Write("guide open");
    }

    internal void Bring()
    {
        Activate();
        this.SetForegroundWindow();
    }

    internal void ApplyTheme(ElementTheme theme) => Root.RequestedTheme = theme;

    /// <summary>Self-test screenshots: jump to a stage without doing it.</summary>
    internal void ShowForTest(string stage)
    {
        if (Enum.TryParse<Stage>(stage, ignoreCase: true, out var s))
        {
            Show(s);
        }
    }

    private void Show(Stage stage)
    {
        _stage = stage;
        var step = stage switch
        {
            <= Stage.HoldDone => 1,
            <= Stage.BackDone => 2,
            _ => 3,
        };

        switch (step)
        {
            case 1:
                StepTitle.Text = "Hold it";
                StepBody.Text = $"Drag the practice file. While you're still holding it, double-tap {_key}, then drop it on Hold.";
                break;
            case 2:
                StepTitle.Text = "Get it back";
                StepBody.Text = $"Double-tap {_key} with nothing in your hand. Your Hold library opens next to the cursor, and you can drag things out of it into any app.";
                break;
            default:
                StepTitle.Text = "That's the whole trick";
                StepBody.Text = "The same tray has a few more places to put things.";
                break;
        }

        PracticePanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        SummaryPanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        StatusRow.Visibility = step == 3 ? Visibility.Collapsed : Visibility.Visible;
        StatusRow.VerticalAlignment = step == 2 ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        StatusRow.Margin = step == 2 ? new Thickness(0) : new Thickness(0, 0, 0, 24);

        var (status, waitingForKey, done) = stage switch
        {
            Stage.HoldIdle => ("Drag the file to start", false, false),
            Stage.HoldDragging => ("Now tap it twice", true, false),
            Stage.HoldTrayOpen => ("Drop it on Hold", false, false),
            Stage.HoldDone => ("Held. It stays there until you take it out.", false, true),
            Stage.BackIdle => ("Tap it twice, with nothing in your hand", true, false),
            Stage.BackDone => ("That's your Hold library.", false, true),
            _ => (string.Empty, false, false),
        };

        StatusText.Text = status;
        Keycap.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        DoneMark.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        if (done)
        {
            Pop();
        }

        TapRhythm.Stop();
        if (waitingForKey && _animate)
        {
            TapRhythm.Begin();
        }

        var finished = stage is Stage.HoldDone or Stage.BackDone or Stage.Summary;
        NextButton.Content = stage == Stage.Summary ? "Done" : finished ? "Next" : "Skip this step";
        NextButton.Style = finished ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
        SkipGuide.Visibility = step == 3 ? Visibility.Collapsed : Visibility.Visible;
        Progress.Text = $"{step} of 3";

        if (step == 3)
        {
            _loading = true;
            StartWithWindows.IsOn = SystemIntegration.StartsWithWindows;
            _loading = false;
        }

        Log.Write($"guide: {stage}");
    }

    /// <summary>The check mark lands with a small overshoot. It is visible from the first frame.</summary>
    private void Pop()
    {
        if (!_animate)
        {
            return;
        }

        var ease = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        var board = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var a = new DoubleAnimation { From = 0.6, To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = ease };
            Storyboard.SetTarget(a, DonePop);
            Storyboard.SetTargetProperty(a, property);
            board.Children.Add(a);
        }

        board.Begin();
    }

    // Hook-thread events from the drag watcher; everything else arrives on the UI thread.
    private void OnDragStartedAnywhere(nint source) => _ui.TryEnqueue(() =>
    {
        if (_stage == Stage.HoldIdle)
        {
            Show(Stage.HoldDragging);
        }
    });

    private void OnDragEndedAnywhere(string reason) => _ui.TryEnqueue(() =>
    {
        // Let a drop on the tray land first (it arrives just after the button comes up).
        var timer = _ui.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            if (_stage is Stage.HoldDragging or Stage.HoldTrayOpen)
            {
                Show(Stage.HoldIdle);
            }
        };
        timer.Start();
    });

    private void OnTrayShown()
    {
        if (_stage is Stage.HoldIdle or Stage.HoldDragging)
        {
            Show(Stage.HoldTrayOpen);
        }
    }

    private void OnDropped(TrayTarget target)
    {
        if (_stage > Stage.HoldTrayOpen)
        {
            return;
        }

        if (target is HoldTarget)
        {
            Show(Stage.HoldDone);
            return;
        }

        Show(Stage.HoldIdle);
        StatusText.Text = $"That went to {target.Title}. Try Hold, the tall one on the left.";
        _ui.TryEnqueue(DispatcherQueuePriority.Low, EnsurePracticeFile);
    }

    private void OnLibraryOpened()
    {
        if (_stage == Stage.BackIdle)
        {
            Show(Stage.BackDone);
        }
        else if (_stage == Stage.HoldIdle)
        {
            StatusText.Text = "That's the library. For this step, start dragging the file first.";
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        switch (_stage)
        {
            case <= Stage.HoldDone:
                Show(Stage.BackIdle);
                break;
            case <= Stage.BackDone:
                Show(Stage.Summary);
                break;
            default:
                Close();
                break;
        }
    }

    private void OnSkipGuide(object sender, RoutedEventArgs e) => Close();

    private void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            SystemIntegration.StartsWithWindows = StartWithWindows.IsOn;
        }
    }

    private async void OnPracticeDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            EnsurePracticeFile();
            var file = await StorageFile.GetFileFromPathAsync(PracticePath);
            e.Data.SetStorageItems([file], readOnly: false);
            e.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Move | DataPackageOperation.Link;
            _watcher.NoteOwnDrag();
        }
        catch (Exception ex)
        {
            Log.Write($"guide: practice drag failed: {ex.Message}");
            e.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    // Explorer-style hover: the file reads as a file, and shows it can be picked up.
    private void OnPracticePointerEntered(object sender, PointerRoutedEventArgs e) =>
        PracticeFile.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];

    private void OnPracticePointerExited(object sender, PointerRoutedEventArgs e) =>
        PracticeFile.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private async Task LoadPracticeIconAsync() => PracticeIcon.Source = await ShellIcons.ForPathAsync(PracticePath, 96, iconOnly: true);

    private static void EnsurePracticeFile()
    {
        try
        {
            if (!File.Exists(PracticePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PracticePath)!);
                File.WriteAllText(PracticePath, "A practice file from Spare Hand's guide. Delete it whenever you like.\r\n");
            }
        }
        catch (Exception e)
        {
            Log.Write($"guide: couldn't create the practice file: {e.Message}");
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        TapRhythm.Stop();
        _watcher.DragStarted -= OnDragStartedAnywhere;
        _watcher.DragEnded -= OnDragEndedAnywhere;
        _tray.Shown -= OnTrayShown;
        _tray.Dropped -= OnDropped;
        _tray.LibraryOpened -= OnLibraryOpened;

        // Tidy up: the practice file leaves Hold and disk. A copy dropped somewhere else stays where the user put it.
        _hold.Forget(PracticePath);
        try
        {
            File.Delete(PracticePath);
        }
        catch
        {
            // Locked or already gone: harmless.
        }

        _settings.GuideShown = true;
        _settings.Save();
        Log.Write($"guide closed at {_stage}");
    }
}

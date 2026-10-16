using Microsoft.UI.Xaml;
using SpareHand.Hold;
using SpareHand.Input;
using SpareHand.LocalSend;
using SpareHand.Tray;

namespace SpareHand;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private static EventWaitHandle? _showSignal;
    private Shell.NotifyIcon? _notifyIcon;
    private SettingsWindow? _settingsWindow;
    private Settings? _settings;
    private DragWatcher? _watcher;

    private TrayWindow? _tray;
    private HoldStore? _hold;
    private Guide.GuideWindow? _guide;
    private ScreenshotWatcher? _screenshots; // kept: it owns the folder watcher and its timers
    private ElementTheme? _themeOverride;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _selfTestTimer;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Write($"UNHANDLED {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, @"Local\SpareHand.SingleInstance", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SpareHand.Show");
        if (!first)
        {
            // Opening Spare Hand again (Start menu, a shortcut) while it runs brings up its settings: that is what
            // someone who clicks the app expects, and the way back in when the notification-area icon is hidden.
            // The Hold library is the notification-area icon's job.
            Log.Write("another instance is running; asking it to open settings");
            _showSignal.Set();
            Exit();
            return;
        }

        var argv = Environment.GetCommandLineArgs();
        Log.Write($"=== Spare Hand start ({string.Join(' ', argv.Skip(1))}) ===");

        var settings = _settings = Settings.Load();
        var hold = _hold = new HoldStore();
        var localSend = new LocalSendService(LocalSendIdentity.LoadOrCreate());
        if (settings.ShowLocalSend)
        {
            localSend.Start();
        }

        Notifier.Register();

        var watcher = _watcher = new DragWatcher(settings);
        _tray = new TrayWindow(settings, hold, localSend);
        _tray.Prepare(watcher);
        watcher.Start();
        _screenshots = new ScreenshotWatcher(hold, settings, _tray.DispatcherQueue);

        var ui = _tray.DispatcherQueue;
        new Thread(() =>
        {
            while (_showSignal.WaitOne())
            {
                ui.TryEnqueue(OpenSettings);
            }
        })
        { IsBackground = true, Name = "SpareHand show signal" }.Start();

        SyncNotifyIcon();
        settings.Changed += () => ui.TryEnqueue(SyncNotifyIcon);

        // Self-test: open the tray at a fixed point without a drag, for screenshots. Never used in normal runs.
        var themeArg = Array.IndexOf(argv, "--theme");
        if (themeArg >= 0 && themeArg + 1 < argv.Length)
        {
            _themeOverride = argv[themeArg + 1] == "light" ? ElementTheme.Light : ElementTheme.Dark;
            _tray.ApplyTheme(_themeOverride.Value);
        }

        // Self-tests for the post-drop surfaces (no drag needed): --share-test <file>, --picker-test <file>,
        // --folder-picker-test <file> (copy only, so a stray pick can never move the file).
        var shareArg = Array.IndexOf(argv, "--share-test");
        if (shareArg >= 0 && shareArg + 1 < argv.Length)
        {
            _ = Shell.ShareSheet.ShowAsync([argv[shareArg + 1]]);
        }

        var folderPickerArg = Array.IndexOf(argv, "--folder-picker-test");
        if (folderPickerArg >= 0 && folderPickerArg + 1 < argv.Length)
        {
            var payload = new Targets.DragPayload([argv[folderPickerArg + 1]], Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy);
            var target = new Targets.PickFolderTarget(() => Shell.ExplorerFolders.GetOpenFoldersAsync(TimeSpan.FromSeconds(2))) { Title = "More…" };
            _ = target.ExecuteAsync(payload, default, 0);
        }

        var pickerArg = Array.IndexOf(argv, "--picker-test");
        if (pickerArg >= 0 && pickerArg + 1 < argv.Length)
        {
            _selfTestTimer = _tray.DispatcherQueue.CreateTimer();
            _selfTestTimer.Interval = TimeSpan.FromSeconds(4);
            _selfTestTimer.IsRepeating = false;
            _selfTestTimer.Tick += (_, _) => _tray.OpenPickerForTest(argv[pickerArg + 1]);
            _selfTestTimer.Start();
        }

        // First run (or --guide): the guide opens once the tray is ready, so the practice drag can summon it.
        // --guide-state <stage> jumps to a stage for screenshots.
        var guideStateArg = Array.IndexOf(argv, "--guide-state");
        if ((!settings.GuideShown && !argv.Any(a => a.StartsWith("--", StringComparison.Ordinal))) || argv.Contains("--guide") || guideStateArg >= 0)
        {
            OpenGuide();
            if (guideStateArg >= 0 && guideStateArg + 1 < argv.Length)
            {
                _guide?.ShowForTest(argv[guideStateArg + 1]);
            }
        }

        // Opened by hand (no arguments) once the guide has been seen: show settings, so clicking the app always
        // shows something. Start with Windows passes --background and opens nothing.
        SystemIntegration.UpgradeStartupEntry();
        if (settings.GuideShown && argv.Length == 1)
        {
            OpenSettings();
        }

        if (argv.Contains("--settings"))
        {
            OpenSettings();
        }

        // Stress test for delegate lifetime bugs: collect garbage every second so a collected callback crashes at once.
        if (argv.Contains("--gc-stress"))
        {
            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(1000);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            })
            { IsBackground = true }.Start();
        }

        if (argv.Contains("--quit-test"))
        {
            _selfTestTimer = _tray.DispatcherQueue.CreateTimer();
            _selfTestTimer.Interval = TimeSpan.FromSeconds(4);
            _selfTestTimer.IsRepeating = false;
            _selfTestTimer.Tick += (_, _) => Quit();
            _selfTestTimer.Start();
        }

        if (argv.Contains("--library-test"))
        {
            _selfTestTimer = _tray.DispatcherQueue.CreateTimer();
            _selfTestTimer.Interval = TimeSpan.FromSeconds(4);
            _selfTestTimer.IsRepeating = false;
            _selfTestTimer.Tick += (_, _) => _tray.OpenLibraryForTest();
            _selfTestTimer.Start();
        }

        // Exercises the open-Explorer-folders lookup (normally only runs mid-drag): it uses late-bound COM, which is
        // the part most at risk under trimming.
        if (argv.Contains("--folders-test"))
        {
            _ = Task.Run(async () =>
            {
                var folders = await Shell.ExplorerFolders.GetOpenFoldersAsync(TimeSpan.FromSeconds(5));
                Log.Write($"folders-test: {folders.Count} open folder(s)");
            });
        }

        if (argv.Contains("--toast-test"))
        {
            Notifier.Show("Spare Hand", "Notifications are working");
        }

        if (argv.Contains("--selftest"))
        {
            // Kept in a field: an unreferenced DispatcherQueueTimer can be collected before it fires.
            _selfTestTimer = _tray.DispatcherQueue.CreateTimer();
            _selfTestTimer.Interval = TimeSpan.FromSeconds(4);
            _selfTestTimer.IsRepeating = false;
            _selfTestTimer.Tick += (_, _) => _tray.Summon(new Native.Win32.POINT { X = 900, Y = 700 });
            _selfTestTimer.Start();
        }
    }

    /// <summary>Adds or removes the notification-area icon to match the setting, live.</summary>
    private void SyncNotifyIcon()
    {
        var ui = _tray!.DispatcherQueue;
        if (_settings!.ShowNotifyIcon && _notifyIcon is null)
        {
            _notifyIcon = new Shell.NotifyIcon(() => _watcher!.Paused);
            _notifyIcon.LibraryRequested += () => ui.TryEnqueue(_tray.ToggleLibrary);
            _notifyIcon.SettingsRequested += () => ui.TryEnqueue(OpenSettings);
            _notifyIcon.GuideRequested += () => ui.TryEnqueue(OpenGuide);
            _notifyIcon.PauseToggled += () => ui.TryEnqueue(() => SetPaused(!_watcher!.Paused));
            _notifyIcon.QuitRequested += () => ui.TryEnqueue(Quit);
            _notifyIcon.Start();
        }
        else if (!_settings.ShowNotifyIcon && _notifyIcon is not null)
        {
            _notifyIcon.Remove();
            _notifyIcon = null;
        }
    }

    private void SetPaused(bool paused)
    {
        _watcher!.Paused = paused;
        Log.Write($"paused={paused}");
        _notifyIcon?.Update();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings!, () => _watcher!.Paused, SetPaused);
            MemoryTrim.Opened();
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                MemoryTrim.Closed("settings closed");
            };
        }

        _settingsWindow.Bring();
    }

    private void OpenGuide()
    {
        if (_guide is null)
        {
            _guide = new Guide.GuideWindow(_settings!, _hold!, _watcher!, _tray!);
            if (_themeOverride is { } theme)
            {
                _guide.ApplyTheme(theme);
            }

            MemoryTrim.Opened();
            _guide.Closed += (_, _) =>
            {
                _guide = null;
                MemoryTrim.Closed("guide closed");
            };
        }

        _guide.Bring();
    }

    private void Quit()
    {
        Log.Write("quit requested");
        _notifyIcon?.Remove();
        Thread.Sleep(150); // let the icon thread remove the icon before the process ends
        Environment.Exit(0);
    }
}

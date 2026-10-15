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
            // Launching again while running opens the library in the running instance (the way back in when the
            // notification-area icon is hidden).
            Log.Write("another instance is running; asking it to open the library");
            _showSignal.Set();
            Exit();
            return;
        }

        var argv = Environment.GetCommandLineArgs();
        Log.Write($"=== Spare Hand start ({string.Join(' ', argv.Skip(1))}) ===");

        var settings = _settings = Settings.Load();
        var hold = new HoldStore();
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

        var ui = _tray.DispatcherQueue;
        new Thread(() =>
        {
            while (_showSignal.WaitOne())
            {
                ui.TryEnqueue(_tray.ToggleLibrary);
            }
        })
        { IsBackground = true, Name = "SpareHand show signal" }.Start();

        SyncNotifyIcon();
        settings.Changed += () => ui.TryEnqueue(SyncNotifyIcon);

        // Self-test: open the tray at a fixed point without a drag, for screenshots. Never used in normal runs.
        var themeArg = Array.IndexOf(argv, "--theme");
        if (themeArg >= 0 && themeArg + 1 < argv.Length)
        {
            _tray.ApplyTheme(argv[themeArg + 1] == "light" ? ElementTheme.Light : ElementTheme.Dark);
        }

        // Self-tests for the post-drop surfaces (no drag needed): --share-test <file>, --picker-test <file>.
        var shareArg = Array.IndexOf(argv, "--share-test");
        if (shareArg >= 0 && shareArg + 1 < argv.Length)
        {
            _ = Shell.ShareSheet.ShowAsync([argv[shareArg + 1]]);
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

    private void Quit()
    {
        Log.Write("quit requested");
        _notifyIcon?.Remove();
        Thread.Sleep(150); // let the icon thread remove the icon before the process ends
        Environment.Exit(0);
    }
}

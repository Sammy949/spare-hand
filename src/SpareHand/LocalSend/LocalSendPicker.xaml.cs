using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpareHand.Shell;
using SpareHand.Targets;
using SpareHand.Tray;
using Windows.Foundation;
using WinUIEx;
using static SpareHand.Native.Win32;

namespace SpareHand.LocalSend;

/// <summary>
/// Drop on the LocalSend tile → this opens where the file was dropped, lists nearby devices (live), and a click
/// sends. One LocalSend tile; pick the device after the drop, like Quick Share.
/// </summary>
public sealed partial class LocalSendPicker : WindowEx
{
    private const int ShadowPad = 24;
    private const int GapDip = 16;

    private readonly LocalSendService _service;
    private readonly DispatcherQueue _ui;
    private readonly nint _hwnd;
    private DispatcherQueueTimer? _timer;
    private DragPayload? _payload;
    private POINT _anchor;
    private bool _open;
    private bool _sending;
    private CancellationTokenSource? _sendCts;

    /// <summary>Self-test only: keep the picker open when focus moves (the screenshot tool steals focus).</summary>
    internal static bool KeepOpenForTest { get; set; }

    internal LocalSendPicker(LocalSendService service)
    {
        _service = service;
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();
        _hwnd = PopupChrome.Apply(this, noActivate: false);

        if (LocalSendService.FindAppExe() is { } exe)
        {
            _ = LoadAppIconAsync(exe);
        }
        else
        {
            AppIcon.Visibility = Visibility.Collapsed;
        }

        _service.DevicesChanged += () => _ui.TryEnqueue(() =>
        {
            if (_open && !_sending)
            {
                RebuildDevices();
                UpdateIdleStatus();
                Reposition();
            }
        });

        Activated += (_, e) =>
        {
            // Clicking anywhere else dismisses, like a flyout. A running send keeps going in the background.
            if (e.WindowActivationState == WindowActivationState.Deactivated && _open && !KeepOpenForTest)
            {
                Close("deactivated");
            }
        };

        // Keep the window hidden until first use.
        AppWindow.Hide();
    }

    internal void ApplyTheme(ElementTheme theme) => Root.RequestedTheme = theme;

    /// <summary>
    /// Click outside closes, like Peek, but without depending on focus: activation isn't guaranteed (e.g. right
    /// after a drop Explorer can keep it), and an unfocused window never gets "Deactivated".
    /// </summary>
    internal void OnMouseDownAnywhere(POINT p)
    {
        if (!_open || KeepOpenForTest || !IsOverOtherProcess(p))
        {
            return;
        }

        Close("click outside");
    }

    internal void Open(DragPayload payload, POINT at)
    {
        if (!_open)
        {
            // Count only real opens: a second Open while open would leave the trim counter stuck above zero.
            MemoryTrim.Opened();
        }

        _payload = payload;
        _anchor = at;
        _open = true;
        _sending = false;
        CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        WhatText.Text = payload.Paths.Count == 1 ? Path.GetFileName(payload.Paths[0]) : $"{payload.Paths.Count} files";
        Devices.IsEnabled = true;
        Devices.Opacity = 1;
        RebuildDevices();
        SetStatus(busy: true, glyph: null, text: "Looking for devices…");
        _service.Refresh(force: true);

        // Give discovery a few seconds before saying nothing was found.
        StartTimer(TimeSpan.FromSeconds(5), UpdateIdleStatus);

        Reposition();
        AppWindow.Show(true);
        Activate();
        SetForegroundWindow(_hwnd);
        Devices.Focus(FocusState.Programmatic);
        Log.Write($"localsend picker open ({Devices.Items.Count} device(s))");
    }

    private void Close(string reason)
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        _timer?.Stop();
        AppWindow.Hide();
        Log.Write($"localsend picker closed ({reason}){(_sending ? ", send continues" : string.Empty)}");
        MemoryTrim.Closed("picker closed");
    }

    private void RebuildDevices()
    {
        Devices.Items.Clear();
        foreach (var device in _service.RecentDevices(TimeSpan.FromMinutes(10)))
        {
            Devices.Items.Add(DeviceRow(device));
        }
    }

    private void UpdateIdleStatus()
    {
        if (!_open || _sending)
        {
            return;
        }

        if (Devices.Items.Count > 0)
        {
            SetStatus(busy: false, glyph: null, text: "Pick a device. It asks before accepting.");
        }
        else if (_timer is { IsRunning: false })
        {
            SetStatus(busy: false, glyph: "", text: "No devices found. Open LocalSend on the other device, on the same Wi-Fi.");
        }
    }

    private async void OnDeviceClick(object sender, ItemClickEventArgs e)
    {
        if (_sending || _payload is null || (e.ClickedItem as FrameworkElement)?.Tag is not LocalSendDevice device)
        {
            return;
        }

        _sending = true;
        Devices.IsEnabled = false;
        Devices.Opacity = 0.5;
        SetStatus(busy: true, glyph: null, text: $"Waiting for {device.Alias} to accept…");

        _sendCts = new CancellationTokenSource();
        var outcome = await _service.SendAsync(device, _payload.Paths, _sendCts.Token);
        _sendCts.Dispose();
        _sendCts = null;
        _sending = false;

        if (outcome == SendOutcome.Cancelled)
        {
            if (_open)
            {
                SetStatus(busy: false, glyph: null, text: "Cancelled. Nothing more will be sent.");
                CancelButton.Visibility = Visibility.Collapsed;
                StartTimer(TimeSpan.FromMilliseconds(1100), () => Close("cancelled"));
            }

            return;
        }

        var (ok, text) = outcome switch
        {
            SendOutcome.Sent => (true, $"Sent to {device.Alias}"),
            SendOutcome.Declined => (false, $"{device.Alias} declined"),
            SendOutcome.Busy => (false, $"{device.Alias} is busy with another transfer"),
            SendOutcome.PinRequired => (false, $"{device.Alias} needs a PIN. That's not supported yet."),
            SendOutcome.Unreachable => (false, $"Couldn't reach {device.Alias}"),
            _ => (false, $"Sending to {device.Alias} failed"),
        };

        if (!_open)
        {
            // The picker was dismissed mid-send: report the outcome as a notification instead.
            Notifier.Show(text, WhatText.Text);
            return;
        }

        SetStatus(busy: false, glyph: ok ? "" : "", text: text, critical: !ok);
        if (ok)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            StartTimer(TimeSpan.FromMilliseconds(1400), () => Close("sent"));
        }
        else
        {
            Devices.IsEnabled = true;
            Devices.Opacity = 1;
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Cancel("escape");
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancel("cancel button");

    /// <summary>
    /// While searching: just close, nothing was sent. While waiting/uploading: stop the request and tell the receiver
    /// (the service calls LocalSend's cancel endpoint once a session exists), then show "Cancelled" and close.
    /// </summary>
    private void Cancel(string reason)
    {
        if (_sending && _sendCts is { IsCancellationRequested: false })
        {
            Log.Write($"localsend send cancelled ({reason})");
            CancelButton.IsEnabled = false;
            SetStatus(busy: true, glyph: null, text: "Cancelling…");
            _sendCts.Cancel();
            return;
        }

        Close(reason);
    }

    private void SetStatus(bool busy, string? glyph, string text, bool critical = false)
    {
        Busy.IsActive = busy;
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusGlyph.Visibility = glyph is null ? Visibility.Collapsed : Visibility.Visible;
        if (glyph is not null)
        {
            StatusGlyph.Glyph = glyph;
            StatusGlyph.Style = (Style)Application.Current.Resources[critical ? "SpareHandCriticalIcon" : "SpareHandSecondaryIcon"];
        }

        StatusText.Text = text;
        Reposition();
    }

    private static FrameworkElement DeviceRow(LocalSendDevice device)
    {
        var glyph = device.DeviceType switch
        {
            "mobile" => "",
            "web" => "",
            "headless" or "server" => "",
            _ => "",
        };
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6), Tag = device };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["SpareHandSecondaryIcon"] });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = device.Alias, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["SpareHandPrimaryText"] });
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(device.DeviceModel) ? device.DeviceType : $"{device.DeviceModel} · {device.DeviceType}",
            FontSize = 12,
            Style = (Style)Application.Current.Resources["SpareHandSecondaryText"],
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"Send to {device.Alias}");
        return row;
    }

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
        var (x, y, _) = Placement.NearCursor(_anchor, cardW, cardH, (int)Math.Round(GapDip * scale));
        SetWindowPos(_hwnd, HWND_TOPMOST, x - pad, y - pad, cardW + (2 * pad), cardH + (2 * pad), 0);
    }

    private void StartTimer(TimeSpan after, Action action)
    {
        _timer?.Stop();
        _timer = _ui.CreateTimer();
        _timer.Interval = after;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => action();
        _timer.Start();
    }

    private async Task LoadAppIconAsync(string exe)
    {
        var icon = await ShellIcons.ForPathAsync(exe, 48, iconOnly: true);
        if (icon is not null)
        {
            AppIcon.Source = icon;
            AppGlyph.Visibility = Visibility.Collapsed;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);
}

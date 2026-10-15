using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SpareHand.Targets;

namespace SpareHand.Tray;

/// <summary>Host side of a tile: the tray resolves the payload and executes drops.</summary>
internal interface ITileHost
{
    Task<DragPayload?> ResolvePayloadAsync(DragEventArgs e);

    void OnTileDropped(TrayTarget target, DragPayload payload, Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers modifiers);
}

public sealed partial class TrayTile : UserControl
{
    private readonly TrayTarget _target;
    private readonly ITileHost _host;
    private DragPayload? _payload;
    private bool _hot;

    internal TrayTile(TrayTarget target, ITileHost host)
    {
        _target = target;
        _host = host;
        InitializeComponent();
        ToolTipService.SetToolTip(this, target.Title);
        AutomationProperties.SetName(this, target.Title);
        target.PropertyChanged += OnTargetChanged;
        Unloaded += (_, _) => target.PropertyChanged -= OnTargetChanged;
        Refresh();
    }

    internal TrayTarget Target => _target;

    /// <summary>Self-test only: render the drop-hover state for a screenshot.</summary>
    internal void ShowHotForTest() => SetHot(true);

    /// <summary>The one place the visual state is decided, so a late icon load can't knock a hot tile back to Normal.</summary>
    private void SetHot(bool hot)
    {
        _hot = hot;
        VisualStateManager.GoToState(this, _hot && !_target.IsDimmed ? "Hot" : _target.IsDimmed ? "Dimmed" : "Normal", false);
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(Refresh);

    private void Refresh()
    {
        TitleText.Text = _target.Title;
        SubtitleText.Text = _target.Subtitle ?? string.Empty;
        SubtitleText.Visibility = string.IsNullOrEmpty(_target.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        IconImage.Source = _target.Icon;
        IconImage.Visibility = _target.HasIcon ? Visibility.Visible : Visibility.Collapsed;
        GlyphIcon.Glyph = _target.Glyph;
        GlyphIcon.Visibility = _target.HasIcon ? Visibility.Collapsed : Visibility.Visible;
        SetHot(_hot);
    }

    private async void OnDragEnter(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            _payload = await _host.ResolvePayloadAsync(e);
            Apply(e);
            SetHot(_payload is { Paths.Count: > 0 });
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e) => Apply(e);

    private void OnDragLeave(object sender, DragEventArgs e) => SetHot(false);

    private void OnDrop(object sender, DragEventArgs e)
    {
        SetHot(false);
        if (_payload is not { Paths.Count: > 0 } payload || _target.IsDimmed || !_target.Plan(payload, e.Modifiers).Accepts)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            return;
        }

        // Always report Copy to the source, even for moves we perform ourselves: a source that sees "Move"
        // may delete the original after the drop, racing (or doubling) our own shell move.
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        _host.OnTileDropped(_target, payload, e.Modifiers);
    }

    private void Apply(DragEventArgs e)
    {
        if (_payload is not { Paths.Count: > 0 } payload)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            e.DragUIOverride.Caption = "Spare Hand can't take this yet";
            e.DragUIOverride.IsCaptionVisible = true;
            return;
        }

        var plan = _target.Plan(payload, e.Modifiers);
        e.AcceptedOperation = plan.Accepts ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        e.DragUIOverride.Caption = plan.Caption;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = plan.ShowGlyph;
    }
}

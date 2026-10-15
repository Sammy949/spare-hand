using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;

namespace SpareHand.Targets;

internal enum TargetGroup
{
    Hold,
    Send,
    MoveTo,
}

/// <summary>What is being dragged, resolved once per drag on the first DragEnter.</summary>
internal sealed record DragPayload(IReadOnlyList<string> Paths, DataPackageOperation Allowed);

/// <summary>What a tile would do with the current drag: the effect reported to the source and the cursor caption.</summary>
internal sealed record DropPlan(bool Accepts, string Caption, bool ShowGlyph = false);

/// <summary>One tile in the tray. Subclasses decide what a drop means.</summary>
internal abstract class TrayTarget : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private string? _subtitle;
    private bool _isDimmed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public abstract TargetGroup Group { get; }

    public required string Title { get; init; }

    /// <summary>Segoe Fluent Icons glyph, used until (or instead of) a real shell icon.</summary>
    public string Glyph { get; init; } = "";

    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; Raise(nameof(Icon)); Raise(nameof(HasIcon)); }
    }

    public bool HasIcon => _icon is not null;

    public string? Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Raise(nameof(Subtitle)); }
    }

    /// <summary>Dimmed tiles refuse drops, e.g. the folder the files already live in.</summary>
    public bool IsDimmed
    {
        get => _isDimmed;
        set { _isDimmed = value; Raise(nameof(IsDimmed)); }
    }

    public abstract DropPlan Plan(DragPayload payload, DragDropModifiers modifiers);

    /// <summary>Runs after the tray has closed. Must not block; long work continues in the background.</summary>
    public abstract Task ExecuteAsync(DragPayload payload, DragDropModifiers modifiers, nint ownerHwnd);

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

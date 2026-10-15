using SpareHand.Hold;
using SpareHand.Shell;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;

namespace SpareHand.Targets;

internal sealed class HoldTarget(HoldStore store) : TrayTarget
{
    public override TargetGroup Group => TargetGroup.Hold;

    public override DropPlan Plan(DragPayload payload, DragDropModifiers modifiers) => new(true, "Hold");

    public override Task ExecuteAsync(DragPayload payload, DragDropModifiers modifiers, nint ownerHwnd)
    {
        var added = store.Add(payload.Paths);
        Log.Write($"hold: +{added}, now {store.Items.Count}");
        return Task.CompletedTask;
    }
}

internal sealed class FolderTarget : TrayTarget
{
    public required string FolderPath { get; init; }

    public override TargetGroup Group => TargetGroup.MoveTo;

    public override DropPlan Plan(DragPayload payload, DragDropModifiers modifiers)
    {
        if (IsDimmed)
        {
            return new(false, "Already here");
        }

        var move = WillMove(payload, modifiers);
        return new(true, $"{(move ? "Move" : "Copy")} to {Title}");
    }

    public override Task ExecuteAsync(DragPayload payload, DragDropModifiers modifiers, nint ownerHwnd) =>
        FileOps.TransferAsync(payload.Paths, FolderPath, WillMove(payload, modifiers));

    /// <summary>Explorer's rule: same volume moves, another volume copies; Shift forces move, Ctrl forces copy.</summary>
    private bool WillMove(DragPayload payload, DragDropModifiers modifiers)
    {
        if (!payload.Allowed.HasFlag(DataPackageOperation.Move))
        {
            return false;
        }

        if (modifiers.HasFlag(DragDropModifiers.Shift))
        {
            return true;
        }

        if (modifiers.HasFlag(DragDropModifiers.Control))
        {
            return false;
        }

        return FileOps.SameVolume(payload.Paths, FolderPath);
    }
}

internal sealed class QuickShareTarget : TrayTarget
{
    public static readonly Guid VerbClsid = new("AD54D7CA-052D-4730-B0F2-2BA34D3438DC");

    public override TargetGroup Group => TargetGroup.Send;

    public override DropPlan Plan(DragPayload payload, DragDropModifiers modifiers) => new(true, "Send with Quick Share");

    public override Task ExecuteAsync(DragPayload payload, DragDropModifiers modifiers, nint ownerHwnd) =>
        ExplorerCommand.InvokeAsync(VerbClsid, payload.Paths);
}

internal sealed class ShareSheetTarget : TrayTarget
{
    public override TargetGroup Group => TargetGroup.Send;

    public override DropPlan Plan(DragPayload payload, DragDropModifiers modifiers) => new(true, "Share…");

    public override Task ExecuteAsync(DragPayload payload, DragDropModifiers modifiers, nint ownerHwnd) =>
        ShareSheet.ShowAsync(payload.Paths);
}

/// <summary>One LocalSend tile; dropping opens the device picker (pick the device after the drop).</summary>
internal sealed class LocalSendTarget(Action<DragPayload> openPicker) : TrayTarget
{
    public override TargetGroup Group => TargetGroup.Send;

    public override DropPlan Plan(DragPayload payload, DragDropModifiers modifiers) =>
        payload.Paths.Any(Directory.Exists) ? new(false, "Folders can't be sent with LocalSend yet") : new(true, "Send with LocalSend");

    public override Task ExecuteAsync(DragPayload payload, DragDropModifiers modifiers, nint ownerHwnd)
    {
        openPicker(payload);
        return Task.CompletedTask;
    }
}

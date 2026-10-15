using SpareHand.Hold;
using SpareHand.Shell;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;

namespace SpareHand.Targets;

internal sealed partial class HoldTarget(HoldStore store) : TrayTarget
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

internal sealed partial class FolderTarget : TrayTarget
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

    private bool WillMove(DragPayload payload, DragDropModifiers modifiers) => MoveRule.WillMove(payload, modifiers, FolderPath);
}

/// <summary>"More…" under Move to: drop first, then choose any folder.</summary>
internal sealed partial class PickFolderTarget(Func<Task<IReadOnlyList<string>>> openFolders) : TrayTarget
{
    public override TargetGroup Group => TargetGroup.MoveTo;

    public override DropPlan Plan(DragPayload payload, DragDropModifiers modifiers) => new(true, "Choose a folder…");

    public override async Task ExecuteAsync(DragPayload payload, DragDropModifiers modifiers, nint ownerHwnd)
    {
        var parents = MoveRule.Parents(payload.Paths);

        // Start in the most recent Explorer window the files aren't already in: dragging between two open windows
        // stays one click.
        var suggested = (await openFolders()).FirstOrDefault(f => !parents.Contains(f));

        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(DropHost.Show()))
        {
            CommitButtonText = "Select folder",
        };
        if (suggested is not null)
        {
            picker.SuggestedFolder = suggested;
        }

        var picked = (await picker.PickSingleFolderAsync())?.Path;
        Log.Write($"folder picker: {(picked is null ? "cancelled" : "picked")}, suggested={suggested is not null}");
        if (picked is null)
        {
            return;
        }

        if (parents.Contains(picked.TrimEnd('\\')) || payload.Paths.Contains(picked, StringComparer.OrdinalIgnoreCase))
        {
            Notifier.Show("Already there", "Pick a different folder to move or copy into.");
            return;
        }

        await FileOps.TransferAsync(payload.Paths, picked, MoveRule.WillMove(payload, modifiers, picked));
    }
}

internal static class MoveRule
{
    /// <summary>Explorer's rule: same volume moves, another volume copies; Shift forces move, Ctrl forces copy.</summary>
    public static bool WillMove(DragPayload payload, DragDropModifiers modifiers, string destination)
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

        return FileOps.SameVolume(payload.Paths, destination);
    }

    /// <summary>The folders the dragged items live in: never a destination.</summary>
    public static HashSet<string> Parents(IEnumerable<string> paths) =>
        paths.Select(p => Path.GetDirectoryName(p.TrimEnd('\\'))).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
}

internal sealed partial class QuickShareTarget : TrayTarget
{
    public static readonly Guid VerbClsid = new("AD54D7CA-052D-4730-B0F2-2BA34D3438DC");

    public override TargetGroup Group => TargetGroup.Send;

    public override DropPlan Plan(DragPayload payload, DragDropModifiers modifiers) => new(true, "Send with Quick Share");

    public override Task ExecuteAsync(DragPayload payload, DragDropModifiers modifiers, nint ownerHwnd) =>
        ExplorerCommand.InvokeAsync(VerbClsid, payload.Paths);
}

/// <summary>One LocalSend tile; dropping opens the device picker (pick the device after the drop).</summary>
internal sealed partial class LocalSendTarget(Action<DragPayload> openPicker) : TrayTarget
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

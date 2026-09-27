using Argent.WebComponents.Workflows.Modeler.Undo;

namespace Argent.WebComponents.Workflows.Modeler.Interaction;

/// <summary>
/// Owns the lifecycle of a modeler gesture. A gesture begins by capturing geometry,
/// routing and selection once; every pointer position updates from that baseline; commit
/// records exactly one undo entry, and cancel restores the capture. While a gesture is
/// open the draft is not autosaved, so a half-finished drag can never overwrite saved work.
/// </summary>
public sealed class ModelerInteractionController(DesignerService state)
{
    private DesignerSnapshot? _baseline;

    public bool IsOpen => _baseline != null;
    public DesignerSnapshot Baseline => _baseline ?? throw new InvalidOperationException("No gesture is open.");

    /// <summary>Starts a gesture and suspends autosave until it is committed or cancelled.</summary>
    public void Begin()
    {
        if (_baseline != null) CommitSilently();
        _baseline = DesignerSnapshot.Capture(state);
        state.SuspendAutoSave();
    }

    /// <summary>Records that this pointer position changed the canvas.</summary>
    public void Changed() => state.Notify();

    /// <summary>
    /// Ends the gesture. One completed gesture becomes one undo entry; a click that moved
    /// nothing produces neither an entry nor a dirty draft.
    /// </summary>
    public void Commit(string description)
    {
        if (_baseline == null) return;
        var after = DesignerSnapshot.Capture(state);
        var before = _baseline;
        Close();

        if (before.DiffersFrom(after))
        {
            state.History.Push(new RestoreSnapshotCommand(before, after, description));
            state.MarkDirty();
        }
    }

    /// <summary>Ends the gesture and restores geometry, routing and selection exactly.</summary>
    public void Cancel()
    {
        if (_baseline == null) return;
        var baseline = _baseline;
        Close();
        baseline.Restore(state);
    }

    /// <summary>Closes a gesture without keeping its result, for a gesture that is replaced.</summary>
    public void CommitSilently()
    {
        if (_baseline == null) return;
        var after = DesignerSnapshot.Capture(state);
        var before = _baseline;
        Close();
        if (before.DiffersFrom(after))
        {
            state.History.Push(new RestoreSnapshotCommand(before, after, "Edit"));
            state.MarkDirty();
        }
    }

    /// <summary>Discards a gesture and the geometry it produced, without an undo entry.</summary>
    public void Discard()
    {
        if (_baseline == null) return;
        var baseline = _baseline;
        Close();
        baseline.Restore(state);
    }

    private void Close()
    {
        _baseline = null;
        state.ResumeAutoSave();
    }
}

using Argent.WebComponents.Workflows.Modeler.Interaction;

namespace Argent.WebComponents.Workflows.Modeler.Undo;

/// <summary>One reversible designer operation. A completed gesture is exactly one command.</summary>
public interface IUndoableCommand
{
    string Description { get; }
    void Undo(DesignerService state);
    void Redo(DesignerService state);
}

/// <summary>
/// Undoes and redoes a canvas state change by restoring a snapshot taken before and after
/// the operation. Covers geometry, routing, structural edits and selection together, which
/// is what makes a cancelled or reversed gesture return the user to exactly where they were.
/// </summary>
public sealed class RestoreSnapshotCommand(DesignerSnapshot before, DesignerSnapshot after, string description)
    : IUndoableCommand
{
    public string Description { get; } = description;

    public void Undo(DesignerService state) => before.Restore(state);
    public void Redo(DesignerService state) => after.Restore(state);
}

/// <summary>
/// Undoes and redoes a change to a workflow element property. Consecutive edits to the same
/// property collapse into one entry so typing in a text field is a single undo step.
/// </summary>
public sealed class PropertyChangeCommand : IUndoableCommand
{
    private readonly Entry[] _entries;

    public PropertyChangeCommand(string description, params Entry[] entries)
    {
        Description = description;
        _entries = entries;
    }

    public string Description { get; }

    public object Target => _entries[0].Target;
    public string Property => _entries[0].Info.Name;

    public bool CanMergeWith(IUndoableCommand other) =>
        other is PropertyChangeCommand merge &&
        merge.Property == Property &&
        ReferenceEquals(merge.Target, Target);

    public void MergeFrom(PropertyChangeCommand newer)
    {
        for (int i = 0; i < _entries.Length && i < newer._entries.Length; i++)
            _entries[i].NewValue = newer._entries[i].NewValue;
    }

    public void Undo(DesignerService state)
    {
        foreach (var entry in _entries) entry.Info.SetValue(entry.Target, entry.OldValue);
        state.Notify();
    }

    public void Redo(DesignerService state)
    {
        foreach (var entry in _entries) entry.Info.SetValue(entry.Target, entry.NewValue);
        state.Notify();
    }

    public sealed class Entry(object target, System.Reflection.PropertyInfo property, object? oldValue, object? newValue)
    {
        public object Target { get; } = target;
        public System.Reflection.PropertyInfo Info { get; } = property;
        public object? OldValue { get; } = oldValue;
        public object? NewValue { get; set; } = newValue;
    }
}

namespace Argent.WebComponents.Workflows.Modeler.Undo;

/// <summary>
/// The modeler-wide undo/redo stack. Every committed gesture, structural edit and property
/// change lands here as a single command; intermediate pointer positions never do.
/// </summary>
public sealed class DesignerHistory
{
    private const int MaxEntries = 200;

    private readonly List<IUndoableCommand> _undo = [];
    private readonly List<IUndoableCommand> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;
    public string? RedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    public void Push(IUndoableCommand command)
    {
        _redo.Clear();

        // Rapid edits of the same property (typing in a field) are one user action.
        if (command is PropertyChangeCommand property && _undo.Count > 0 &&
            _undo[^1] is PropertyChangeCommand previous && previous.CanMergeWith(property))
        {
            previous.MergeFrom(property);
            return;
        }

        _undo.Add(command);
        if (_undo.Count > MaxEntries) _undo.RemoveAt(0);
    }

    public IUndoableCommand? Undo()
    {
        if (_undo.Count == 0) return null;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(command);
        return command;
    }

    public IUndoableCommand? Redo()
    {
        if (_redo.Count == 0) return null;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(command);
        return command;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

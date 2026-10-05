namespace TotalWarModTool.UndoRedo;

/// <summary>
/// The global undo/redo history of the program. One instance lives until the App is closed and
/// has no upper limit. Every state of the stacks is final before <see cref="Changed"/> is raised, so
/// a throwing handler never leaves the history half updated.
/// </summary>
public sealed class ActionHistory
{
    private readonly List<IUndoableAction> _undoStack = new();
    private readonly List<IUndoableAction> _redoStack = new();

    /// <summary>
    /// Raised after <see cref="Execute"/>, <see cref="Record"/>, <see cref="Undo"/>, <see cref="Redo"/>,
    /// <see cref="Clear"/> and <see cref="RemoveWhere"/> (the latter only when a step was removed).
    /// </summary>
    public event Action? Changed;

    /// <summary>True when there is a step to undo.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>True when there is a step to redo.</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>The step that the next <see cref="Undo"/> reverts, or null when the undo stack is empty.</summary>
    public IUndoableAction? NextUndo => CanUndo ? _undoStack[^1] : null;

    /// <summary>The step that the next <see cref="Redo"/> repeats, or null when the redo stack is empty.</summary>
    public IUndoableAction? NextRedo => CanRedo ? _redoStack[^1] : null;

    /// <summary>
    /// Runs the action (<see cref="IUndoableAction.Do"/>) and records it. When <c>Do</c> throws, the
    /// step is not recorded and the exception goes to the caller.
    /// </summary>
    public void Execute(IUndoableAction action)
    {
        action.Do();
        Record(action);
    }

    /// <summary>
    /// Records an already performed action without calling <see cref="IUndoableAction.Do"/>. The redo
    /// stack is cleared, because the history continues from this step.
    /// </summary>
    public void Record(IUndoableAction action)
    {
        _undoStack.Add(action);
        _redoStack.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Reverts the latest step and returns it. When the step's <c>Undo</c> throws, the step is dropped
    /// from both stacks, <see cref="Changed"/> is raised and the exception goes to the caller.
    /// </summary>
    /// <exception cref="InvalidOperationException">The undo stack is empty.</exception>
    public IUndoableAction Undo()
    {
        if (!CanUndo)
        {
            throw new InvalidOperationException("There is nothing to undo.");
        }

        var action = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        var succeeded = false;
        try
        {
            action.Undo();
            succeeded = true;
        }
        finally
        {
            if (succeeded)
            {
                _redoStack.Add(action);
            }

            Changed?.Invoke();
        }

        return action;
    }

    /// <summary>
    /// Repeats the latest undone step and returns it. When the step's <c>Do</c> throws, the step is
    /// dropped from both stacks, <see cref="Changed"/> is raised and the exception goes to the caller.
    /// </summary>
    /// <exception cref="InvalidOperationException">The redo stack is empty.</exception>
    public IUndoableAction Redo()
    {
        if (!CanRedo)
        {
            throw new InvalidOperationException("There is nothing to redo.");
        }

        var action = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        var succeeded = false;
        try
        {
            action.Do();
            succeeded = true;
        }
        finally
        {
            if (succeeded)
            {
                _undoStack.Add(action);
            }

            Changed?.Invoke();
        }

        return action;
    }

    /// <summary>Empties both stacks.</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Removes every matching step from both stacks; the order of the remaining steps is kept.
    /// Returns the number of removed steps. <see cref="Changed"/> is raised only when something was removed.
    /// </summary>
    public int RemoveWhere(Func<IUndoableAction, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var removed = _undoStack.RemoveAll(a => match(a)) + _redoStack.RemoveAll(a => match(a));
        if (removed > 0)
        {
            Changed?.Invoke();
        }

        return removed;
    }
}

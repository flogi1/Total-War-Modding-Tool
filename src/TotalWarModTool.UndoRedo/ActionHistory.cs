using System.Runtime.ExceptionServices;

namespace TotalWarModTool.UndoRedo;

/// <summary>
/// The global undo/redo history of the program. One instance lives until the App is closed and
/// has no upper limit. Every state of the stacks is final before <see cref="Changed"/> is raised, so
/// a throwing handler never leaves the history half updated.
/// </summary>
/// <remarks>
/// Actions must not call back into the history from <see cref="IUndoableAction.Do"/> or
/// <see cref="IUndoableAction.Undo"/> (the stacks are in the middle of a step then).
/// <see cref="Changed"/> handlers may call back, because the state is final when they run.
/// </remarks>
public sealed class ActionHistory
{
    private readonly List<IUndoableAction> _undoStack = new();
    private readonly List<IUndoableAction> _redoStack = new();

    /// <summary>
    /// Raised after <see cref="Execute"/>, <see cref="Record"/>, <see cref="Undo"/>, <see cref="Redo"/>,
    /// <see cref="Clear"/> and <see cref="RemoveWhere"/> (the latter only when a step was removed).
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised by <see cref="Execute"/> and <see cref="Record"/> when the new step removed steps from the redo stack, with those steps
    /// (the one that was next to redo first). Not raised when the redo stack was empty. The stacks are final when it is raised, and it
    /// comes before <see cref="Changed"/>. Steps that <see cref="Clear"/>, <see cref="RemoveWhere"/> or a failing step drop are not reported.
    /// </summary>
    public event Action<IReadOnlyList<IUndoableAction>>? RedoDiscarded;

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
        ArgumentNullException.ThrowIfNull(action);

        action.Do();
        Record(action);
    }

    /// <summary>
    /// Records an already performed action without calling <see cref="IUndoableAction.Do"/>. The redo
    /// stack is cleared, because the history continues from this step; the steps it held are reported by <see cref="RedoDiscarded"/>.
    /// </summary>
    public void Record(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Top of the stack first: the step that was next to redo.
        var discarded = _redoStack.Count > 0 ? _redoStack.AsEnumerable().Reverse().ToList() : null;
        _undoStack.Add(action);
        _redoStack.Clear();
        if (discarded is not null)
        {
            RedoDiscarded?.Invoke(discarded);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Reverts the latest step and returns it. When the step's <c>Undo</c> throws, the step is dropped
    /// from both stacks, <see cref="Changed"/> is raised and the exception of the step goes to the caller.
    /// </summary>
    /// <exception cref="InvalidOperationException">The undo stack is empty.</exception>
    public IUndoableAction Undo()
    {
        if (!CanUndo)
        {
            throw new InvalidOperationException("There is nothing to undo.");
        }

        return MoveStep(_undoStack, _redoStack, a => a.Undo());
    }

    /// <summary>
    /// Repeats the latest undone step and returns it. When the step's <c>Do</c> throws, the step is
    /// dropped from both stacks, <see cref="Changed"/> is raised and the exception of the step goes to the caller.
    /// </summary>
    /// <exception cref="InvalidOperationException">The redo stack is empty.</exception>
    public IUndoableAction Redo()
    {
        if (!CanRedo)
        {
            throw new InvalidOperationException("There is nothing to redo.");
        }

        return MoveStep(_redoStack, _undoStack, a => a.Do());
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
    /// <paramref name="match"/> is evaluated for every step first: when it throws, both stacks stay unchanged.
    /// </summary>
    public int RemoveWhere(Func<IUndoableAction, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var undoKeep = _undoStack.Where(a => !match(a)).ToList();
        var redoKeep = _redoStack.Where(a => !match(a)).ToList();
        var removed = (_undoStack.Count - undoKeep.Count) + (_redoStack.Count - redoKeep.Count);
        if (removed == 0)
        {
            return 0;
        }

        _undoStack.Clear();
        _undoStack.AddRange(undoKeep);
        _redoStack.Clear();
        _redoStack.AddRange(redoKeep);
        Changed?.Invoke();
        return removed;
    }

    /// <summary>
    /// Takes the top step of <paramref name="source"/>, performs it and puts it on <paramref name="target"/>.
    /// When the step throws, it is dropped, <see cref="Changed"/> is raised and the step's own exception
    /// is rethrown; an exception of a <see cref="Changed"/> handler is dropped in that one case.
    /// </summary>
    private IUndoableAction MoveStep(
        List<IUndoableAction> source,
        List<IUndoableAction> target,
        Action<IUndoableAction> perform)
    {
        var action = source[^1];
        source.RemoveAt(source.Count - 1);

        ExceptionDispatchInfo? failure = null;
        try
        {
            perform(action);
            target.Add(action);
        }
        catch (Exception ex)
        {
            // Not swallowed: captured here and rethrown below, after Changed was raised.
            failure = ExceptionDispatchInfo.Capture(ex);
        }

        if (failure is null)
        {
            Changed?.Invoke();
            return action;
        }

        try
        {
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // The caller must see the original failure of the step, not the one of the handler.
        }

        failure.Throw();
        return action;
    }
}

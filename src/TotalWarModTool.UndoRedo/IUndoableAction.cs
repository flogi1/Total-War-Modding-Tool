namespace TotalWarModTool.UndoRedo;

/// <summary>
/// One undoable step of the program. Every change that goes through the global
/// <see cref="ActionHistory"/> implements this interface.
/// </summary>
public interface IUndoableAction
{
    /// <summary>A technical English description of the step. The App does not show it in this plan.</summary>
    string Description { get; }

    /// <summary>Performs the step. Called for the first execution and again for every redo.</summary>
    void Do();

    /// <summary>Reverts the step and restores the state from before <see cref="Do"/>.</summary>
    void Undo();
}

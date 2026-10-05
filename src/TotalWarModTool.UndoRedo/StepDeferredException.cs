namespace TotalWarModTool.UndoRedo;

/// <summary>
/// Thrown by <see cref="IUndoableAction.Do"/> or <see cref="IUndoableAction.Undo"/> when the step could not run for a reason that may pass by
/// itself (a file that another program holds, a folder that cannot be reached right now) and nothing was changed. <see cref="ActionHistory"/>
/// keeps such a step where it was, so the user can try again; every other exception drops the step.
/// </summary>
public sealed class StepDeferredException : Exception
{
    public StepDeferredException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

using System.Windows;

namespace TotalWarModTool.App;

/// <summary>Chooses the owner of the crash dialog.</summary>
public static class CrashDialogOwner
{
    /// <summary>
    /// The active window, except a <see cref="ProgressWindow"/>: during an operation that is the active window, but the runner closes it when
    /// the work ends, and a dialog it owns would be closed with it. In that case, and when no window is active, the main window is the owner,
    /// provided it was shown (a window that was never shown cannot be an owner, and a failed start-up can leave exactly that behind).
    /// Otherwise there is no owner.
    /// </summary>
    public static Window? Choose(Window? active, Window? mainWindow)
    {
        if (active is not null and not ProgressWindow)
        {
            return active;
        }

        return mainWindow is { IsVisible: true } and not ProgressWindow ? mainWindow : null;
    }
}

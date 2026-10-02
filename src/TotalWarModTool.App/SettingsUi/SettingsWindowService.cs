using System.Windows;

namespace TotalWarModTool.App;

/// <summary>Opens the settings window as a non-modal tool window, and only once: another request activates the window that is open.</summary>
/// <param name="create">Creates a new settings window (owner and view model are set by the composition root). Called only when none is open.</param>
public sealed class SettingsWindowService(Func<SettingsWindow> create)
{
    private SettingsWindow? _window;

    public void ShowOrActivate()
    {
        if (_window is { } open)
        {
            // A minimized window would be activated without coming back on screen.
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        var window = create();
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
        _window = window;
        window.Show();
    }
}

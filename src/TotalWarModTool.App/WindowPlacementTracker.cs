using System.Windows;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>
/// Keeps a window where the user left it: applies the saved placement when the window is created, and stores the placement
/// (restore bounds plus maximized state) into the settings when the window closes. A saved placement that is no longer usable
/// (monitor removed, too small) is ignored and the window keeps its XAML position.
/// </summary>
public sealed class WindowPlacementTracker
{
    private readonly Window _window;
    private readonly GlobalSettings _settings;

    public WindowPlacementTracker(Window window, GlobalSettings settings)
    {
        _window = window;
        _settings = settings;
        window.SourceInitialized += (_, _) => Apply();
        window.Closing += (_, _) => Store();
    }

    private void Apply()
    {
        var saved = _settings.MainWindow;
        if (saved is null || !WindowBoundsCheck.IsUsable(
                saved,
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight,
                _window.MinWidth,
                _window.MinHeight))
        {
            return;
        }

        // Without this the XAML start location (centred) would place the window again after the saved position.
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Left = saved.Left;
        _window.Top = saved.Top;
        _window.Width = saved.Width;
        _window.Height = saved.Height;
        if (saved.Maximized)
        {
            _window.WindowState = WindowState.Maximized;
        }
    }

    private void Store()
    {
        // RestoreBounds is the normal-state rectangle, also while the window is maximized or minimized.
        var bounds = _window.RestoreBounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        _settings.MainWindow = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, _window.WindowState == WindowState.Maximized);
    }
}

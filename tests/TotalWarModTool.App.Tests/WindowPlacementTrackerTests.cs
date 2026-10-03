using System.Windows;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class WindowPlacementTrackerTests
{
    private const double Tolerance = 1;

    // A window with a minimum size, started centred like the main window.
    private static Window NewWindow() => new()
    {
        Width = 640,
        Height = 480,
        MinWidth = 300,
        MinHeight = 200,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        ShowActivated = false,
    };

    [Fact] public void A_usable_saved_placement_is_applied_to_the_window() => Sta.Run(() =>
    {
        var left = SystemParameters.VirtualScreenLeft + 50;
        var top = SystemParameters.VirtualScreenTop + 60;
        var settings = new GlobalSettings { MainWindow = new WindowPlacement(left, top, 700, 500, false) };
        var window = NewWindow();
        _ = new WindowPlacementTracker(window, settings);

        window.Show();

        // WPF snaps a window to whole device pixels, so a scaled display shifts the values by a fraction.
        Assert.Equal(left, window.Left, Tolerance);
        Assert.Equal(top, window.Top, Tolerance);
        Assert.Equal(700d, window.Width, Tolerance);
        Assert.Equal(500d, window.Height, Tolerance);
        window.Close();
    });

    [Fact] public void An_unusable_saved_placement_is_ignored() => Sta.Run(() =>
    {
        // Far to the right of and below the virtual screen: no part of the title bar is reachable.
        var farLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 5000;
        var farTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 5000;
        var settings = new GlobalSettings { MainWindow = new WindowPlacement(farLeft, farTop, 700, 500, false) };
        var window = NewWindow();
        _ = new WindowPlacementTracker(window, settings);

        window.Show();

        // The window keeps its default size and start location instead of the saved ones.
        Assert.Equal((640d, 480d), (window.Width, window.Height));
        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        Assert.True(window.Left < farLeft && window.Top < farTop);
        window.Close();
    });

    [Fact] public void Moving_the_window_writes_the_placement_into_the_settings() => Sta.Run(() =>
    {
        var settings = new GlobalSettings();
        var window = NewWindow();
        _ = new WindowPlacementTracker(window, settings);
        window.Show();

        var left = SystemParameters.VirtualScreenLeft + 120;
        var top = SystemParameters.VirtualScreenTop + 130;
        window.Left = left;
        window.Top = top;
        window.Close();

        // The tracker stores the placement when the window closes.
        var stored = Assert.IsType<WindowPlacement>(settings.MainWindow);
        Assert.Equal(left, stored.Left, Tolerance);
        Assert.Equal(top, stored.Top, Tolerance);
        Assert.False(stored.Maximized);
    });
}

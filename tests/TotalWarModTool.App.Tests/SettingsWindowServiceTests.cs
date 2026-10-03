using System.Windows;
using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class SettingsWindowServiceTests
{
    private static (SettingsWindowService Service, List<SettingsWindow> Created) NewService()
    {
        var texts = new AppTexts(new UiText(@"X:\none"));
        var viewModel = SettingsViewModelTests.NewViewModel(new GlobalSettings(), texts);
        var created = new List<SettingsWindow>();
        var service = new SettingsWindowService(() =>
        {
            var window = new SettingsWindow(viewModel);
            created.Add(window);
            return window;
        });
        return (service, created);
    }

    // Also shows that the window is not modal: both calls return while the window is open, which a ShowDialog would not do (the test would
    // then end with the TimeoutException of Sta.Run).
    [Fact] public void Second_call_activates_the_open_window_instead_of_creating_another() => Sta.Run(() =>
    {
        var (service, created) = NewService();

        service.ShowOrActivate();
        service.ShowOrActivate();

        var window = Assert.Single(created);
        Assert.True(window.IsVisible);
    });

    [Fact] public void After_the_window_was_closed_the_next_call_creates_a_new_one() => Sta.Run(() =>
    {
        var (service, created) = NewService();
        service.ShowOrActivate();
        created[0].Close();

        service.ShowOrActivate();

        Assert.Equal(2, created.Count);
        Assert.False(created[0].IsVisible);
        Assert.True(created[1].IsVisible);
    });

    [Fact] public void Second_call_restores_a_minimized_window() => Sta.Run(() =>
    {
        var (service, created) = NewService();
        service.ShowOrActivate();
        created[0].WindowState = WindowState.Minimized;

        service.ShowOrActivate();

        Assert.Equal((1, WindowState.Normal), (created.Count, created[0].WindowState));
    });
}

using System.Windows;
using TotalWarModTool.Localization;

namespace TotalWarModTool.App.Tests;

public class CrashDialogOwnerTests
{
    private static ProgressWindow NewProgressWindow() =>
        new("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => { });

    private static Window NewShownWindow()
    {
        var window = new Window { ShowActivated = false };
        window.Show();
        return window;
    }

    // During an operation the active window is the progress window. The runner closes it when the work ends, and a dialog it owns would be
    // closed with it, so the crash dialog would vanish unseen.
    [Fact] public void A_progress_window_is_never_the_owner_the_main_window_is() => Sta.Run(() =>
    {
        var main = NewShownWindow();
        Assert.Same(main, CrashDialogOwner.Choose(NewProgressWindow(), main));
    });

    [Fact] public void No_owner_when_the_active_window_is_a_progress_window_and_there_is_no_main_window() => Sta.Run(() =>
        Assert.Null(CrashDialogOwner.Choose(NewProgressWindow(), null)));

    [Fact] public void Another_active_window_is_the_owner() => Sta.Run(() =>
    {
        var main = NewShownWindow(); var settings = NewShownWindow();
        Assert.Same(settings, CrashDialogOwner.Choose(settings, main));
    });

    [Fact] public void The_main_window_is_the_owner_when_the_app_has_no_active_window() => Sta.Run(() =>
    {
        var main = NewShownWindow();
        Assert.Same(main, CrashDialogOwner.Choose(null, main));
    });

    // A start-up failure can leave a main window behind that was constructed but never shown; a window that was never shown cannot be an owner.
    [Fact] public void A_main_window_that_was_never_shown_is_not_an_owner() => Sta.Run(() =>
    {
        Assert.Null(CrashDialogOwner.Choose(null, new Window()));
        Assert.Null(CrashDialogOwner.Choose(NewProgressWindow(), new Window()));
    });

    [Fact] public void No_owner_without_windows() => Assert.Null(CrashDialogOwner.Choose(null, null));
}

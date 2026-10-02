using System.Windows;
using TotalWarModTool.Localization;

namespace TotalWarModTool.App.Tests;

public class MainViewModelTests
{
    private static readonly AppPaths Paths = new(@"X:\roaming", @"X:\local", @"X:\program");

    private static AppTexts NewTexts() => new(new UiText(@"X:\none"));

    // The runner only needs the windows to lock; a plain Window is enough. Created on the STA thread of the test, like the runner's windows.
    private static (MainViewModel Vm, Window Main, List<(string Title, Exception Error)> Reported, List<ProgressWindow> Shown) CreateWithRunner(TimeSpan showDelay)
    {
        var main = new Window();
        var reported = new List<(string, Exception)>();
        var shown = new List<ProgressWindow>();
        var texts = NewTexts();
        var runner = new ProgressRunner(() => [main], texts, new FakeAppLog(), TimeProvider.System, (title, ex) => reported.Add((title, ex)), showDelay);
        runner.WindowShown += (_, w) => shown.Add(w);
        return (new MainViewModel(texts, Paths, _ => { }, runner), main, reported, shown);
    }

    // Waits for the effect, no fixed sleep. The Sta.RunAsync timeout is the outer guard.
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 8000;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected state was not reached in time.");
    }

    [Fact] public void OpenLogFolder_opens_the_log_directory()
    {
        string? opened = null;
        var vm = new MainViewModel(NewTexts(), Paths, p => opened = p, NoRunner());
        vm.OpenLogFolderCommand.Execute(null);
        Assert.Equal(@"X:\local\logs", opened);
    }

    [Fact] public void DebugThrow_throws_the_test_exception()
    {
        var vm = new MainViewModel(NewTexts(), Paths, _ => { }, NoRunner());
        var ex = Assert.Throws<InvalidOperationException>(() => vm.DebugThrowCommand.Execute(null));
        Assert.Equal("Debug test exception", ex.Message);
    }

    // The run goes through the progress runner: the window is locked at once and the failure is reported with the operation title and the reason.
    [Fact] public void DebugFail_runs_through_the_runner_and_reports_the_failure() => Sta.RunAsync(async () =>
    {
        var (vm, main, reported, shown) = CreateWithRunner(TimeSpan.FromSeconds(30));
        vm.DebugFailCommand.Execute(null);
        Assert.False(main.IsHitTestVisible);

        await WaitUntil(() => reported.Count > 0);
        var (title, error) = Assert.Single(reported);
        Assert.Equal(("Run failing operation", "Debug failure"), (title, error.Message));
        Assert.IsType<IOException>(error);
        await WaitUntil(() => main.IsHitTestVisible);
        Assert.Empty(shown);
    });

    // Progress arrives as "n / 100", and Cancel ends the run quickly and without an error report.
    [Fact] public void DebugSlow_reports_progress_and_ends_quickly_on_cancel() => Sta.RunAsync(async () =>
    {
        var (vm, main, reported, shown) = CreateWithRunner(TimeSpan.FromMilliseconds(10));
        vm.DebugSlowCommand.Execute(null);
        Assert.False(main.IsHitTestVisible);

        await WaitUntil(() => shown.Count > 0 && shown[0].CountText.EndsWith(" / 100", StringComparison.Ordinal));
        var window = Assert.Single(shown);
        Assert.Equal("Run slow operation", window.Title);

        window.RequestCancel();
        await WaitUntil(() => main.IsHitTestVisible);
        Assert.Empty(reported);
        Assert.False(window.IsVisible);
    });

    // For the commands that never start a run.
    private static ProgressRunner NoRunner() =>
        new(() => [], NewTexts(), new FakeAppLog(), TimeProvider.System, (_, _) => { });
}

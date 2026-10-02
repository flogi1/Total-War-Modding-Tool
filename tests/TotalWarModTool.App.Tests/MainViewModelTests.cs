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
        return (new MainViewModel(texts, Paths, _ => { }, runner, () => { }), main, reported, shown);
    }

    // Waits for the effect, no fixed sleep. The Sta.RunAsync timeout is the outer guard.
    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected state was not reached in time.");
    }

    [Fact] public void OpenLogFolder_opens_the_log_directory()
    {
        string? opened = null;
        var vm = new MainViewModel(NewTexts(), Paths, p => opened = p, NoRunner(), () => { });
        vm.OpenLogFolderCommand.Execute(null);
        Assert.Equal(@"X:\local\logs", opened);
    }

    [Fact] public void OpenSettings_calls_the_delegate()
    {
        var opened = 0;
        var vm = new MainViewModel(NewTexts(), Paths, _ => { }, NoRunner(), () => opened++);
        vm.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, opened);
    }

    [Fact] public void DebugThrow_throws_the_test_exception()
    {
        var vm = new MainViewModel(NewTexts(), Paths, _ => { }, NoRunner(), () => { });
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

    // Progress arrives as "n / 100", and Cancel ends the run quickly and without an error report. "Quickly" has to be clearly below the five
    // seconds the work takes on its own: if the work ignored the token, the lock would only be released after about five seconds.
    [Fact] public void DebugSlow_reports_progress_and_ends_quickly_on_cancel() => Sta.RunAsync(async () =>
    {
        var (vm, main, reported, shown) = CreateWithRunner(TimeSpan.FromMilliseconds(10));
        vm.DebugSlowCommand.Execute(null);
        Assert.False(main.IsHitTestVisible);

        await WaitUntil(() => shown.Count > 0 && shown[0].CountText.EndsWith(" / 100", StringComparison.Ordinal));
        var window = Assert.Single(shown);
        Assert.Equal("Run slow operation", window.Title);

        var cancelledAt = Environment.TickCount64;
        window.RequestCancel();
        await WaitUntil(() => main.IsHitTestVisible, timeoutMs: 2000);
        var endedAfterMs = Environment.TickCount64 - cancelledAt;

        Assert.True(endedAfterMs < 2000, $"The run took {endedAfterMs} ms to end after Cancel; the work does not honour the token.");
        var lastCount = int.Parse(window.CountText.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(lastCount, 1, 99);
        Assert.Empty(reported);
        Assert.False(window.IsVisible);
    });

    // For the commands that never start a run.
    private static ProgressRunner NoRunner() =>
        new(() => [], NewTexts(), new FakeAppLog(), TimeProvider.System, (_, _) => { });
}

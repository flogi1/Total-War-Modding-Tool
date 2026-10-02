using System.Windows;
using TotalWarModTool.Localization;

namespace TotalWarModTool.App.Tests;

public class ProgressRunnerTests
{
    private static readonly TimeSpan Never = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Soon = TimeSpan.FromMilliseconds(10);

    private static (ProgressRunner Runner, FakeAppLog Log, List<(string Title, Exception Error)> Reported, List<ProgressWindow> Shown)
        Create(TimeSpan delay, params Window[] windows)
    {
        var log = new FakeAppLog(); var reported = new List<(string, Exception)>(); var shown = new List<ProgressWindow>();
        var runner = new ProgressRunner(() => windows, new AppTexts(new UiText(@"X:\none")), log, TimeProvider.System,
            (title, ex) => reported.Add((title, ex)), delay);
        runner.WindowShown += (_, w) => shown.Add(w);
        return (runner, log, reported, shown);
    }

    [Fact] public void Fast_operation_never_creates_a_window() => Sta.RunAsync(async () =>
    {
        var (runner, _, reported, shown) = Create(Never);
        var outcome = await runner.RunAsync("t", (_, _) => 42);
        Assert.Equal((OperationStatus.Completed, 42), (outcome.Status, outcome.Value));
        Assert.Empty(shown); Assert.Empty(reported);
    });

    [Fact] public void Slow_operation_shows_window_and_closes_it_when_done() => Sta.RunAsync(async () =>
    {
        var (runner, _, _, shown) = Create(Soon);
        using var gate = new ManualResetEventSlim();
        runner.WindowShown += (_, _) => gate.Set();
        var outcome = await runner.RunAsync("t", (_, _) => { gate.Wait(); return "done"; });
        Assert.Equal((OperationStatus.Completed, "done"), (outcome.Status, outcome.Value));
        Assert.Single(shown); Assert.False(shown[0].IsVisible);
    });

    [Fact] public void Exception_before_delay_reports_error_without_window() => Sta.RunAsync(async () =>
    {
        var (runner, log, reported, shown) = Create(Never);
        var outcome = await runner.RunAsync<int>("load", (_, _) => throw new InvalidDataException("bad"));
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.IsType<InvalidDataException>(outcome.Error);
        Assert.Empty(shown);
        Assert.Equal("load", Assert.Single(reported).Title); Assert.Single(log.Errors);
    });

    [Fact] public void Exception_after_window_shown_closes_window_and_reports_error() => Sta.RunAsync(async () =>
    {
        var (runner, _, reported, shown) = Create(Soon);
        using var gate = new ManualResetEventSlim();
        runner.WindowShown += (_, _) => gate.Set();
        var outcome = await runner.RunAsync<int>("load", (_, _) => { gate.Wait(); throw new InvalidDataException("bad"); });
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.False(Assert.Single(shown).IsVisible); Assert.Single(reported);
    });

    [Fact] public void Cancel_keeps_partial_result() => Sta.RunAsync(async () =>
    {
        var (runner, _, reported, _) = Create(Soon);
        runner.WindowShown += (_, w) => w.RequestCancel();
        var outcome = await runner.RunAsync("t", (_, ct) => { ct.WaitHandle.WaitOne(); return 7; });
        Assert.Equal((OperationStatus.Cancelled, 7), (outcome.Status, outcome.Value));
        Assert.Empty(reported);
    });

    [Fact] public void Cancel_by_exception_is_not_an_error() => Sta.RunAsync(async () =>
    {
        var (runner, log, reported, _) = Create(Soon);
        runner.WindowShown += (_, w) => w.RequestCancel();
        var outcome = await runner.RunAsync<int>("t", (_, ct) => { ct.WaitHandle.WaitOne(); ct.ThrowIfCancellationRequested(); return 0; });
        Assert.Equal(OperationStatus.Cancelled, outcome.Status);
        Assert.Empty(reported); Assert.Empty(log.Errors);
    });

    [Fact] public void Closing_the_window_requests_cancel_and_window_stays_until_work_ends() => Sta.RunAsync(async () =>
    {
        var (runner, _, _, shown) = Create(Soon);
        var stillVisibleAfterClose = false;
        runner.WindowShown += (_, w) => { w.Close(); stillVisibleAfterClose = w.IsVisible; };
        var outcome = await runner.RunAsync("t", (_, ct) => { ct.WaitHandle.WaitOne(); return 1; });
        Assert.True(stillVisibleAfterClose);
        Assert.Equal(OperationStatus.Cancelled, outcome.Status);
        Assert.False(shown[0].IsVisible);
    });

    [Fact] public void Input_is_locked_while_running_and_restored_in_every_case() => Sta.RunAsync(async () =>
    {
        var main = new Window();
        var (runner, _, _, _) = Create(Soon, main);
        using var gate = new ManualResetEventSlim();
        bool? hitTestWhileRunning = null;
        runner.WindowShown += (_, _) => { hitTestWhileRunning = main.IsHitTestVisible; gate.Set(); };
        await runner.RunAsync("slow", (_, _) => { gate.Wait(); return 0; });
        Assert.False(hitTestWhileRunning); Assert.True(main.IsHitTestVisible);
        await runner.RunAsync<int>("fail", (_, _) => throw new IOException("x"));
        Assert.True(main.IsHitTestVisible);
        await runner.RunAsync("fast", (_, _) => 0);
        Assert.True(main.IsHitTestVisible);
    });

    [Fact] public void Second_run_while_one_is_active_throws() => Sta.RunAsync(async () =>
    {
        var (runner, _, _, _) = Create(Never);
        using var gate = new ManualResetEventSlim();
        var first = runner.RunAsync("a", (_, _) => { gate.Wait(); return 0; });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync("b", (_, _) => 0));
        gate.Set(); await first;
    });
}

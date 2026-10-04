using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
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

    // A cancellation the user did not ask for (HttpClient timeout, a foreign token) is an error: logged once, reported once.
    [Fact] public void Cancellation_without_a_cancel_request_is_a_failure() => Sta.RunAsync(async () =>
    {
        var (runner, log, reported, shown) = Create(Never);
        var outcome = await runner.RunAsync<int>("fetch", (_, _) => throw new OperationCanceledException("not requested by the user"));
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.IsType<OperationCanceledException>(outcome.Error);
        Assert.Equal("fetch", Assert.Single(reported).Title); Assert.Single(log.Errors);
        Assert.Empty(shown);
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

    // The subscriber throws inside the Loaded dispatch of the window. That exception belongs to the dispatcher, not to RunAsync: it reaches
    // Dispatcher.UnhandledException (in the app: the global DispatcherUnhandledException handler with its crash dialog), while the run itself
    // goes on and ends normally, because the close of the window was registered before the event was raised.
    [Fact] public void Window_closes_even_when_a_WindowShown_subscriber_throws() => Sta.RunAsync(async () =>
    {
        var (runner, _, reported, shown) = Create(Soon);
        using var gate = new ManualResetEventSlim();
        var unhandled = new List<Exception>();
        Dispatcher.CurrentDispatcher.UnhandledException += (_, e) => { unhandled.Add(e.Exception); e.Handled = true; };

        // Without the fix nothing closes the window. The outer timeout of Sta.RunAsync would end the modal loop too, but only after 10 seconds
        // and with a generic TimeoutException. This guard closes the window after 3 seconds, so the test fails fast on the assertion below
        // with a message that names the cause.
        var stuck = false;
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        watchdog.Tick += (_, _) => { stuck = true; shown.ForEach(w => w.CloseByRunner()); };
        watchdog.Start();

        runner.WindowShown += (_, _) => { gate.Set(); throw new InvalidOperationException("subscriber failed"); };
        var outcome = await runner.RunAsync("t", (_, _) => { gate.Wait(); return 5; });
        watchdog.Stop();

        Assert.False(stuck, "The window stayed open until the guard closed it.");
        Assert.Equal((OperationStatus.Completed, 5), (outcome.Status, outcome.Value));
        Assert.False(Assert.Single(shown).IsVisible);
        Assert.Equal("subscriber failed", Assert.Single(unhandled).Message);
        Assert.Empty(reported);
    });

    // The app can lose activation during the show delay (alt-tab, a dialog of another program). The modal window must still have an owner,
    // or it can end up behind other windows with Cancel out of reach.
    [Fact] public void Progress_window_is_owned_by_a_visible_window_when_no_window_is_active() => Sta.RunAsync(async () =>
    {
        var main = new Window { ShowActivated = false };
        main.Show();
        Assert.False(main.IsActive);
        var (runner, _, _, _) = Create(Soon, main);
        using var gate = new ManualResetEventSlim();
        Window? owner = null;
        runner.WindowShown += (_, w) => { owner = w.Owner; gate.Set(); };
        await runner.RunAsync("slow", (_, _) => { gate.Wait(); return 0; });
        Assert.Same(main, owner);
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

    // If the modal window cannot be shown, the user can neither see the progress nor cancel: the runner cancels the work itself and fails the run.
    [Fact] public void Work_is_cancelled_when_the_window_cannot_be_shown() => Sta.RunAsync(async () =>
    {
        var main = new Window();
        var (runner, log, reported, shown) = Create(Soon, main);
        runner.ShowDialogOverride = _ => throw new InvalidOperationException("no window");
        var sawCancel = false;
        var outcome = await runner.RunAsync("op", (_, ct) => { ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)); sawCancel = ct.IsCancellationRequested; return 1; });
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.IsType<InvalidOperationException>(outcome.Error);
        Assert.Equal(1, outcome.Value);   // the work returned normally after the cancel: its value is kept, like for a cancelled run
        Assert.True(sawCancel);
        Assert.False(runner.IsRunning);
        Assert.True(main.IsHitTestVisible);
        Assert.Equal("op", Assert.Single(reported).Title); Assert.Single(log.Errors);
        Assert.Empty(shown);
    });

    // The work ignores the cancel request and returns normally: the user still saw no progress and could not cancel, so the run still fails.
    [Fact] public void Run_still_fails_when_the_work_ignores_the_cancel_after_the_window_failed() => Sta.RunAsync(async () =>
    {
        var (runner, _, reported, _) = Create(Soon);
        using var gate = new ManualResetEventSlim();
        runner.ShowDialogOverride = _ => { gate.Set(); throw new InvalidOperationException("no window"); };
        var outcome = await runner.RunAsync("op", (_, _) => { gate.Wait(); return 9; });
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.IsType<InvalidOperationException>(outcome.Error);
        Assert.Equal(9, outcome.Value);
        Assert.False(runner.IsRunning);
        Assert.Single(reported);
    });

    // The work throws something else once it was told to cancel: the outcome stays the show failure, and the fault of the work is logged as a
    // warning (not as an error), so the show error is the single logged error.
    [Fact] public void Fault_of_the_work_after_the_window_failed_is_logged_as_a_warning() => Sta.RunAsync(async () =>
    {
        var (runner, log, reported, _) = Create(Soon);
        runner.ShowDialogOverride = _ => throw new Win32Exception("no window");
        var outcome = await runner.RunAsync<int>("op", (_, ct) => { ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)); throw new IOException("late"); });
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.IsType<Win32Exception>(outcome.Error);
        Assert.Equal(0, outcome.Value);
        Assert.Single(reported);
        Assert.Single(log.Errors);
        var warning = Assert.Single(log.Warnings);
        Assert.Contains("op", warning); Assert.Contains("IOException", warning); Assert.Contains("late", warning);
    });

    // A cancellation of the work after the requested cancel is the expected reaction and is not logged.
    [Fact] public void Cancellation_of_the_work_after_the_window_failed_is_not_logged() => Sta.RunAsync(async () =>
    {
        var (runner, log, reported, _) = Create(Soon);
        runner.ShowDialogOverride = _ => throw new InvalidOperationException("no window");
        var outcome = await runner.RunAsync<int>("op", (_, ct) => { ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)); ct.ThrowIfCancellationRequested(); return 1; });
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.Equal(0, outcome.Value);
        Assert.Single(reported); Assert.Single(log.Errors); Assert.Empty(log.Warnings);
    });

    // Whatever else leaves the run early, the lock stays until the work has ended; the foreign exception propagates only after that.
    [Fact] public void Foreign_exception_while_showing_the_window_propagates_after_the_work_was_cancelled_and_ended() => Sta.RunAsync(async () =>
    {
        var main = new Window();
        var (runner, _, reported, _) = Create(Soon, main);
        runner.ShowDialogOverride = _ => throw new NullReferenceException();
        var sawCancel = false;
        await Assert.ThrowsAsync<NullReferenceException>(() => runner.RunAsync("op", (_, ct) =>
        {
            ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)); sawCancel = ct.IsCancellationRequested; return 1;
        }));
        Assert.True(sawCancel);
        Assert.True(main.IsHitTestVisible);
        Assert.False(runner.IsRunning);
        Assert.Empty(reported);
    });

    // The runner waits for work that ignores the token: no timeout, the lock stays and the run is still active until the work ends.
    [Fact] public void Run_waits_for_work_that_ignores_the_cancel_after_the_window_failed() => Sta.RunAsync(async () =>
    {
        var main = new Window();
        var (runner, _, _, _) = Create(Soon, main);
        using var gate = new ManualResetEventSlim();
        var showAttempted = false;
        runner.ShowDialogOverride = _ => { showAttempted = true; throw new InvalidOperationException("no window"); };
        var run = runner.RunAsync("op", (_, _) => { gate.Wait(); return 3; });
        while (!showAttempted)
        {
            await Task.Delay(5);
        }

        await Task.Delay(50);   // gives the runner every chance to (wrongly) finish while the work blocks
        Assert.False(run.IsCompleted);
        Assert.True(runner.IsRunning);
        Assert.False(main.IsHitTestVisible);

        gate.Set();
        var outcome = await run;
        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.Equal(3, outcome.Value);
        Assert.False(runner.IsRunning);
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

    [Fact] public void IsRunning_is_true_only_during_a_run() => Sta.RunAsync(async () =>
    {
        var (runner, _, _, _) = Create(Never);
        Assert.False(runner.IsRunning);
        var seen = false;
        var run = runner.RunAsync("t", (_, _) => { Thread.Sleep(50); return 1; });
        seen = runner.IsRunning;
        await run;
        Assert.True(seen); Assert.False(runner.IsRunning);
    });
}

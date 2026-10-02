using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace TotalWarModTool.App.Tests;

public class StaTests
{
    [Fact] public void RunAsync_returns_when_the_task_is_already_complete() =>
        Sta.RunAsync(() => Task.CompletedTask);

    [Fact] public void RunAsync_continues_awaits_on_the_dispatcher_thread() => Sta.RunAsync(async () =>
    {
        var thread = Environment.CurrentManagedThreadId;
        await Task.Delay(20);
        Assert.Equal(thread, Environment.CurrentManagedThreadId);
    });

    [Fact] public void RunAsync_rethrows_the_exception_of_the_task()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Sta.RunAsync(async () =>
        {
            await Task.Delay(10);
            throw new InvalidOperationException("boom");
        }));
        Assert.Equal("boom", ex.Message);
    }

    [Fact] public void RunAsync_throws_TimeoutException_when_the_task_does_not_finish() =>
        Assert.Throws<TimeoutException>(() => Sta.RunAsync(() => Task.Delay(Timeout.Infinite), timeoutSeconds: 1));

    // Run itself must not wait forever for a thread that is stuck; the stuck thread is a background thread, so it cannot hold the test host open.
    [Fact] public void Run_fails_instead_of_blocking_when_the_action_never_returns()
    {
        var ex = Assert.Throws<TimeoutException>(() => Sta.Run(() => Thread.Sleep(Timeout.Infinite), joinTimeoutSeconds: 1));
        Assert.Contains("STA", ex.Message);
    }

    [Fact] public void Run_shuts_down_the_dispatcher_of_its_thread()
    {
        Dispatcher? dispatcher = null;
        Sta.Run(() => dispatcher = Dispatcher.CurrentDispatcher);
        Assert.True(dispatcher!.HasShutdownFinished);
    }

    // A modal loop (ShowDialog) is a nested dispatcher frame; the timeout has to end that one too, or the test host hangs for good.
    [Fact] public void RunAsync_times_out_even_when_a_modal_window_never_closes()
    {
        var watch = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => Sta.RunAsync(async () =>
        {
            await Task.Yield();
            new Window().ShowDialog();      // nobody closes it
            await Task.Delay(Timeout.Infinite);
        }, timeoutSeconds: 1));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"The timeout took {watch.Elapsed}.");
    }

    // Same, when the action itself runs into the modal loop before it has returned a task.
    [Fact] public void RunAsync_times_out_when_the_action_blocks_in_a_modal_loop_before_it_returns()
    {
        var watch = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => Sta.RunAsync(() =>
        {
            new Window().ShowDialog();      // nobody closes it
            return Task.Delay(Timeout.Infinite);
        }, timeoutSeconds: 1));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"The timeout took {watch.Elapsed}.");
    }
}

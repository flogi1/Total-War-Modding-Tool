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
}

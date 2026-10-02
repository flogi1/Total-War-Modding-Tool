namespace TotalWarModTool.App.Tests;

public class LatestProgressTests
{
    private sealed class CountingContext : SynchronizationContext { public int Posts; public override void Post(SendOrPostCallback d, object? state) => Posts++; }

    [Fact] public void Flood_of_reports_posts_nothing_to_the_ui_thread()
    {
        var previous = SynchronizationContext.Current;
        var context = new CountingContext(); SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var progress = new LatestProgress();
            Parallel.For(0, 200_000, i => progress.Report(new(i, 200_000, "f" + i)));
            Assert.Equal(0, context.Posts);
            Assert.True(progress.TryTake(out var last)); Assert.Equal(200_000L, last.Total);
            Assert.False(progress.TryTake(out _));        // nothing new since the last take
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}

using TotalWarModTool.Operations;

namespace TotalWarModTool.App.Tests;

public class FakeOperationRunnerTests
{
    [Fact] public async Task Runs_the_work_and_records_the_title()
    {
        var runner = new FakeOperationRunner();
        var outcome = await runner.RunAsync("Title", (progress, _) => { progress.Report(new OperationProgress(1, 2, "x")); return 7; });
        Assert.Equal((OperationStatus.Completed, 7), (outcome.Status, outcome.Value));
        Assert.Equal(["Title"], runner.Titles);
        Assert.Equal(new OperationProgress(1, 2, "x"), Assert.Single(runner.Reports));
        Assert.False(runner.IsRunning);
    }

    [Fact] public async Task Cancel_and_failure_become_outcomes()
    {
        var runner = new FakeOperationRunner { CancelNext = true };
        var cancelled = await runner.RunAsync<int>("a", (_, ct) => { ct.ThrowIfCancellationRequested(); return 1; });
        Assert.Equal(OperationStatus.Cancelled, cancelled.Status);
        var failed = await runner.RunAsync<int>("b", (_, _) => throw new IOException("boom"));
        Assert.Equal((OperationStatus.Failed, "boom"), (failed.Status, failed.Error!.Message));
    }
}

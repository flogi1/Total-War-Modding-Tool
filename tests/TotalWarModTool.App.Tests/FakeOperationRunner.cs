using TotalWarModTool.Operations;

namespace TotalWarModTool.App.Tests;

/// <summary>
/// Stand-in for <see cref="ProgressRunner"/> in view model tests: no window, no thread. Runs the work synchronously on the calling thread and
/// turns its end into an <see cref="OperationOutcome{T}"/> the way the real runner does.
/// </summary>
public sealed class FakeOperationRunner : IOperationRunner
{
    /// <summary>Set by a test to simulate a run in progress; also true while the work of a fake run executes.</summary>
    public bool IsRunning { get; set; }

    public List<string> Titles { get; } = [];

    /// <summary>The next run gets a cancelled token. Work that returns anyway yields <see cref="OperationStatus.Cancelled"/> with its value.</summary>
    public bool CancelNext { get; set; }

    public List<OperationProgress> Reports { get; } = [];

    public Task<OperationOutcome<T>> RunAsync<T>(string title, Func<IProgress<OperationProgress>, CancellationToken, T> work)
    {
        Titles.Add(title);
        using var cts = new CancellationTokenSource();
        if (CancelNext)
        {
            cts.Cancel();
            CancelNext = false;
        }

        var wasRunning = IsRunning;
        IsRunning = true;
        try
        {
            var value = work(new CollectingProgress(Reports), cts.Token);
            var status = cts.IsCancellationRequested ? OperationStatus.Cancelled : OperationStatus.Completed;
            return Task.FromResult(new OperationOutcome<T>(status, value, null));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return Task.FromResult(new OperationOutcome<T>(OperationStatus.Cancelled, default, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationOutcome<T>(OperationStatus.Failed, default, ex));
        }
        finally
        {
            IsRunning = wasRunning;
        }
    }

    // Synchronous on purpose: Progress<T> would post to a context and make the reports arrive after the test has asserted.
    private sealed class CollectingProgress(List<OperationProgress> reports) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => reports.Add(value);
    }
}

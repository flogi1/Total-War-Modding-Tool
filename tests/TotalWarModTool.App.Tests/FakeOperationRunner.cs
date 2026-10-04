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

    /// <summary>The next run reports its end as cancelled although its work ran to the end with a live token (the cancel came too late): the outcome is <see cref="OperationStatus.Cancelled"/> with the value.</summary>
    public bool CancelAfterWork { get; set; }

    /// <summary>
    /// Called synchronously on every progress report of a run, with an action that cancels the token of that run. Lets a test cancel at a
    /// chosen moment of the work. Not used by default.
    /// </summary>
    public Action<OperationProgress, Action>? OnReport { get; set; }

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

        var cancelAfterWork = CancelAfterWork;
        CancelAfterWork = false;
        var wasRunning = IsRunning;
        IsRunning = true;
        try
        {
            var value = work(new CollectingProgress(Reports, OnReport, cts.Cancel), cts.Token);
            var status = cts.IsCancellationRequested || cancelAfterWork ? OperationStatus.Cancelled : OperationStatus.Completed;
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
    private sealed class CollectingProgress(List<OperationProgress> reports, Action<OperationProgress, Action>? hook, Action cancel) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            reports.Add(value);
            hook?.Invoke(value, cancel);
        }
    }
}

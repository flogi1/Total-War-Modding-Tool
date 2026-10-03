using TotalWarModTool.Operations;

namespace TotalWarModTool.App.Tests;

/// <summary>
/// Second stand-in for the progress runner: a run starts as pending and keeps <see cref="IsRunning"/> true until the test calls
/// <see cref="Complete"/>. That lets a test finish a user run at a chosen moment, for example in the middle of a silent run.
/// <see cref="Complete"/> executes the work on the calling thread and continues the awaiting code inline. One run at a time.
/// </summary>
internal sealed class ManualOperationRunner : IOperationRunner
{
    private readonly object _gate = new();
    private Action? _complete;
    private TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsRunning { get; private set; }

    public List<string> Titles { get; } = [];

    public Task<OperationOutcome<T>> RunAsync<T>(string title, Func<IProgress<OperationProgress>, CancellationToken, T> work)
    {
        var source = new TaskCompletionSource<OperationOutcome<T>>();
        lock (_gate)
        {
            if (_complete is not null)
            {
                throw new InvalidOperationException("Another operation is already running.");
            }

            Titles.Add(title);
            IsRunning = true;
            _complete = () =>
            {
                OperationOutcome<T> outcome;
                try
                {
                    outcome = new OperationOutcome<T>(OperationStatus.Completed, work(new NoProgress(), CancellationToken.None), null);
                }
                catch (Exception ex)
                {
                    outcome = new OperationOutcome<T>(OperationStatus.Failed, default, ex);
                }

                // Like the real runner: the lock is released before the caller continues.
                IsRunning = false;
                source.SetResult(outcome);
            };
            _started.TrySetResult();
        }

        return source.Task;
    }

    /// <summary>Ends when a run is waiting for <see cref="Complete"/> (at once if one already waits). Use it instead of a sleep.</summary>
    public Task WaitForRun()
    {
        lock (_gate)
        {
            return _complete is not null ? Task.CompletedTask : _started.Task;
        }
    }

    /// <summary>Runs the waiting run to its end. Throws when none waits.</summary>
    public void Complete()
    {
        Action complete;
        lock (_gate)
        {
            complete = _complete ?? throw new InvalidOperationException("No operation is waiting.");
            _complete = null;
            _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        complete();
    }

    private sealed class NoProgress : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
        }
    }
}

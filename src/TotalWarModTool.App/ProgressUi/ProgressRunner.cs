using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TotalWarModTool.Logging;
using TotalWarModTool.Operations;

namespace TotalWarModTool.App;

public enum OperationStatus { Completed, Cancelled, Failed }

/// <summary>Result of a run. <c>Value</c> is the return value of the work (also after a cancel, if the work returned normally) and the default otherwise.</summary>
public sealed record OperationOutcome<T>(OperationStatus Status, T? Value, Exception? Error);

/// <summary>
/// The one component every user-started operation runs through. The work starts at once on a pool thread. The main window is locked from the
/// start. Only if the work is still running after the show delay does a modal <see cref="ProgressWindow"/> appear, so fast operations never
/// flash a window. Errors are logged and reported, cancelling is reported as <see cref="OperationStatus.Cancelled"/>, and the lock is
/// released in every case.
/// </summary>
public sealed class ProgressRunner
{
    /// <summary>Time a run may take before its progress window appears. The one place to change it.</summary>
    public static readonly TimeSpan DefaultShowDelay = TimeSpan.FromMilliseconds(150);

    private readonly Func<IReadOnlyList<Window>> _openWindows;
    private readonly AppTexts _texts;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;
    private readonly Action<string, Exception> _reportError;
    private readonly TimeSpan _showDelay;
    private bool _running;

    public ProgressRunner(Func<IReadOnlyList<Window>> openWindows, AppTexts texts, IAppLog log, TimeProvider time,
                          Action<string, Exception> reportError, TimeSpan? showDelay = null)
    {
        _openWindows = openWindows;
        _texts = texts;
        _log = log;
        _time = time;
        _reportError = reportError;
        _showDelay = showDelay ?? DefaultShowDelay;
    }

    /// <summary>Raised on the UI thread when the progress window is loaded. It only exists for runs that outlast the show delay.</summary>
    public event EventHandler<ProgressWindow>? WindowShown;

    /// <summary>
    /// Runs <paramref name="work"/> and returns when it has ended and the window is gone. Must be called on the UI thread.
    /// Throws <see cref="InvalidOperationException"/> when another run is active.
    /// </summary>
    public async Task<OperationOutcome<T>> RunAsync<T>(string title, Func<IProgress<OperationProgress>, CancellationToken, T> work)
    {
        // Checked and set before the try block, so a rejected call can never reset the flag of the run that is still active.
        if (_running)
        {
            throw new InvalidOperationException("Another operation is already running.");
        }

        _running = true;
        InputLock? inputLock = null;
        using var cts = new CancellationTokenSource();
        try
        {
            var windows = _openWindows();
            inputLock = new InputLock(windows);

            var progress = new LatestProgress();
            var task = Task.Run(() => work(progress, cts.Token));

            await WaitForShowDelay(task);
            if (!task.IsCompleted)
            {
                ShowModal(title, progress, cts, task, windows);
            }

            return await Evaluate(title, task, cts);
        }
        finally
        {
            inputLock?.Dispose();
            _running = false;
        }
    }

    private async Task WaitForShowDelay(Task work)
    {
        using var delayCts = new CancellationTokenSource();
        await Task.WhenAny(work, Task.Delay(_showDelay, _time, delayCts.Token));
        delayCts.Cancel();
    }

    // Modal. The close is registered only once the window is loaded, i.e. after ShowDialog is running, so Close can never run before ShowDialog.
    // It is registered before WindowShown is raised: if a subscriber throws, the window still closes when the work has ended. The
    // continuation is posted to the UI thread, so it runs after this handler has returned.
    private void ShowModal(string title, LatestProgress progress, CancellationTokenSource cts, Task work, IReadOnlyList<Window> windows)
    {
        var owner = windows.FirstOrDefault(window => window.IsActive);
        var window = new ProgressWindow(title, _texts, progress, cts.Cancel)
        {
            Owner = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        window.Loaded += (_, _) =>
        {
            work.ContinueWith(_ => window.CloseByRunner(), TaskScheduler.FromCurrentSynchronizationContext());
            WindowShown?.Invoke(this, window);
        };
        window.ShowDialog();
    }

    private async Task<OperationOutcome<T>> Evaluate<T>(string title, Task<T> task, CancellationTokenSource cts)
    {
        try
        {
            var value = await task;
            return new OperationOutcome<T>(cts.IsCancellationRequested ? OperationStatus.Cancelled : OperationStatus.Completed, value, null);
        }
        // Only a cancel the user asked for counts as cancelled. Any other cancellation (HttpClient timeout, a foreign token) is an error
        // and falls through to the generic catch below, which logs and reports it.
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return new OperationOutcome<T>(OperationStatus.Cancelled, default, null);
        }
        catch (Exception ex)
        {
            _log.Error($"Operation \"{title}\" failed.", ex);
            _reportError(title, ex);
            return new OperationOutcome<T>(OperationStatus.Failed, default, ex);
        }
    }
}

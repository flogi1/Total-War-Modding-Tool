using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace TotalWarModTool.App.Tests;

/// <summary>WPF controls and dispatcher timers need an STA thread; xUnit runs on MTA. Every call gets its own thread and dispatcher.</summary>
internal static class Sta
{
    /// <summary>Runs <paramref name="action"/> on a fresh STA thread and rethrows what it throws.</summary>
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Handed to the calling thread below; the test thread rethrows it with its original stack trace.
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    /// <summary>
    /// Installs a <see cref="DispatcherSynchronizationContext"/> on a fresh STA thread, starts <paramref name="action"/>, and pumps a
    /// <see cref="DispatcherFrame"/> until the returned task ends, so awaits inside the action continue on the dispatcher thread and
    /// dispatcher timers tick. Rethrows the exception of the task. Throws <see cref="TimeoutException"/> if the task is not done
    /// after <paramref name="timeoutSeconds"/>.
    /// </summary>
    public static void RunAsync(Func<Task> action, int timeoutSeconds = 10)
    {
        Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            var frame = new DispatcherFrame();
            var task = action();
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();

            Dispatcher.PushFrame(frame);
            timer.Stop();

            if (!task.IsCompleted)
            {
                throw new TimeoutException($"The task did not finish within {timeoutSeconds} seconds.");
            }

            task.GetAwaiter().GetResult();
        });
    }
}

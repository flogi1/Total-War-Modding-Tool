using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace TotalWarModTool.App.Tests;

/// <summary>
/// WPF controls and dispatcher timers need an STA thread; xUnit runs on MTA. Every call gets its own thread and dispatcher. A stall turns into
/// a failing test (<see cref="TimeoutException"/>), never into a test host that hangs.
/// </summary>
internal static class Sta
{
    /// <summary>
    /// Runs <paramref name="action"/> on a fresh STA thread and rethrows what it throws. Afterwards the dispatcher of that thread is shut down,
    /// which also destroys windows left open on it. If the thread has not finished after <paramref name="joinTimeoutSeconds"/> (blocked in a call
    /// that never returns), a <see cref="TimeoutException"/> is thrown; the thread is a background thread, so it cannot keep the test host alive.
    /// </summary>
    public static void Run(Action action, int joinTimeoutSeconds = 30)
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
            finally
            {
                try
                {
                    Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
                }
                catch (Exception ex)
                {
                    // Same hand-over; an earlier error of the action is the more useful one and stays.
                    error ??= ex;
                }
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(joinTimeoutSeconds)))
        {
            throw new TimeoutException($"The STA thread did not finish within {joinTimeoutSeconds} seconds. It is probably blocked in a call that never returns. The thread was left running in the background.");
        }

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    /// <summary>
    /// Installs a <see cref="DispatcherSynchronizationContext"/> on a fresh STA thread, starts <paramref name="action"/>, and pumps a
    /// <see cref="DispatcherFrame"/> until the returned task ends, so awaits inside the action continue on the dispatcher thread and
    /// dispatcher timers tick. Rethrows the exception of the task. Throws <see cref="TimeoutException"/> if the task is not done
    /// after <paramref name="timeoutSeconds"/>. The timeout also ends nested frames, such as a <c>ShowDialog</c> that nobody closes (also one
    /// that the action itself enters before it has returned its task), and its timer starts before the action does, so that even this cannot hang.
    /// </summary>
    public static void RunAsync(Func<Task> action, int timeoutSeconds = 10)
    {
        // The thread gets time to unwind after the timeout fired before Run gives up on it.
        Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            timer.Tick += (_, _) =>
            {
                frame.Continue = false;
                Dispatcher.ExitAllFrames();
            };
            timer.Start();

            try
            {
                var task = action();
                task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);

                Dispatcher.PushFrame(frame);

                if (!task.IsCompleted)
                {
                    throw new TimeoutException($"The task did not finish within {timeoutSeconds} seconds.");
                }

                task.GetAwaiter().GetResult();
            }
            finally
            {
                timer.Stop();
            }
        }, joinTimeoutSeconds: timeoutSeconds + 20);
    }
}

using System.IO;
using System.Windows.Threading;
using TotalWarModTool.Logging;

namespace TotalWarModTool.App;

/// <summary>
/// Runs <c>save</c> 500 ms after the last <see cref="MarkDirty"/>, so a burst of changes writes once. Only <see cref="MarkDirty"/> makes the
/// state unsaved; the timer and <see cref="Flush"/> write nothing else. Must be created on the UI thread. A save that fails on a file
/// (I/O or access error) is logged, never thrown: losing one write must not take the app down. Any failure keeps the change pending;
/// an error of another kind is a bug and reaches the caller.
/// </summary>
public sealed class DebouncedSaver
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);

    private readonly Action _save;
    private readonly IAppLog _log;
    private readonly string _what;
    private readonly DispatcherTimer _timer;
    private bool _dirty;

    /// <param name="what">Names the saved thing in the log message (<c>"&lt;what&gt; could not be saved: ..."</c>).</param>
    public DebouncedSaver(Action save, IAppLog log, string what, TimeSpan? delay = null)
    {
        _save = save;
        _log = log;
        _what = what;
        _timer = new DispatcherTimer { Interval = delay ?? DefaultDelay };
        _timer.Tick += (_, _) => Flush();
    }

    public void MarkDirty()
    {
        _dirty = true;

        // Stop and start again to restart the delay.
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Saves now if there is something unsaved and cancels the pending timer. Does nothing when clean. A failed save keeps it pending.</summary>
    public void Flush()
    {
        _timer.Stop();
        if (!_dirty)
        {
            return;
        }

        // Cleared before the save: a change that arrives while it runs sets the flag again and is not lost.
        _dirty = false;
        try
        {
            _save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dirty = true;
            _log.Warn($"{_what} could not be saved: {ex.Message}");
        }
        catch
        {
            _dirty = true;
            throw;
        }
    }
}

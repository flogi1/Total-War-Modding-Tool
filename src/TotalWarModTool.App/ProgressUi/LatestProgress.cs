using TotalWarModTool.Operations;

namespace TotalWarModTool.App;

/// <summary>
/// Progress sink that keeps only the newest report. <see cref="Report"/> stores and returns at once (it never posts to the UI thread, so
/// a flood of reports cannot clog the dispatcher queue); the progress window picks the value up with <see cref="TryTake"/> on a timer.
/// </summary>
public sealed class LatestProgress : IProgress<OperationProgress>
{
    // OperationProgress is larger than a machine word, so the pair (value, flag) is guarded by a lock to avoid torn reads.
    private readonly object _gate = new();
    private OperationProgress _latest;
    private bool _hasNew;

    /// <summary>Thread-safe. Replaces the stored value; does nothing else.</summary>
    public void Report(OperationProgress value)
    {
        lock (_gate)
        {
            _latest = value;
            _hasNew = true;
        }
    }

    /// <summary>Returns <see langword="true"/> once per change: the newest value if something was reported since the last take.</summary>
    public bool TryTake(out OperationProgress value)
    {
        lock (_gate)
        {
            value = _latest;
            var changed = _hasNew;
            _hasNew = false;
            return changed;
        }
    }
}

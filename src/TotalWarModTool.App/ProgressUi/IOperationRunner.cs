using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.Operations;

namespace TotalWarModTool.App;

/// <summary>
/// The way view models start a user operation. <see cref="ProgressRunner"/> is the one implementation; a test fake is the other.
/// This is a deliberate exception to "interfaces only at the outside world": without it every view model test would need an STA thread and real windows.
/// </summary>
public interface IOperationRunner
{
    /// <summary>True from the start of <see cref="RunAsync{T}"/> until its input lock is released. Callers check it before starting a run.</summary>
    bool IsRunning { get; }

    Task<OperationOutcome<T>> RunAsync<T>(string title, Func<IProgress<OperationProgress>, CancellationToken, T> work);
}

using System.IO;
using System.Threading;
using System.Windows.Input;
using TotalWarModTool.Operations;

namespace TotalWarModTool.App;

/// <summary>View model of the main window.</summary>
public sealed class MainViewModel : ObservableObject
{
    private const int SlowStepCount = 100;
    private static readonly TimeSpan SlowStepDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FailDelay = TimeSpan.FromSeconds(1);

    /// <param name="openFolder">Opens a folder in the file explorer. Passed in so the view model stays free of processes and tests need no explorer.</param>
    /// <param name="progress">The runner every user-started operation goes through. Only the debug commands use it for now.</param>
    /// <param name="openSettings">Opens the settings window (or activates it if it is open). Passed in so the view model knows no windows.</param>
    public MainViewModel(AppTexts texts, AppPaths paths, Action<string> openFolder, ProgressRunner progress, Action openSettings)
    {
        Texts = texts;
        OpenSettingsCommand = new RelayCommand(openSettings);
        OpenLogFolderCommand = new RelayCommand(() => openFolder(paths.LogDirectory));

        // The debug commands exist in every build; only the menu that offers them is limited to the debug build (IsDebugBuild).
        DebugThrowCommand = new RelayCommand(() => throw new InvalidOperationException("Debug test exception"));
        DebugSlowCommand = new RelayCommand(async () => await progress.RunAsync(texts.MenuDebugSlow, RunSlowWork));
        DebugFailCommand = new RelayCommand(async () => await progress.RunAsync<int>(texts.MenuDebugFail, RunFailingWork));
    }

    public AppTexts Texts { get; }

    /// <summary>True in the debug build only. The Debug menu binds its visibility to it.</summary>
    public bool IsDebugBuild { get; } = ReadIsDebugBuild();

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenLogFolderCommand { get; }

    /// <summary>Throws on the UI thread, to try out the global exception handling and the crash dialog.</summary>
    public ICommand DebugThrowCommand { get; }

    /// <summary>A five-second operation with a progress count, to try out the progress window and Cancel.</summary>
    public ICommand DebugSlowCommand { get; }

    /// <summary>An operation that fails after one second, to try out the error report of the progress runner.</summary>
    public ICommand DebugFailCommand { get; }

    private static bool ReadIsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    // Waits on the token instead of sleeping, so Cancel ends the run at once. Returns the steps done, which is also the partial result after a cancel.
    private static int RunSlowWork(IProgress<OperationProgress> progress, CancellationToken cancellation)
    {
        var done = 0;
        while (done < SlowStepCount && !cancellation.WaitHandle.WaitOne(SlowStepDelay))
        {
            done++;
            progress.Report(new OperationProgress(done, SlowStepCount, $"step {done}"));
        }

        return done;
    }

    private static int RunFailingWork(IProgress<OperationProgress> progress, CancellationToken cancellation)
    {
        Thread.Sleep(FailDelay);
        throw new IOException("Debug failure");
    }
}

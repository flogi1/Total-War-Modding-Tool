namespace TotalWarModTool.App;

/// <summary>
/// Runs the start-up composition of the app. If a step throws before the main window exists, the process would stay alive without any
/// window, so the failure is handled as fatal (log entry and crash dialog that says the app closes) and the app is asked to shut down.
/// </summary>
public static class StartupGuard
{
    /// <summary>Exit code of a process whose start-up failed.</summary>
    public const int FailureExitCode = 1;

    /// <param name="composition">Creates everything up to the visible main window.</param>
    /// <param name="crashHandler">Logs the failure and shows the dialog. Must be usable already, so it is created before the composition.</param>
    /// <param name="shutdown">Asks the app to exit with the given code.</param>
    public static void Run(Action composition, CrashHandler crashHandler, Action<int> shutdown)
    {
        try
        {
            composition();
        }
        catch (Exception ex)
        {
            // Logged by Handle, which also shows the dialog and never throws.
            crashHandler.Handle(ex, CrashHandler.SourceStartup, fatal: true);
            shutdown(FailureExitCode);
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TotalWarModTool.Logging;

namespace TotalWarModTool.App;

/// <summary>
/// The global handler for exceptions nobody caught. Every exception is logged; the crash dialog is shown for the first one and, while it
/// is open, later ones only reach the log, so a failing dialog or a burst of errors cannot stack up windows.
/// </summary>
public sealed class CrashHandler
{
    public const string SourceUi = "ui", SourceAppDomain = "appdomain", SourceTask = "task";

    private readonly IAppLog _log;
    private readonly TimeProvider _time;
    private readonly string _appVersion;
    private readonly string? _userProfilePath;
    private readonly Action<string> _showDialog;

    // Exceptions arrive from any thread (pool, finalizer, UI), so the "dialog is open" flag is set atomically.
    private int _dialogOpen;

    /// <param name="showDialog">Shows the dialog with the report text and returns when it is closed. May be called from any thread.</param>
    public CrashHandler(IAppLog log, TimeProvider time, string appVersion, string? userProfilePath, Action<string> showDialog)
    {
        _log = log;
        _time = time;
        _appVersion = appVersion;
        _userProfilePath = userProfilePath;
        _showDialog = showDialog;
    }

    /// <summary>
    /// Hooks the three global handlers. A UI exception is marked as handled, so the app keeps running after the dialog;
    /// an unobserved task exception is marked as observed.
    /// </summary>
    public void Install(Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            Handle(e.Exception, SourceUi);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Handle(e.ExceptionObject as Exception ?? new InvalidOperationException($"A non-exception object was thrown: {e.ExceptionObject}"), SourceAppDomain);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Handle(e.Exception, SourceTask);
            e.SetObserved();
        };
    }

    /// <summary>Logs the exception and shows the crash dialog unless one is already open. Never throws.</summary>
    public void Handle(Exception exception, string source)
    {
        _log.Error($"Unhandled exception (source: {source}).", exception);

        if (Interlocked.Exchange(ref _dialogOpen, 1) == 1)
        {
            return;
        }

        try
        {
            _showDialog(CrashReport.Build(exception, source, _appVersion, _time.GetLocalNow(), _userProfilePath));
        }
        catch (Exception ex)
        {
            // The handler is the last line of defence: a dialog that cannot be shown must not become the next crash.
            _log.Error("The crash dialog could not be shown.", ex);
        }
        finally
        {
            Volatile.Write(ref _dialogOpen, 0);
        }
    }
}

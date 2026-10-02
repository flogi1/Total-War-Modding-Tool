using TotalWarModTool.Localization;

namespace TotalWarModTool.App;

/// <summary>The UI texts of the app shell: one property per text, each with its English inline fallback.</summary>
public sealed class AppTexts(UiText ui) : TextsBase(ui)
{
    /// <summary>Product name. Not translated.</summary>
    public const string AppName = "Total War Modding Tool";

    public string AppTitle => T("app.title", AppName);

    public string MenuTools => T("menu.tools", "Tools");

    public string MenuOpenLogFolder => T("menu.tools.openLogFolder", "Open log folder");

    public string ProgressCancel => T("progress.cancel", "Cancel");

    public string ProgressCancelling => T("progress.cancelling", "Cancelling");

    /// <summary>Count line of the progress window, e.g. "120 / 4000".</summary>
    public string ProgressCount(long done, long total) => F("progress.count", "{0} / {1}", done, total);

    public string OperationFailedTitle => T("error.operationFailed.title", "Operation failed");

    /// <param name="operation">Title of the operation that failed.</param>
    /// <param name="reason">Message of the exception.</param>
    public string OperationFailedMessage(string operation, string reason) =>
        F("error.operationFailed.message", "\"{0}\" could not be completed: {1} The log contains details (Tools, Open log folder).", operation, reason);
}

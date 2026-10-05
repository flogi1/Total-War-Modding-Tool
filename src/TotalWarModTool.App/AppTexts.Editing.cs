using TotalWarModTool.Projects;

namespace TotalWarModTool.App;

// Texts of the editable tabs: the messages for a cell text that is refused. One text per type of value; EditableRowsTabViewModel
// chooses among them. The library gives only a short technical English reason, which is appended as data.
public sealed partial class AppTexts
{
    /// <param name="min">The smallest value of the type, as text in the invariant culture.</param>
    /// <param name="max">The largest value of the type, as text in the invariant culture.</param>
    public string EditExpectedInteger(string min, string max) => F("edit.expected.integer", "Expected a whole number from {0} to {1}.", min, max);

    public string EditExpectedFloat => T("edit.expected.float", "Expected a number with a decimal point, for example 1.5.");

    public string EditExpectedBoolean => T("edit.expected.boolean", "Expected true or false.");

    public string EditExpectedColour => T("edit.expected.colour", "Expected a colour as hex digits, for example FF8800.");

    public string EditExpectedText => T("edit.expected.text", "Expected text that the file can store. Carriage returns and NUL characters are not allowed.");

    public string EditExpectedChannel => T("edit.expected.channel", "Expected a whole number from 0 to 255, because this column is a colour channel.");

    /// <param name="expected">What the column expects, one of the <c>EditExpected</c> texts.</param>
    /// <param name="reason">Why the text was refused; English text of the library. Data, not a UI text.</param>
    public string EditCheckMessage(string expected, string reason) => F("edit.check.message", "{0} Reason: {1}", expected, reason);

    // ---- menu "Edit" and the messages of undo and redo ----------------------------------------------------------------------------

    public string MenuEdit => T("menu.edit", "Edit");

    public string MenuEditUndo => T("menu.edit.undo", "Undo");

    public string MenuEditRedo => T("menu.edit.redo", "Redo");

    /// <summary>The shortcut shown next to "Undo" in the menu; the shortcut itself is bound in the window.</summary>
    public string MenuEditUndoGesture => T("menu.edit.undoGesture", "Ctrl+Z");

    /// <summary>The shortcut shown next to "Redo" in the menu; the shortcut itself is bound in the window.</summary>
    public string MenuEditRedoGesture => T("menu.edit.redoGesture", "Ctrl+Y");

    public string EditUndoFailedTitle => T("edit.undoFailed.title", "Undo failed");

    /// <param name="reason">Why the step failed; technical text of the exception, escaped. Data, not a UI text.</param>
    public string EditUndoFailedMessage(string reason) =>
        F("edit.undoFailed.message", "The step could not be undone and was removed from the history. Reason: {0}", reason);

    public string EditRedoFailedTitle => T("edit.redoFailed.title", "Redo failed");

    /// <param name="reason">Why the step failed; technical text of the exception, escaped. Data, not a UI text.</param>
    public string EditRedoFailedMessage(string reason) =>
        F("edit.redoFailed.message", "The step could not be repeated and was removed from the history. Reason: {0}", reason);

    // ---- saving and the guard for unsaved changes -----------------------------------------------------------------------------------

    public string MenuProjectSave => T("menu.project.save", "Save");

    public string MenuProjectSaveAll => T("menu.project.saveAll", "Save all");

    /// <summary>The shortcut shown next to "Save" in the menu; the shortcut itself is bound in the window.</summary>
    public string MenuProjectSaveGesture => T("menu.project.saveGesture", "Ctrl+S");

    /// <summary>The shortcut shown next to "Save all" in the menu; the shortcut itself is bound in the window.</summary>
    public string MenuProjectSaveAllGesture => T("menu.project.saveAllGesture", "Ctrl+Shift+S");

    /// <summary>The title of the progress window of a save.</summary>
    public string OperationSaveFiles => T("operation.save", "Save files");

    /// <summary>The tool tip of the mark in the header of a tab that has changes that are not saved.</summary>
    public string TabModified => T("tab.modified", "Unsaved changes");

    public string SaveFailedTitle => T("save.failed.title", "Saving failed");

    /// <param name="failed">Number of files that could not be saved.</param>
    public string SaveFailedSummary(int failed) =>
        F("save.failed.summary", "Files that could not be saved: {0}. The tabs stay modified, so no change is lost.", failed);

    /// <param name="file">Display name of the file (escaped). Data, not a UI text.</param>
    /// <param name="reason">One of the <c>SaveProblem</c> texts, with its detail.</param>
    public string SaveProblemLine(string file, string reason) => F("save.problemLine", "{0}: {1}", file, reason);

    /// <param name="text">One of the <c>SaveProblem</c> texts.</param>
    /// <param name="detail">Short technical English of the library, escaped. Data, not a UI text.</param>
    public string SaveProblemWithDetail(string text, string detail) => F("save.problem.withDetail", "{0} Detail: {1}", text, detail);

    // Every FileWriteProblem has its own property so that the locale check sees each key; SaveProblemText only chooses among them.
    public string SaveProblemUnsafePath => T("save.problem.unsafePath",
        "The path of the file is not a plain path below the pack folder, so the file is not written.");

    public string SaveProblemLink => T("save.problem.link", "The file or one of its folders is a link. The tool never writes through links.");

    public string SaveProblemLocked => T("save.problem.locked",
        "The file is in use by another program or is read-only. Close it in the other program, then save again.");

    public string SaveProblemFailed => T("save.problem.failed", "The file could not be written.");

    public string SaveProblemProjectGone => T("save.problem.projectGone",
        "The project of this file is no longer in the list, so the file cannot be written.");

    public string SaveProblemRefused => T("save.problem.refused", "The content cannot be written as text.");

    /// <summary>The text for a write problem; empty for <see cref="FileWriteProblem.None"/>.</summary>
    public string SaveProblemText(FileWriteProblem problem) => problem switch
    {
        FileWriteProblem.UnsafePath => SaveProblemUnsafePath,
        FileWriteProblem.Link => SaveProblemLink,
        FileWriteProblem.Locked => SaveProblemLocked,
        FileWriteProblem.Failed => SaveProblemFailed,
        _ => string.Empty,
    };

    public string SaveCancelledTitle => T("save.cancelled.title", "Saving cancelled");

    /// <param name="saved">Number of files that were written before the cancel.</param>
    /// <param name="total">Number of files that were to be written.</param>
    public string SaveCancelledMessage(int saved, int total) =>
        F("save.cancelled.message", "Saving was cancelled. {0} of {1} files were saved; the others are still modified.", saved, total);

    // ---- changes of the files from outside ----------------------------------------------------------------------------------------

    /// <summary>The tool tip of the stale mark and the line in the tab: the file of a project tab was changed by another program.</summary>
    public string TabStaleChanged => T("tab.staleChanged", "The file was changed outside. This tab shows the old content.");

    /// <summary>The tool tip of the stale mark and the line in the tab: the file of a project tab was deleted by another program.</summary>
    public string TabStaleDeleted => T("tab.staleDeleted", "The file was deleted outside. This tab shows the content from before.");

    /// <summary>The button that reads the file of a stale tab again; also the "reload" answer of the conflict question.</summary>
    public string TabReload => T("tab.reload", "Reload");

    /// <summary>The title of the message when a tab cannot be reloaded because another operation runs.</summary>
    public string ReloadFailedTitle => T("reload.failed.title", "Reload not possible");

    public string ConflictTitle => T("conflict.title", "File changed outside");

    public string ConflictMessage => T("conflict.message",
        "The file was changed outside since you opened it. Overwrite replaces that version with yours. Reload drops your changes and shows the outside version.");

    public string ConflictOverwrite => T("conflict.overwrite", "Overwrite");

    public string DeletedTitle => T("deleted.title", "File deleted outside");

    public string DeletedMessage => T("deleted.message", "The file was deleted outside. Create it again with the content of this tab?");

    public string DeletedCreateAgain => T("deleted.createAgain", "Create again");

    public string ActionFailedTitle => T("action.failed.title", "Action failed");

    /// <param name="reason">Why the action failed; technical text of the exception, escaped. Data, not a UI text.</param>
    public string ActionFailedMessage(string reason) => F("action.failed.message", "The action could not be completed. Reason: {0}", reason);

    public string UnsavedTitle => T("unsaved.title", "Unsaved changes");

    public string UnsavedMessage => T("unsaved.message", "These files have changes that are not saved. What should happen to them?");

    public string UnsavedSave => T("unsaved.save", "Save");

    public string UnsavedDiscard => T("unsaved.discard", "Discard");

    public string UnsavedBuildMessage => T("unsaved.build.message",
        "The project has changes that are not saved. A build contains only saved files. Save all and build?");

    public string UnsavedBuildSaveAll => T("unsaved.build.saveAll", "Save all and build");

    /// <summary>Why a project cannot be removed from the list while some of its tabs have changes that are not saved.</summary>
    public string ProjectRemoveUnsaved => T("project.removeUnsaved",
        "The project has open tabs with unsaved changes. Save or discard them first.");
}

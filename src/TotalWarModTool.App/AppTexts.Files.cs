using TotalWarModTool.Projects;

namespace TotalWarModTool.App;

// Texts of the file operations of a project (new table, new loc file, add a pack entry, rename, delete). The library returns enums; these
// texts turn them into words with a hint what to do. Every enum value has its own property so the locale check sees each key.
public sealed partial class AppTexts
{
    /// <param name="fileName">File name of the new file. Data, not a UI text.</param>
    public string OperationCreateFile(string fileName) => F("operation.createFile", "Create {0}", fileName);

    /// <param name="fileName">File name of the file that is added. Data, not a UI text.</param>
    public string OperationAddFile(string fileName) => F("operation.addFile", "Add {0} to the project", fileName);

    /// <param name="fileName">Current file name. Data, not a UI text.</param>
    public string OperationRenameFile(string fileName) => F("operation.renameFile", "Rename {0}", fileName);

    /// <param name="fileName">File name of the file that is deleted. Data, not a UI text.</param>
    public string OperationDeleteFile(string fileName) => F("operation.deleteFile", "Delete {0}", fileName);

    public string OperationPrepareTables => T("operation.prepareTables", "Preparing the table list");

    public string FileProblemUnsafePath => T("file.problem.unsafePath",
        "This is not a plain file name below the pack folder of the project. Choose a different name.");

    public string FileProblemLink => T("file.problem.link", "The file or one of its folders is a link. The tool never writes through links.");

    public string FileProblemTargetExists => T("file.problem.targetExists",
        "A file or a folder with this name exists already. Nothing was changed. Choose a different name.");

    public string FileProblemSourceMissing => T("file.problem.sourceMissing",
        "The file is not there any more. Refresh the project and try again.");

    public string FileProblemLocked => T("file.problem.locked",
        "The file is in use by another program or the file system refused the change. Close the other program, then try again.");

    public string FileProblemFailed => T("file.problem.failed", "The file could not be written. Check the project folder and your permissions, then try again.");

    public string TrashProblemLocked => T("trash.problem.locked",
        "The file could not be moved to the trash because another program holds it open. Close that program, then try again.");

    public string TrashProblemFailed => T("trash.problem.failed",
        "The file could not be moved to the trash. Check the project folder and the trash folder of the app, then try again.");

    /// <summary>The text for the problem of a create, add or rename; empty for <see cref="FileOpProblem.None"/>.</summary>
    public string FileProblemText(FileOpProblem problem) => problem switch
    {
        FileOpProblem.UnsafePath => FileProblemUnsafePath,
        FileOpProblem.Link => FileProblemLink,
        FileOpProblem.TargetExists => FileProblemTargetExists,
        FileOpProblem.SourceMissing => FileProblemSourceMissing,
        FileOpProblem.Locked => FileProblemLocked,
        FileOpProblem.Failed => FileProblemFailed,
        _ => string.Empty,
    };

    /// <summary>The text for the problem of a delete (a move to the trash); empty for <see cref="FileOpProblem.None"/>.</summary>
    public string TrashProblemText(FileOpProblem problem) => problem switch
    {
        FileOpProblem.Locked => TrashProblemLocked,
        FileOpProblem.Failed => TrashProblemFailed,
        _ => FileProblemText(problem),
    };

    /// <param name="reason">Why the file cannot be created; English text of the library. Data, not a UI text.</param>
    public string FileCannotCreate(string reason) => F("file.cannotCreate", "The file cannot be created: {0}", reason);

    public string FileNameEmpty => T("file.name.empty", "The name is empty. Enter a name.");

    /// <param name="maxChars">The longest allowed name, in characters.</param>
    public string FileNameTooLong(int maxChars) => F("file.name.tooLong", "The name may have at most {0} characters.", maxChars);

    public string FileNameSeparator => T("file.name.separator", "The name must not contain a slash or a backslash.");

    public string FileNameDot => T("file.name.dot", "The name must not contain a dot.");

    public string FileNameLeadingOrTrailingSpace => T("file.name.space", "The name must not start or end with a space.");

    public string FileNameTabOrLineBreak => T("file.name.tabOrLineBreak", "The name must not contain a tab or a line break.");

    public string FileNameSemicolon => T("file.name.semicolon", "The name must not contain a semicolon (;).");

    public string FileNameUnpairedSurrogate => T("file.name.unpairedSurrogate",
        "The name contains a broken character (an unpaired surrogate). Type the name again.");

    public string FileNameInvisibleCharacter => T("file.name.invisible", "The name must not contain invisible control characters.");

    public string FileNameIgnored => T("file.name.ignored",
        "A project ignores files with this name, so the file would never get into the pack. Choose a different name.");

    public string FileNameInvalidCharacter => T("file.name.invalidCharacter",
        "The name must not contain characters that Windows does not allow in file names, such as : | ? * < >.");

    public string FileNameReservedName => T("file.name.reserved", "Windows reserves this name (for example NUL, CON or COM1). Choose a different name.");

    /// <summary>The text for a name the rules refuse; empty for <see cref="NameIssue.None"/>.</summary>
    public string FileNameIssueText(NameIssue issue) => issue switch
    {
        NameIssue.Empty => FileNameEmpty,
        NameIssue.TooLong => FileNameTooLong(NewFiles.MaxNameLength),
        NameIssue.Separator => FileNameSeparator,
        NameIssue.Dot => FileNameDot,
        NameIssue.LeadingOrTrailingSpace => FileNameLeadingOrTrailingSpace,
        NameIssue.TabOrLineBreak => FileNameTabOrLineBreak,
        NameIssue.Semicolon => FileNameSemicolon,
        NameIssue.UnpairedSurrogate => FileNameUnpairedSurrogate,
        NameIssue.InvisibleCharacter => FileNameInvisibleCharacter,
        NameIssue.Ignored => FileNameIgnored,
        NameIssue.InvalidCharacter => FileNameInvalidCharacter,
        NameIssue.ReservedName => FileNameReservedName,
        _ => string.Empty,
    };

    /// <param name="file">The file, escaped. Data, not a UI text.</param>
    public string FileTabUnsaved(string file) => F("file.tabUnsaved",
        "The file {0} has unsaved changes in an open tab. Save the tab or close it, then try again.", file);

    public string FileNoSchema => T("file.noSchema", "No schema is loaded, so no new table can be created. Check the schema folder in the settings.");

    public string FileRenameChangesKind => T("file.renameChangesKind",
        "With this name the project would take the file for another kind of file. Choose a different name.");

    /// <param name="reason">Why the table or loc file stayed binary; English text of the library. Data, not a UI text.</param>
    public string FileAddedBinary(string reason) => F("file.addedBinary",
        "The file was added as it is, because it could not be converted to text: {0}", reason);

    public string FileNameUnchanged => T("file.nameUnchanged", "The name is unchanged. Enter a different name.");

    /// <param name="file">The file, escaped. Data, not a UI text.</param>
    public string FileStepFileChanged(string file) => F("file.stepFileChanged",
        "The file {0} was changed or removed after this step left it, so the step was removed from the history and nothing was moved to the trash. A file that is still there was left as it is; delete it with Delete if you do not need it.",
        file);

    /// <param name="count">How many further steps were removed.</param>
    /// <param name="file">The file, escaped. Data, not a UI text.</param>
    public string FileFurtherStepsRemoved(int count, string file) => F("file.furtherStepsRemoved",
        "Further steps of the history for the file {0} were removed as well, because they no longer match the folder. Removed steps: {1}.", file, count);

    /// <summary>The line above the trash paths of the steps that a failed undo or redo removed; each path follows on a line of its own.</summary>
    public string FileStepTrashFiles => T("file.stepTrashFiles", "These files stay in the trash, and no step brings them back. The log lists them too:");

    /// <param name="count">How many trash paths are not shown in the message.</param>
    public string FileStepMoreTrashFiles(int count) => F("file.stepMoreTrashFiles", "Further files in the trash, listed in the log: {0}.", count);

    public string FileStepDeferred => T("file.stepDeferred",
        "The file is in use by another program, or the project folder cannot be reached right now. Nothing was changed, and the step stays in the history. Close the other program or check the project folder, then try again.");

    // ---- commands and dialogs of the file operations -----------------------------------------------------------------------------------
    // The name label reuses DialogName, "no project is active" ProjectNoneActive, and "another operation runs" ProjectBusy.

    public string FileNewTableTitle => T("file.newTable.title", "New table");

    public string FileNewLocTitle => T("file.newLoc.title", "New loc file");

    public string FileAddTitle => T("file.add.title", "Add to the project");

    public string FileRenameTitle => T("file.rename.title", "Rename");

    public string FileActionDelete => T("file.action.delete", "Delete");

    public string FileTableFilterLabel => T("file.table.filterLabel", "Filter tables");

    public string FileTableListLabel => T("file.table.listLabel", "Table");

    public string FileTableNoMatch => T("file.table.noMatch", "No table matches the filter. Change the filter text.");

    public string FileSameNameTitle => T("file.sameName.title", "Replace the whole file in the game?");

    public string FileSameNameMessage => T("file.sameName.message",
        "The new file has the same name as the entry it comes from. In the game it replaces that whole file, not only the rows you change. Keep this name?");

    public string FileSameNameKeep => T("file.sameName.keep", "Keep the name");

    public string FileSameNameChange => T("file.sameName.change", "Choose another name");

    /// <param name="file">The new file, escaped. Data, not a UI text.</param>
    public string FileCreatedStatus(string file) => F("file.createdStatus", "Created {0}.", file);

    /// <param name="file">The file that was added, escaped. Data, not a UI text.</param>
    public string FileAddedStatus(string file) => F("file.addedStatus", "Added {0} to the project.", file);

    /// <param name="from">The old name of the file, escaped. Data, not a UI text.</param>
    /// <param name="to">The new name of the file, escaped. Data, not a UI text.</param>
    public string FileRenamedStatus(string from, string to) => F("file.renamedStatus", "Renamed {0} to {1}.", from, to);

    /// <param name="file">The file that was deleted, escaped. Data, not a UI text.</param>
    public string FileDeletedStatus(string file) => F("file.deletedStatus", "Deleted {0}. Undo brings it back.", file);

    public string EditUndoBlockedTitle => T("edit.undoBlocked.title", "Undo not possible");

    public string EditRedoBlockedTitle => T("edit.redoBlocked.title", "Redo not possible");
}

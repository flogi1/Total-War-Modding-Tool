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

    /// <param name="reason">Why the name cannot be used; English text of the library. Data, not a UI text.</param>
    public string FileNameInvalid(string reason) => F("file.nameInvalid", "This name cannot be used: {0}", reason);

    /// <param name="file">The file, escaped. Data, not a UI text.</param>
    public string FileTabUnsaved(string file) => F("file.tabUnsaved",
        "The file {0} has unsaved changes in an open tab. Save the tab or close it, then try again.", file);

    public string FileNoSchema => T("file.noSchema", "No schema is loaded, so no new table can be created. Check the schema folder in the settings.");

    public string FileRenameChangesKind => T("file.renameChangesKind",
        "With this name the project would take the file for another kind of file. Choose a different name.");

    /// <param name="reason">Why the table or loc file stayed binary; English text of the library. Data, not a UI text.</param>
    public string FileAddedBinary(string reason) => F("file.addedBinary",
        "The file was added as it is, because it could not be converted to text: {0}", reason);

    public string EditUndoBlockedTitle => T("edit.undoBlocked.title", "Undo not possible");

    public string EditRedoBlockedTitle => T("edit.redoBlocked.title", "Redo not possible");
}

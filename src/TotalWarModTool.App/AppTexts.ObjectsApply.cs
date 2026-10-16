using TotalWarModTool.Editing;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

// Texts of the questions and messages when the text of an object tab is applied (ObjectApplyController, ObjectDialogs).
public sealed partial class AppTexts
{
    // Unapplied text.
    public string ObjectsUnappliedTitle => T("objects.unapplied.title", "Unapplied changes");

    /// <param name="objectName">The key of the object (data, escaped by the caller).</param>
    public string ObjectsUnappliedMessage(string objectName) =>
        F("objects.unapplied.message", "The text of {0} has changes that are not applied. What should happen to them?", objectName);

    public string ObjectsUnappliedDiscard => T("objects.unapplied.discard", "Discard");

    // Target file question.
    public string ObjectsTargetTitle => T("objects.target.title", "Which file?");

    /// <param name="table">The table (data, escaped by the caller).</param>
    public string ObjectsTargetMessage(string table) => F("objects.target.message", "The project has several files for {0}. Which one takes the changes?", table);

    public string ObjectsTargetLocMessage => T("objects.target.locMessage", "The project has several loc files. Which one takes the texts?");

    // Copy question.
    public string ObjectsCopyTitle => T("objects.copy.title", "Copy the file of the game?");

    public string ObjectsCopyMessage => T("objects.copy.message",
        "Deleting a row of the game needs a copy of its file in the project. The copy replaces the file of the game as a whole. Other mods that replace the same file will conflict. After a game update the copy can be outdated.");

    /// <param name="file">The file (data, escaped by the caller).</param>
    /// <param name="rows">Rows of the copy.</param>
    /// <param name="keys">The deleted keys, joined with ", " (data, escaped by the caller).</param>
    public string ObjectsCopyLine(string file, int rows, string keys) => F("objects.copy.line", "{0} ({1} rows), without: {2}", file, rows, keys);

    public string ObjectsCopyConfirm => T("objects.copy.confirm", "Copy and delete");

    // Errors before writing.
    public string ObjectsApplyErrorsTitle => T("objects.apply.errors.title", "The text cannot be applied");

    public string ObjectsApplyErrorsMessage(int count) => F("objects.apply.errors.message", "{0} errors in the text:", count);

    public string ObjectsApplyErrorLine(int line, string text) => F("objects.apply.errorLine", "Line {0}: {1}", line, text);

    public string ObjectsApplyPlanErrorsMessage(int count) => F("objects.apply.planErrors.message", "{0} problems with the project files:", count);

    /// <param name="file">The file (data, escaped by the caller).</param>
    /// <param name="text">The problem.</param>
    public string ObjectsApplyPlanErrorLine(string file, string text) => F("objects.apply.planErrorLine", "{0}: {1}", file, text);

    /// <param name="file">The file (data, escaped by the caller).</param>
    public string ObjectsApplyGridUnsaved(string file) => F("objects.apply.gridUnsaved", "{0} has unsaved changes in the grid. Save them first.", file);

    // Errors while writing.
    public string ObjectsApplyFailedTitle => T("objects.apply.failed.title", "Applying failed");

    /// <param name="file">The file (data, escaped by the caller).</param>
    public string ObjectsApplyFileChanged(string file) =>
        F("objects.apply.fileChanged", "{0} changed since the object was built. Refresh the object and apply again.", file);

    /// <param name="file">The file (data, escaped by the caller).</param>
    /// <param name="detail">Why (data, escaped by the caller).</param>
    public string ObjectsApplyFileUnknown(string file, string detail) => F("objects.apply.fileUnknown", "{0} cannot be checked: {1}", file, detail);

    public string ObjectsApplyWriteFailed(string detail) =>
        F("objects.apply.writeFailed", "A file could not be written and the written files were restored: {0}", detail);

    public string ObjectsApplyRollbackFailed(string detail) =>
        F("objects.apply.rollbackFailed", "A file could not be written and not every written file could be restored: {0} The files and their state:", detail);

    public string ObjectsApplyFileState(string file, string state) => F("objects.apply.fileState", "{0}: {1}", file, state);

    public string ObjectsFileStateUnchanged => T("objects.fileState.unchanged", "unchanged");

    public string ObjectsFileStateWritten => T("objects.fileState.written", "written");

    public string ObjectsFileStateRolledBack => T("objects.fileState.rolledBack", "restored");

    public string ObjectsFileStateRollbackFailed => T("objects.fileState.rollbackFailed", "could not be restored");

    public string ObjectsFileStateText(WrittenFileState state) => state switch
    {
        WrittenFileState.Unchanged => ObjectsFileStateUnchanged,
        WrittenFileState.Written => ObjectsFileStateWritten,
        WrittenFileState.RolledBack => ObjectsFileStateRolledBack,
        WrittenFileState.RollbackFailed => ObjectsFileStateRollbackFailed,
        _ => state.ToString(),
    };

    // One text per DiagnosticKind.
    public string ObjectsDiagUnknownLineForm => T("objects.diag.unknownLineForm", "This line is not a field, a row, a text or a section head.");

    public string ObjectsDiagHeadChanged => T("objects.diag.headChanged", "The head line was changed. Refresh the object.");

    public string ObjectsDiagUnknownField => T("objects.diag.unknownField", "Unknown field.");

    public string ObjectsDiagUnknownSection => T("objects.diag.unknownSection", "Unknown section.");

    public string ObjectsDiagHiddenSection => T("objects.diag.hiddenSection", "This section is hidden in this view.");

    public string ObjectsDiagFieldWithoutRow => T("objects.diag.fieldWithoutRow", "A field line without a row above it.");

    public string ObjectsDiagSentenceMismatch => T("objects.diag.sentenceMismatch", "The line does not match the sentence pattern of the section.");

    public string ObjectsDiagFieldRepeated => T("objects.diag.fieldRepeated", "The field is repeated.");

    public string ObjectsDiagControlCharacter =>
        T("objects.diag.controlCharacter", "The value contains a tab, a line break or the escape sequence of the file format.");

    public string ObjectsDiagUnterminatedText => T("objects.diag.unterminatedText", "The text has no closing quote.");

    public string ObjectsDiagRootFieldMissing => T("objects.diag.rootFieldMissing", "A field of the object is missing; it keeps its value.");

    public string ObjectsDiagEmptyText => T("objects.diag.emptyText", "The text is empty.");

    public string ObjectsDiagKeyRepeated => T("objects.diag.keyRepeated", "The key is already used by another row.");

    public string ObjectsDiagDuplicateCountExceeded => T("objects.diag.duplicateCountExceeded", "More rows with this key than the object has.");

    public string ObjectsDiagRootKeyChanged => T("objects.diag.rootKeyChanged", "The key of the object cannot be changed here. Use Copy as.");

    public string ObjectsDiagInvalidValue => T("objects.diag.invalidValue", "The value does not fit the type of the field.");

    public string ObjectsDiagReferenceNotFound => T("objects.diag.referenceNotFound", "No row with this value in the target table.");

    public string ObjectsDiagTextWithoutOwnerKey => T("objects.diag.textWithoutOwnerKey", "The text belongs to a row without a key.");

    public string DiagnosticText(DiagnosticKind kind) => kind switch
    {
        DiagnosticKind.UnknownLineForm => ObjectsDiagUnknownLineForm,
        DiagnosticKind.HeadChanged => ObjectsDiagHeadChanged,
        DiagnosticKind.UnknownField => ObjectsDiagUnknownField,
        DiagnosticKind.UnknownSection => ObjectsDiagUnknownSection,
        DiagnosticKind.HiddenSection => ObjectsDiagHiddenSection,
        DiagnosticKind.FieldWithoutRow => ObjectsDiagFieldWithoutRow,
        DiagnosticKind.SentenceMismatch => ObjectsDiagSentenceMismatch,
        DiagnosticKind.FieldRepeated => ObjectsDiagFieldRepeated,
        DiagnosticKind.ControlCharacter => ObjectsDiagControlCharacter,
        DiagnosticKind.UnterminatedText => ObjectsDiagUnterminatedText,
        DiagnosticKind.RootFieldMissing => ObjectsDiagRootFieldMissing,
        DiagnosticKind.EmptyText => ObjectsDiagEmptyText,
        DiagnosticKind.KeyRepeated => ObjectsDiagKeyRepeated,
        DiagnosticKind.DuplicateCountExceeded => ObjectsDiagDuplicateCountExceeded,
        DiagnosticKind.RootKeyChanged => ObjectsDiagRootKeyChanged,
        DiagnosticKind.InvalidValue => ObjectsDiagInvalidValue,
        DiagnosticKind.ReferenceNotFound => ObjectsDiagReferenceNotFound,
        DiagnosticKind.TextWithoutOwnerKey => ObjectsDiagTextWithoutOwnerKey,
        _ => kind.ToString(),
    };

    // One text per WriteErrorKind.
    public string ObjectsWriteErrorSnapshotProblem => T("objects.writeError.snapshotProblem", "A project file or a pack could not be read.");

    public string ObjectsWriteErrorKeyAlreadyInFile => T("objects.writeError.keyAlreadyInFile", "The key is already in the file.");

    public string ObjectsWriteErrorRowNotInFile => T("objects.writeError.rowNotInFile", "The row was not found in the file.");

    public string ObjectsWriteErrorFieldNotInFileVersion =>
        T("objects.writeError.fieldNotInFileVersion", "The file has an older table version without this field.");

    public string ObjectsWriteErrorInvalidValue => T("objects.writeError.invalidValue", "A value does not fit the file.");

    public string ObjectsWriteErrorColourChannelOutOfRange =>
        T("objects.writeError.colourChannel", "A colour channel must be an integer from 0 to 255.");

    public string ObjectsWriteErrorTableNotWritable => T("objects.writeError.tableNotWritable", "The table cannot be written as text.");

    public string ObjectsWriteErrorNoAnswer => T("objects.writeError.noAnswer", "No file was chosen.");

    public string ObjectsWriteErrorOutdatedTableVersion => T("objects.writeError.outdatedTableVersion", "The table is in another version than the game uses. For a project file: delete the file or create the table again.");

    public string WriteErrorText(WriteErrorKind kind) => kind switch
    {
        WriteErrorKind.SnapshotProblem => ObjectsWriteErrorSnapshotProblem,
        WriteErrorKind.KeyAlreadyInFile => ObjectsWriteErrorKeyAlreadyInFile,
        WriteErrorKind.RowNotInFile => ObjectsWriteErrorRowNotInFile,
        WriteErrorKind.FieldNotInFileVersion => ObjectsWriteErrorFieldNotInFileVersion,
        WriteErrorKind.InvalidValue => ObjectsWriteErrorInvalidValue,
        WriteErrorKind.ColourChannelOutOfRange => ObjectsWriteErrorColourChannelOutOfRange,
        WriteErrorKind.TableNotWritable => ObjectsWriteErrorTableNotWritable,
        WriteErrorKind.NoAnswer => ObjectsWriteErrorNoAnswer,
        WriteErrorKind.OutdatedTableVersion => ObjectsWriteErrorOutdatedTableVersion,
        _ => kind.ToString(),
    };
}

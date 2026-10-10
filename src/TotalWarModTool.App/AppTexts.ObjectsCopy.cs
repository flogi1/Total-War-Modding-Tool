namespace TotalWarModTool.App;

// Texts of "Copy as" on an object tab (ObjectApplyController, ObjectTabViewModel, ObjectDialogs, the editor's context menu).
public sealed partial class AppTexts
{
    public string ObjectsCopyAs => T("objects.copyAs", "Copy as");

    /// <param name="objectName">The key of the object (data, escaped by the caller).</param>
    public string ObjectsCopyAsTitle(string objectName) => F("objects.copyAs.title", "Copy {0} as", objectName);

    public string ObjectsCopyAsLabel => T("objects.copyAs.label", "New key");

    // The checks of a new key (ObjectKeyRules).
    public string ObjectsCopyAsKeyEmpty => T("objects.copyAs.key.empty", "Enter a key.");

    public string ObjectsCopyAsKeyWhitespace => T("objects.copyAs.key.whitespace", "A key cannot contain spaces or tabs.");

    /// <param name="source">Where the key exists already (data, escaped by the caller).</param>
    public string ObjectsCopyAsKeyExists(string source) => F("objects.copyAs.key.exists", "This key already exists in {0}.", source);

    // Why "Copy as" is disabled (after the reasons it shares with Apply).
    public string ObjectsCopyAsNoRoot => T("objects.copyAs.reason.noRoot", "The object does not exist, there is nothing to copy.");

    public string ObjectsCopyAsCompositeKey => T("objects.copyAs.reason.compositeKey", "Objects with a composite key cannot be copied.");

    public string ObjectsCopyAsChecking => T("objects.copyAs.checking", "Preparing the copy");

    // Keys of the copy that exist already.
    public string ObjectsCopyAsTakenTitle => T("objects.copyAs.taken.title", "The copy cannot be written");

    public string ObjectsCopyAsTakenMessage(int count) => F("objects.copyAs.taken.message", "{0} keys of the copy already exist. Choose another key:", count);

    // The notes of a copy in the result line.
    /// <param name="table">The table (data, escaped by the caller).</param>
    public string ObjectsCopyAsNoteForeign(int rows, string table) => F("objects.copyAs.note.foreign", "not copied: {0} rows in {1}", rows, table);

    /// <param name="table">The table (data, escaped by the caller).</param>
    public string ObjectsCopyAsNoteDepth(int rows, string table) => F("objects.copyAs.note.depth", "too deep, not copied: {0} rows in {1}", rows, table);

    public string ObjectsCopyAsNoteNoChain => T("objects.copyAs.note.noChain", "the template has no chain");

    // The context menu of the object editor.
    public string ObjectsEditorCut => T("objects.editor.cut", "Cut");

    public string ObjectsEditorCopy => T("objects.editor.copy", "Copy");

    public string ObjectsEditorPaste => T("objects.editor.paste", "Paste");
}

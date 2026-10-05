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
}

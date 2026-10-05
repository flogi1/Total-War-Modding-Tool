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
}

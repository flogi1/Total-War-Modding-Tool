namespace TotalWarModTool.App;

/// <summary>What the user chose in the new table dialog: the table (for example <c>units_tables</c>) and the name part of the file.</summary>
public sealed record NewTableInput(string TableName, string Name);

/// <summary>Everything the file commands of a project ask the user. The fake in the tests records the calls and returns prepared answers.</summary>
public interface IFileDialogs
{
    /// <summary>Asks for a name. <paramref name="validate"/> returns the text that blocks OK, or null. Null when the user cancels.</summary>
    string? AskName(string title, string label, string suggestion, Func<string, string?> validate);

    /// <summary>Asks for a table of the list (with a filter box) and a file name. Null when the user cancels.</summary>
    NewTableInput? AskNewTable(IReadOnlyList<string> tables, string suggestion, Func<NewTableInput, string?> validate);

    /// <summary>Explicit confirmation that the new file has the same name as the entry it comes from and so replaces that whole file in the game.</summary>
    bool ConfirmSameName(string file);

    /// <summary>Tells the user that something could not be done. Shown as a warning.</summary>
    void ShowMessage(string title, string message);
}

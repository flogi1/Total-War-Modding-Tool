namespace TotalWarModTool.App;

/// <summary>Everything the apply of an object asks or tells the user. Display strings are escaped by the caller. The fake in the tests records the calls.</summary>
public interface IObjectDialogs
{
    /// <summary>Tells the user that something could not be done. Shown as a warning.</summary>
    void ShowMessage(string title, string message);

    /// <summary>"Apply, Discard, Cancel" for a tab with unapplied text that is about to move on or close. Closing the question is Cancel.</summary>
    SaveChoice AskUnapplied(string objectName);

    /// <summary>Which of the candidates (relative files, display form) takes the changes of the table (or of the loc texts). Null: cancel.</summary>
    string? AskTargetFile(string tableOrLoc, bool isLoc, IReadOnlyList<string> candidates);

    /// <summary>The copies an apply makes, one line each (file, row count, deleted keys); true for "Copy and delete", false for cancel.</summary>
    bool AskCopies(IReadOnlyList<string> lines);
}

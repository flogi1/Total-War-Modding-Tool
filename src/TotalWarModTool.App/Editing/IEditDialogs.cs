namespace TotalWarModTool.App;

/// <summary>The answer to the question what to do with unsaved changes.</summary>
public enum SaveChoice
{
    /// <summary>Save the files, then go on.</summary>
    Save,

    /// <summary>Throw the changes away and go on.</summary>
    Discard,

    /// <summary>Do not go on; the changes stay where they are.</summary>
    Cancel,
}

/// <summary>Everything the editing commands ask or tell the user. The fake in the tests records the calls.</summary>
public interface IEditDialogs
{
    /// <summary>Tells the user that something could not be done. Shown as a warning.</summary>
    void ShowMessage(string title, string message);

    /// <summary>
    /// Asks what to do with unsaved changes. <paramref name="files"/> are display names (already escaped); at most 10 are shown, then a count.
    /// Closing the question counts as <see cref="SaveChoice.Cancel"/>.
    /// </summary>
    SaveChoice AskUnsaved(IReadOnlyList<string> files);

    /// <summary>
    /// "Save all and continue" or cancel, asked before a build. <paramref name="files"/> as for <see cref="AskUnsaved"/>. True for "save all and
    /// continue"; closing the question counts as cancel (false).
    /// </summary>
    bool AskSaveAllBeforeBuild(IReadOnlyList<string> files);
}

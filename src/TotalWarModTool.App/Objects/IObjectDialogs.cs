using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>What the building dialog shows. ValidateKey refuses an empty key, spaces and tabs, a level key the catalog has and a key whose chain key it has.</summary>
public sealed record NewBuildingInput(BuildingCatalog Catalog, string? PreselectedSubculture, Func<string, string?> ValidateKey);

/// <summary>After null: a new chain. Else the stage the new building follows.</summary>
public sealed record NewBuildingAnswer(string Template, string Key, BuildingStage? After);

/// <summary>Everything the apply, the copy of an object and the wizards ask or tell the user. Display strings are escaped by the caller. The fake in the tests records the calls.</summary>
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

    /// <summary>The new key for a copy of objectName; the box starts with suggestion, validate gives the reason a key is refused or null. Null: cancel.</summary>
    string? AskNewKey(string objectName, string suggestion, Func<string, string?> validate);

    /// <summary>The dialog of the new building: subculture, template, key and chain. Null: cancel.</summary>
    NewBuildingAnswer? AskNewBuilding(NewBuildingInput input);
}

namespace TotalWarModTool.App;

// Texts of the object view that are used from the grids.
public sealed partial class AppTexts
{
    public string GridShowObject => T("grid.showObject", "Show as object");

    public string ObjectsNoKeyField => T("objects.reason.noKeyField", "This table has no key column, so its rows cannot be shown as objects.");

    public string ObjectsNoRowSelected => T("objects.reason.noRow", "Select a row first.");
}

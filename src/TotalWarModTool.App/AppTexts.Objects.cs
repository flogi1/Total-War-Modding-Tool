namespace TotalWarModTool.App;

// Texts of the object view that are used from the grids.
public sealed partial class AppTexts
{
    public string GridShowObject => T("grid.showObject", "Show as object");

    public string ObjectsNoKeyField => T("objects.reason.noKeyField", "This table has no key column, so its rows cannot be shown as objects.");

    public string ObjectsNoRowSelected => T("objects.reason.noRow", "Select a row first.");

    // The object tab.
    public string ObjectsTabSourceName => T("objects.tab.source", "Object");

    public string ObjectsBack => T("objects.back", "Back");

    public string ObjectsForward => T("objects.forward", "Forward");

    public string ObjectsRefresh => T("objects.refresh", "Refresh");

    public string ObjectsJumpToRow => T("objects.jumpToRow", "Go to row");

    /// <param name="mods">Number of mod packs in the load order.</param>
    public string ObjectsStatus(int mods) => F("objects.status", "Vanilla + {0} mods", mods);

    /// <param name="mods">Number of mod packs in the load order.</param>
    public string ObjectsStatusWithProject(int mods) => F("objects.status.project", "Vanilla + {0} mods + project", mods);

    /// <param name="loaded">Mod packs that could be read.</param>
    /// <param name="total">Mod packs of the list of active mods.</param>
    public string ObjectsStatusModsLoaded(int loaded, int total) => F("objects.status.modsLoaded", "{0} of {1} mods loaded", loaded, total);

    public string ObjectsStatusWarnings(int count) => F("objects.status.warnings", "{0} warnings", count);

    public string ObjectsStateIdle => T("objects.state.idle", "Not built yet. Refresh builds the object.");

    public string ObjectsStateBuilding => T("objects.state.building", "Building");

    public string ObjectsStateCancelled => T("objects.state.cancelled", "Cancelled. Refresh builds the object again.");

    public string ObjectsStateFailed => T("objects.state.failed", "The object could not be built. Refresh tries again; the log has details.");

    public string ObjectsNotFound => T("objects.notFound", "Object not found: no row with this key in the load order.");

    public string ObjectsNoticeUnsavedSource => T("objects.notice.unsavedSource", "The table has unsaved changes. This view shows the saved state.");

    public string ObjectsNoticeRootPackAdded => T("objects.notice.rootPackAdded",
        "The pack of this row is not active. It was added to the load order for this view.");

    public string ObjectsNoticeNoUsedMods => T("objects.notice.noUsedMods",
        "used_mods.txt was not found or cannot be read, so no mods are in the load order.");

    public string ObjectsNoticeCategoryUnreadable => T("objects.notice.categoryUnreadable",
        "The category file of this table could not be read. The default layout is used.");

    public string ObjectsNoticeRowSourceGone => T("objects.notice.rowSourceGone", "The pack or project of this row is no longer available.");

    public string ObjectsNoticeRowNotFound => T("objects.notice.rowNotFound", "The row was not found in the opened table.");

    public string ObjectsStale => T("objects.stale", "The sources of this object changed. Refresh builds it again.");

    /// <param name="count">Number of rows that are not shown.</param>
    public string ObjectsMoreRows(int count) => F("objects.moreRows", "+ {0} more rows", count);

    public string ObjectsDuplicate => T("objects.duplicate", "duplicate key");

    public string ObjectsOlderLayout => T("objects.olderLayout", "older layout");

    public string ObjectsTargetUnknown => T("objects.targetUnknown", "Target unknown: no row and no text for this value.");

    // The controller of the object view.
    public string ObjectsNoGame => T("objects.reason.noGame", "The game folder was not found, so the game data cannot be read.");

    public string ObjectsNoSchema => T("objects.reason.noSchema", "No schema is loaded, so the tables cannot be decoded.");

    /// <param name="key">The joined key of the object (data, escaped by the caller).</param>
    public string ObjectsBuilding(string key) => F("objects.building", "Building object {0}", key);

    // Titles of the shipped category files. The object tab translates the keys of the category data at run time;
    // the properties make the locale check see the keys as used.
    public string ObjectsCategoryBuilding => T("objects.category.building", "Building");

    public string ObjectsSectionNames => T("objects.section.names", "Names and descriptions");

    public string ObjectsSectionEffects => T("objects.section.effects", "Effects");

    public string ObjectsSectionUnits => T("objects.section.units", "Units");

    public string ObjectsSectionUpgradesTo => T("objects.section.upgradesTo", "Upgrades to");

    public string ObjectsSectionUpgradesFrom => T("objects.section.upgradesFrom", "Upgrades from");

    public string ObjectsSectionRequirements => T("objects.section.requirements", "Requirements");

    public string ObjectsSectionRequiredBuildings => T("objects.section.requiredBuildings", "Required buildings");

    public string ObjectsSectionDowngrades => T("objects.section.downgrades", "Downgrades");

    // Applying the text of an object tab.
    public string ObjectsApply => T("objects.apply", "Apply");

    public string ObjectsApplyGesture => T("objects.applyGesture", "Ctrl+Enter");

    public string ObjectsApplyNoProject => T("objects.apply.reason.noProject", "No active project to write into.");

    public string ObjectsApplyRootPackNotInOrder => T("objects.apply.reason.rootPack", "The pack of this object is not in the load order.");

    public string ObjectsApplyNotReady => T("objects.apply.reason.notReady", "The object is not built.");

    public string ObjectsApplyStale => T("objects.apply.reason.stale", "The object is outdated. Refresh it first.");

    public string ObjectsApplyNoChanges => T("objects.apply.reason.noChanges", "Nothing changed.");

    public string ObjectsApplyErrors(int count) => F("objects.apply.reason.errors", "{0} errors in the text.", count);

    public string ObjectsApplyChecking => T("objects.apply.checking", "Checking the changes");

    public string ObjectsApplyWriting => T("objects.apply.writing", "Applying the changes");

    /// <param name="rows">Rows set, inserted and deleted.</param>
    /// <param name="files">Files changed.</param>
    public string ObjectsApplyResultChanged(int rows, int files) => F("objects.apply.result.changed", "{0} rows in {1} files changed", rows, files);

    /// <param name="texts">Loc texts set and inserted.</param>
    public string ObjectsApplyResultTexts(int texts) => F("objects.apply.result.texts", "{0} texts", texts);

    /// <param name="files">Files created (copies included).</param>
    /// <param name="names">The created files, joined with ", " (data, escaped).</param>
    public string ObjectsApplyResultCreated(int files, string names) => F("objects.apply.result.created", "{0} files created: {1}", files, names);

    public string ObjectsApplyResultWarnings(int count) => F("objects.apply.result.warnings", "{0} warnings", count);

    public string ObjectsApplyResultNothing => T("objects.apply.result.nothing", "Nothing to write: the text has no change.");
}

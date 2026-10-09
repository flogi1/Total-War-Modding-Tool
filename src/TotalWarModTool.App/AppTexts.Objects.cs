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
}

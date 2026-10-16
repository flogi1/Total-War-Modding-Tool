using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

// Texts of the menu bar, the tile bar, the toolbar and the layout pages of the settings window.
public sealed partial class AppTexts
{
    public string MenuHelp => T("menu.help", "Help");

    /// <summary>Gesture of "Refresh pack list". Not bound to a menu item any more, so it has a text of its own.</summary>
    public string MenuToolsRefreshGesture => T("menu.tools.refreshGesture", "F5");

    public string TileNewBuildingTip => T("tile.newBuilding.tip", "Create a building from a template");

    public string TileNewTraitTip => T("tile.newTrait.tip", "Create a trait from a template");

    /// <summary>Label of a tile; an unknown id is shown as it is.</summary>
    public string TileLabel(string id) => id switch
    {
        TileIds.NewBuilding => MenuToolsNewBuilding,
        TileIds.NewTrait => MenuToolsNewTrait,
        _ => id,
    };

    /// <summary>Tooltip of a tile; an unknown id is shown as it is.</summary>
    public string TileTip(string id) => id switch
    {
        TileIds.NewBuilding => TileNewBuildingTip,
        TileIds.NewTrait => TileNewTraitTip,
        _ => id,
    };

    /// <summary>Label of a toolbar entry (the former menu text); an unknown id is shown as it is.</summary>
    public string ToolbarLabel(string id) => id switch
    {
        ToolbarItemIds.NewProject => ProjectNewTitle,
        ToolbarItemIds.OpenProject => MenuProjectOpen,
        ToolbarItemIds.ImportPack => MenuProjectImport,
        ToolbarItemIds.NewTable => FileNewTableTitle,
        ToolbarItemIds.NewLoc => FileNewLocTitle,
        ToolbarItemIds.Save => MenuProjectSave,
        ToolbarItemIds.SaveAll => MenuProjectSaveAll,
        ToolbarItemIds.Undo => MenuEditUndo,
        ToolbarItemIds.Redo => MenuEditRedo,
        ToolbarItemIds.RefreshPacks => OperationRefreshPacks,
        ToolbarItemIds.Build => BuildMessageTitle,
        ToolbarItemIds.BuildAndInstall => ProjectActionBuildAndInstall,
        _ => id,
    };

    /// <summary>Keyboard gesture of a toolbar entry, or null when it has none.</summary>
    public string? ToolbarGesture(string id) => id switch
    {
        ToolbarItemIds.Save => MenuProjectSaveGesture,
        ToolbarItemIds.SaveAll => MenuProjectSaveAllGesture,
        ToolbarItemIds.Undo => MenuEditUndoGesture,
        ToolbarItemIds.Redo => MenuEditRedoGesture,
        ToolbarItemIds.RefreshPacks => MenuToolsRefreshGesture,
        ToolbarItemIds.Build => MenuProjectBuildGesture,
        ToolbarItemIds.BuildAndInstall => MenuProjectBuildAndInstallGesture,
        _ => null,
    };

    /// <summary>Tooltip of a toolbar entry: the label, plus the gesture when there is one.</summary>
    public string ToolbarTip(string id) =>
        ToolbarGesture(id) is { } gesture ? ToolbarToolTip(ToolbarLabel(id), gesture) : ToolbarLabel(id);

    public string ToolbarPositionTitle => T("toolbar.position", "Toolbar position");

    public string ToolbarPositionTop => T("toolbar.position.top", "Top");

    public string ToolbarPositionLeft => T("toolbar.position.left", "Left");

    public string ToolbarPositionRight => T("toolbar.position.right", "Right");

    public string ToolbarPositionBottom => T("toolbar.position.bottom", "Bottom");

    public string ToolbarPositionLabel(ToolbarPosition position) => position switch
    {
        ToolbarPosition.Left => ToolbarPositionLeft,
        ToolbarPosition.Right => ToolbarPositionRight,
        ToolbarPosition.Bottom => ToolbarPositionBottom,
        _ => ToolbarPositionTop,
    };

    public string SettingsCategoryLayout => T("settings.category.layout", "Layout");

    public string SettingsCategoryTiles => T("settings.category.tiles", "Tile bar");

    public string SettingsCategoryToolbar => T("settings.category.toolbar", "Toolbar");

    public string SettingsToolbarPosition => T("settings.layout.toolbarPosition", "Position of the toolbar");

    public string SettingsToolbarLabels => T("settings.layout.toolbarLabels", "Show labels in the toolbar");

    public string SettingsTileBarVisible => T("settings.layout.tileBarVisible", "Show the tile bar");

    public string SettingsTileSize => T("settings.layout.tileSize", "Tile size");

    public string SettingsTileSizeLarge => T("settings.layout.tileSize.large", "Large");

    public string SettingsTileSizeMedium => T("settings.layout.tileSize.medium", "Medium");

    public string SettingsTileSizeCompact => T("settings.layout.tileSize.compact", "Compact, icon only");

    public string SettingsTileLabels => T("settings.layout.tileLabels", "Show tile labels");

    public string SettingsTileOrderHint => T("settings.tiles.orderHint", "Drag a tile or use the arrows to change the order. Hidden tiles keep their place.");

    public string SettingsTileVisible => T("settings.tiles.visible", "Visible");

    public string SettingsTileIconOnly => T("settings.tiles.iconOnly", "Icon only");

    public string SettingsTileReset => T("settings.tiles.reset", "Restore the default order");

    public string SettingsToolbarOrderHint => T("settings.toolbar.orderHint", "Drag an entry or use the arrows. Separators are entries of their own.");

    public string SettingsToolbarSeparator => T("settings.toolbar.separator", "Separator");

    public string SettingsToolbarAddSeparator => T("settings.toolbar.addSeparator", "Add separator");

    public string SettingsToolbarReset => T("settings.toolbar.reset", "Restore the default order");

    public string ReorderMoveUp => T("reorder.moveUp", "Move up");

    public string ReorderMoveDown => T("reorder.moveDown", "Move down");

    public string ReorderRemove => T("reorder.remove", "Remove");
}

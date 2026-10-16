using TotalWarModTool.Storage;

namespace TotalWarModTool.Settings.Tests;

public class GlobalSettingsBarsTests
{
    [Fact] public void Defaults_are_the_catalog_orders_everything_visible_large_top_labels_on()
    {
        var s = new GlobalSettings();
        Assert.Equal(TileCatalog.DefaultOrder, s.TileOrder); Assert.Equal(ToolbarCatalog.DefaultOrder, s.ToolbarOrder);
        Assert.Equal((TileSize.Large, ToolbarPosition.Top, true, true, true, 0), (s.TileSize, s.ToolbarPosition, s.TileLabelsVisible, s.TileBarVisible, s.ToolbarLabelsVisible, s.SettingsCategory));
        Assert.True(s.IsTileVisible("newTrait")); Assert.True(s.IsToolbarItemVisible("build")); Assert.False(s.IsTileIconOnly("newTrait"));
    }

    [Fact] public void Setting_an_order_normalizes_it_and_raises_Changed_once()
    {
        var s = new GlobalSettings(); var changes = new List<string>(); s.Changed += (_, n) => changes.Add(n);
        s.TileOrder = ["newTrait", "gone", "newTrait"];
        s.TileOrder = ["newTrait"];                                  // same normalized result: no event
        Assert.Equal(["newTrait", "newBuilding"], s.TileOrder); Assert.Equal(["TileOrder"], changes);
    }

    [Fact] public void Toolbar_order_keeps_separators_and_appends_missing_ids_at_the_end()
    {
        var s = new GlobalSettings { ToolbarOrder = ["build", "sep:7", "save"] };
        Assert.Equal(["build", "sep:7", "save", "newProject", "openProject", "importPack", "newTable", "newLoc", "saveAll", "undo", "redo", "refreshPacks", "buildAndInstall"], s.ToolbarOrder);
    }

    [Fact] public void Hidden_and_icon_only_sets_raise_Changed_only_on_a_real_change()
    {
        var s = new GlobalSettings(); var changes = new List<string>(); s.Changed += (_, n) => changes.Add(n);
        s.SetTileVisible("newTrait", false); s.SetTileVisible("newTrait", false); s.SetTileIconOnly("newTrait", true); s.SetToolbarItemVisible("undo", false);
        Assert.Equal(["HiddenTiles", "IconOnlyTiles", "HiddenToolbarItems"], changes);
        Assert.Equal((false, true, false), (s.IsTileVisible("newTrait"), s.IsTileIconOnly("newTrait"), s.IsToolbarItemVisible("undo")));
    }

    [Fact] public void Scalar_bar_settings_raise_Changed_only_on_a_real_change_and_negative_category_becomes_zero()
    {
        var s = new GlobalSettings(); var changes = new List<string>(); s.Changed += (_, n) => changes.Add(n);
        s.TileSize = TileSize.Large; s.TileSize = TileSize.Medium; s.ToolbarPosition = ToolbarPosition.Bottom;
        s.TileLabelsVisible = true; s.TileLabelsVisible = false; s.TileBarVisible = false; s.ToolbarLabelsVisible = false;
        s.SettingsCategory = 0; s.SettingsCategory = -4; s.SettingsCategory = 2; s.SettingsCategory = -1;
        Assert.Equal(["TileSize", "ToolbarPosition", "TileLabelsVisible", "TileBarVisible", "ToolbarLabelsVisible", "SettingsCategory", "SettingsCategory"], changes);
        Assert.Equal(0, s.SettingsCategory);
    }

    [Fact] public void The_store_round_trips_every_bar_field()
    {
        using var dir = new TempDir(); var store = new GlobalSettingsStore(dir.Path);
        var s = new GlobalSettings { TileOrder = ["newTrait"], TileSize = TileSize.Compact, TileLabelsVisible = false, TileBarVisible = false,
            ToolbarOrder = ["build", "sep:2", "save"], ToolbarPosition = ToolbarPosition.Left, ToolbarLabelsVisible = false, SettingsCategory = 3 };
        s.SetTileVisible("newBuilding", false); s.SetTileIconOnly("newTrait", true); s.SetToolbarItemVisible("undo", false);
        store.Save(s); var l = store.Load().Settings;
        Assert.Equal(s.TileOrder, l.TileOrder); Assert.Equal(s.ToolbarOrder, l.ToolbarOrder);
        Assert.Equal((TileSize.Compact, false, false, ToolbarPosition.Left, false, 3), (l.TileSize, l.TileLabelsVisible, l.TileBarVisible, l.ToolbarPosition, l.ToolbarLabelsVisible, l.SettingsCategory));
        Assert.Equal((false, true, false), (l.IsTileVisible("newBuilding"), l.IsTileIconOnly("newTrait"), l.IsToolbarItemVisible("undo")));
    }

    [Fact] public void An_old_file_without_the_fields_loads_with_defaults_and_unknown_ids_are_dropped()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), """{"schemaVersion":1,"language":"de","tileOrder":["newBuilding","gone","newTrait"],"hiddenTiles":["gone"],"tileSize":"Huge","toolbarPosition":"Middle"}""");
        var l = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal(LoadStatus.Loaded, l.Status);
        Assert.Equal(["newBuilding", "newTrait"], l.Settings.TileOrder); Assert.Empty(l.Settings.HiddenTiles);
        Assert.Equal(ToolbarCatalog.DefaultOrder, l.Settings.ToolbarOrder);
        Assert.Equal((TileSize.Large, ToolbarPosition.Top), (l.Settings.TileSize, l.Settings.ToolbarPosition));
    }

    // Review focus 1: an id the catalog does not know must not break the load; the known ids keep their order.
    [Fact] public void An_unknown_tile_id_in_the_middle_vanishes_silently_and_the_known_order_stays()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), """{"schemaVersion":1,"tileOrder":["newTrait","gone","newBuilding"]}""");
        var l = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal((LoadStatus.Loaded, null), (l.Status, l.BadCopyPath));
        Assert.Equal(["newTrait", "newBuilding"], l.Settings.TileOrder);
    }

    // Review focus 2: no separators and some ids missing: missing ids are completed from the catalog, no separator is invented.
    [Fact] public void A_toolbar_order_without_separators_and_with_missing_ids_is_completed_without_inventing_separators()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), """{"schemaVersion":1,"toolbarOrder":["build","save","gone","undo"],"hiddenToolbarItems":["gone","redo"]}""");
        var l = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal(["build", "save", "undo", "newProject", "openProject", "importPack", "newTable", "newLoc", "saveAll", "redo", "refreshPacks", "buildAndInstall"], l.Settings.ToolbarOrder);
        Assert.DoesNotContain(l.Settings.ToolbarOrder, ToolbarCatalog.IsSeparator);
        Assert.Equal(["redo"], l.Settings.HiddenToolbarItems);
    }

    [Fact] public void Null_lists_and_values_in_the_file_load_as_defaults()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), """{"schemaVersion":1,"tileOrder":null,"hiddenTiles":null,"iconOnlyTiles":null,"toolbarOrder":null,"hiddenToolbarItems":null,"tileLabelsVisible":null,"settingsCategory":-5}""");
        var s = new GlobalSettingsStore(dir.Path).Load().Settings;
        Assert.Equal(TileCatalog.DefaultOrder, s.TileOrder); Assert.Equal(ToolbarCatalog.DefaultOrder, s.ToolbarOrder);
        Assert.Equal((true, 0), (s.TileLabelsVisible, s.SettingsCategory));
        Assert.Empty(s.HiddenTiles); Assert.Empty(s.IconOnlyTiles); Assert.Empty(s.HiddenToolbarItems);
    }
}

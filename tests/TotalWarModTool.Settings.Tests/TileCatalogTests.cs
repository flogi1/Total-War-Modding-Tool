namespace TotalWarModTool.Settings.Tests;

public class TileCatalogTests
{
    [Fact] public void The_catalog_lists_building_and_trait_in_this_order() =>
        Assert.Equal(["newBuilding", "newTrait"], TileCatalog.DefaultOrder);

    [Fact] public void Both_tiles_have_no_menu_and_are_visible_without_stored_settings() =>
        Assert.All(TileCatalog.All, t => Assert.Equal((false, true), (t.HasMenu, new GlobalSettings().IsTileVisible(t.Id))));

    [Fact] public void Find_and_IsKnown_reject_unknown_ids() =>
        Assert.Equal((false, (TileItem?)null), (TileCatalog.IsKnown("gone"), TileCatalog.Find("gone")));
}

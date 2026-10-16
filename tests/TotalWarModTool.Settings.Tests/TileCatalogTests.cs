namespace TotalWarModTool.Settings.Tests;

public class TileCatalogTests
{
    [Fact] public void The_catalog_lists_building_and_trait_in_this_order() =>
        Assert.Equal(["newBuilding", "newTrait"], TileCatalog.DefaultOrder);

    [Fact] public void Both_tiles_are_visible_by_default_and_have_no_menu() =>
        Assert.All(TileCatalog.All, t => Assert.Equal((true, false), (t.DefaultVisible, t.HasMenu)));

    [Fact] public void Find_and_IsKnown_reject_unknown_ids() =>
        Assert.Equal((false, (TileItem?)null), (TileCatalog.IsKnown("gone"), TileCatalog.Find("gone")));
}

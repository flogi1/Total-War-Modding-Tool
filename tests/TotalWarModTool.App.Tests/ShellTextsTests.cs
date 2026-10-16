using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class ShellTextsTests
{
    private static string Locales => Path.Combine(RepoRoot.Find(), "locales");

    private static AppTexts NewTexts() => new(new UiText(Locales));

    [Fact] public void Every_toolbar_id_has_a_label_and_the_gestures_match_the_menu()
    {
        var texts = NewTexts();
        Assert.All(ToolbarCatalog.All, id => Assert.False(string.IsNullOrWhiteSpace(texts.ToolbarLabel(id)), id));
        Assert.Equal(("Ctrl+S", "F5", (string?)null), (texts.ToolbarGesture("save"), texts.ToolbarGesture("refreshPacks"), texts.ToolbarGesture("newProject")));
        Assert.Equal("Save (Ctrl+S)", texts.ToolbarTip("save"));
        Assert.Equal("New project", texts.ToolbarTip("newProject"));
        Assert.Equal("Build (Ctrl+B)", texts.ToolbarTip("build"));
    }

    [Fact] public void Both_tiles_have_label_and_tip()
    {
        var texts = NewTexts();
        Assert.All(TileCatalog.DefaultOrder, id => Assert.NotEqual(texts.TileLabel(id), texts.TileTip(id)));
    }

    [Fact] public void Every_toolbar_position_has_a_distinct_label()
    {
        var texts = NewTexts();
        var labels = Enum.GetValues<ToolbarPosition>().Select(texts.ToolbarPositionLabel).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }
}

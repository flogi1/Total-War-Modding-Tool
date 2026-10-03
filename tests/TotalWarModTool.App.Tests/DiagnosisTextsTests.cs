using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class DiagnosisTextsTests
{
    private static AppTexts Texts() => new(new UiText(@"X:\none"));

    [Fact] public void Every_problem_value_has_a_text()
    {
        var t = Texts();
        Assert.All(Enum.GetValues<PackProblem>().Where(p => p != PackProblem.None), p => Assert.NotEqual("", t.PackProblemText(p, "PFH5")));
        Assert.All(Enum.GetValues<EntryProblem>().Where(p => p != EntryProblem.None), p => Assert.NotEqual("", t.EntryProblemText(p)));
        Assert.All(Enum.GetValues<TableDecodeStatus>().Where(s => s != TableDecodeStatus.Ok), s => Assert.NotEqual("", t.TableStatusText(s)));
        Assert.All(Enum.GetValues<SchemaProblem>().Where(p => p != SchemaProblem.None), p => Assert.NotEqual("", t.SchemaProblemText(p)));
        Assert.All(Enum.GetValues<PackOrigin>(), o => Assert.NotEqual("", t.OriginName(o)));
        Assert.Equal(("", "", ""), (t.PackProblemText(PackProblem.None, null), t.EntryProblemText(EntryProblem.None), t.TableStatusText(TableDecodeStatus.Ok)));
    }

    [Fact] public void Unknown_format_names_the_format_of_the_game() =>
        Assert.Equal("Unknown pack format. This game uses PFH5.", Texts().PackProblemText(PackProblem.UnknownFormat, "PFH5"));

    [Fact] public void A_changed_or_removed_pack_tells_the_user_to_refresh() =>
        Assert.Equal("The pack was changed or removed on disk. Refresh the pack list and open the entry again.", Texts().EntryProblemText(EntryProblem.PackChanged));

    [Fact] public void An_unknown_origin_is_named_by_its_value() =>
        Assert.Equal("99", Texts().OriginName((PackOrigin)99));

    [Fact] public void Operation_titles_carry_the_name() =>
        Assert.Equal(("Open a.pack", "Open data__"), (Texts().OperationOpenPack("a.pack"), Texts().OperationOpenEntry("data__")));
}

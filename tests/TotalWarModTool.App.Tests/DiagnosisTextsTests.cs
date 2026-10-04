using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
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
        Assert.All(Enum.GetValues<ProjectProblem>().Where(p => p != ProjectProblem.None), p => Assert.NotEqual("", t.ProjectProblemText(p)));
        Assert.Equal("", t.ProjectProblemText(ProjectProblem.None));
        Assert.All(Enum.GetValues<PackOrigin>(), o => Assert.NotEqual("", t.OriginName(o)));
        Assert.Equal(("", "", ""), (t.PackProblemText(PackProblem.None, null), t.EntryProblemText(EntryProblem.None), t.TableStatusText(TableDecodeStatus.Ok)));
    }

    [Fact] public void Unknown_format_names_the_format_of_the_game() =>
        Assert.Equal("Unknown pack format. This game uses PFH5.", Texts().PackProblemText(PackProblem.UnknownFormat, "PFH5"));

    [Fact] public void A_changed_or_removed_pack_tells_the_user_to_refresh() =>
        Assert.Equal("The pack was changed or removed on disk. Refresh the pack list and open the entry again.", Texts().EntryProblemText(EntryProblem.PackChanged));

    [Fact] public void Schema_source_names_the_origin_and_the_date_of_the_file()
    {
        var date = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();
        Assert.Equal(("shipped with the app, 2026-09-24", "from the schema folder, 2026-09-24", "none", "none"),
            (Texts().SchemaSourceText(new ResolvedSchema(null, SchemaOrigin.Shipped, null, date, null, null)),
             Texts().SchemaSourceText(new ResolvedSchema(null, SchemaOrigin.Override, null, date, null, null)),
             Texts().SchemaSourceText(new ResolvedSchema(null, SchemaOrigin.None, null, null, null, null)), Texts().SchemaSourceText(null)));
    }

    // Without a file date the text must not end in a dangling comma.
    [Fact] public void Schema_source_without_a_date_has_no_dangling_comma() =>
        Assert.Equal(("shipped with the app", "from the schema folder"),
            (Texts().SchemaSourceText(new ResolvedSchema(null, SchemaOrigin.Shipped, null, null, null, null)),
             Texts().SchemaSourceText(new ResolvedSchema(null, SchemaOrigin.Override, null, null, null, null))));

    [Fact] public void An_unknown_origin_is_named_by_its_value() =>
        Assert.Equal("99", Texts().OriginName((PackOrigin)99));

    [Fact] public void Operation_titles_carry_the_name() =>
        Assert.Equal(("Open a.pack", "Open data__"), (Texts().OperationOpenPack("a.pack"), Texts().OperationOpenEntry("data__")));
}

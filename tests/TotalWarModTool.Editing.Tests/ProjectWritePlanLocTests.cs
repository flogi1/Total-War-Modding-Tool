using TotalWarModTool.Objects;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Tables;
using static TotalWarModTool.Editing.Tests.ProjectWritePlanTests;
using static TotalWarModTool.Editing.Tests.WriteFixture;

namespace TotalWarModTool.Editing.Tests;

public class ProjectWritePlanLocTests
{
    private const string LocA = @"text\db\a.loc.tsv";
    private const string LocB = @"text\db\b.loc.tsv";

    private static SnapshotLocFile LocFile(string relativeFile, LocRow[] rows, bool isCopy = false)
    {
        var packPath = ProjectLayout.FromRelativeFile(relativeFile).PackPath;
        var text = TableTsv.WriteLoc(packPath, rows).Text!;
        var parsed = TableTsv.ReadLoc(text).Loc!;
        return new SnapshotLocFile(relativeFile, packPath, new FileFingerprint(@"C:\p\pack\" + relativeFile, text.Length, DateTime.UnixEpoch), text, parsed, isCopy);
    }

    private static ProjectSnapshot SnapshotOf(SnapshotTableFile[] tables, params SnapshotLocFile[] locs) => new(tables, locs, [], []);

    private static ObjectChanges Texts(params TextChange[] texts) => Changes([], texts);

    private static TextChange Set(string key, string text, int line = 2) => new(TextChangeKind.Set, key, text, Vanilla("v.pack"), line);

    private static TextChange Insert(string key, string text, int line = 2) => new(TextChangeKind.Insert, key, text, null, line);

    private static LocRow[] LocRows(string text) => TableTsv.ReadLoc(text).Loc!.Rows.ToArray();

    [Fact]
    public void A_text_whose_key_a_project_loc_file_holds_is_replaced_there()
    {
        var snapshot = SnapshotOf([], LocFile(LocA, [new LocRow("x_name_k1", "Old", true)]), LocFile(LocB, []));

        var plan = Plan(Texts(Set("x_name_k1", "New")), snapshot);

        Assert.Empty(plan.TargetQuestions);
        var planned = Assert.Single(plan.Files);
        Assert.Equal((LocA, PlannedFileKind.Changed), (planned.RelativeFile, planned.Kind));
        Assert.Equal([new LocRow("x_name_k1", "New", true)], LocRows(planned.NewText));
    }

    [Fact]
    public void A_text_of_a_vanilla_entry_is_appended_to_the_only_project_loc_file()
    {
        var snapshot = SnapshotOf([], LocFile(LocA, [new LocRow("other", "o", false)]));

        var plan = Plan(Texts(Set("x_name_k1", "New")), snapshot);

        var planned = Assert.Single(plan.Files);
        Assert.Equal([new LocRow("other", "o", false), new LocRow("x_name_k1", "New", false)], LocRows(planned.NewText));
        Assert.Equal(1, plan.Summary.TextsSet);
    }

    [Fact]
    public void Without_a_loc_file_a_new_one_is_created_named_after_the_project()
    {
        var plan = Plan(Texts(Insert("x_name_k9", "N")), SnapshotOf([]), "Flo");

        var planned = Assert.Single(plan.Files);
        Assert.Equal((@"text\db\Flo.loc.tsv", PlannedFileKind.Created), (planned.RelativeFile, planned.Kind));
        Assert.Null(planned.Previous);
        Assert.Contains("#Loc;1;text/db/Flo.loc", planned.NewText);
        Assert.Equal([new LocRow("x_name_k9", "N", false)], LocRows(planned.NewText));
        Assert.Equal((1, 1), (plan.Summary.TextsInserted, plan.Summary.FilesCreated));
    }

    [Fact]
    public void A_loc_copy_does_not_count_as_the_project_loc_file()
    {
        var copy = LocFile(@"text\db\data__.loc.tsv", [new LocRow("other", "o", false)], isCopy: true);

        var plan = Plan(Texts(Insert("x_name_k9", "N")), SnapshotOf([], copy), "Flo");

        Assert.Equal(PlannedFileKind.Created, Assert.Single(plan.Files).Kind);
    }

    [Fact]
    public void Several_loc_files_ask_once_for_all_texts()
    {
        var snapshot = SnapshotOf([], LocFile(LocA, []), LocFile(LocB, []));
        var changes = Texts(Insert("x_name_k8", "M", 3), Insert("x_name_k9", "N", 9));

        var asked = Plan(changes, snapshot);
        var answered = Plan(changes, snapshot, answers: new WriteAnswers(new Dictionary<string, string> { [WriteAnswers.LocKey] = LocB }, false));

        var question = Assert.Single(asked.TargetQuestions);
        Assert.Equal((WriteAnswers.LocKey, true), (question.Table, question.IsLoc));
        Assert.Equal([LocA, LocB], question.Candidates);
        Assert.Equal([3, 9], question.Lines);
        Assert.Empty(asked.Files);
        var planned = Assert.Single(answered.Files);
        Assert.Equal(LocB, planned.RelativeFile);
        Assert.Equal(["x_name_k8", "x_name_k9"], LocRows(planned.NewText).Select(r => r.Key).ToArray());
    }

    [Fact]
    public void Table_and_loc_changes_of_one_write_land_in_their_own_files()
    {
        var table = TableFile(@"db\x_tables\a.tsv", X, 2, [XRow("k1")]);
        var snapshot = SnapshotOf([table], LocFile(LocA, [new LocRow("x_name_k1", "Old", false)]));
        var row = Change(RowChangeKind.Set, X, ["k1", "b1", "x", "1"], ["k1", "b1", "n", "1"], From(Project("proj"), @"pack\db\x_tables\a.tsv"), 3, "note");
        var changes = Changes([row], [Set("x_name_k1", "New")]);

        var plan = Plan(changes, snapshot);

        Assert.Equal([@"db\x_tables\a.tsv", LocA], plan.Files.Select(f => f.RelativeFile).ToArray());
    }
}

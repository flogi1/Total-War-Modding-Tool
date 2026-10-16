using TotalWarModTool.Objects;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Tables;
using static TotalWarModTool.Editing.Tests.ProjectWritePlanTests;
using static TotalWarModTool.Editing.Tests.WriteFixture;

namespace TotalWarModTool.Editing.Tests;

public class ProjectWritePlanDeleteTests
{
    private const string A = @"db\x_tables\a.tsv";
    private const string Data = @"db\x_tables\data__.tsv";
    private const string M = @"db\x_tables\m.tsv";

    private static readonly TableSource V = Vanilla("v.pack");
    private static readonly TableSource Md = Mod("m.pack");
    private static readonly WriteAnswers Confirmed = new(new Dictionary<string, string>(), true);

    private static SnapshotPackEntry PackEntry(TableSource source, string entryPath, string table, int version, object?[][] rows)
    {
        var packPath = entryPath;
        var text = TableTsv.Write(table, version, packPath, Definition(table, version), rows).Text!;
        var parsed = TableTsv.Read(text, MiniSchema).Table!;
        return new SnapshotPackEntry(source, entryPath, ProjectLayout.ToRelativeFile(packPath, ProjectFileKind.Table), parsed);
    }

    private static ProjectSnapshot Snapshot(SnapshotTableFile[] files, params SnapshotPackEntry[] entries) => new(files, [], entries, []);

    private static RowChange Delete(string key, RowProvenance provenance, int line = 4) =>
        Change(RowChangeKind.Delete, X, [key, "b1", "n", "1"], [key, "b1", "n", "1"], provenance, line);

    private static RowProvenance InProject(string relativeFile, params OverriddenLayer[] overridden) =>
        From(Project("proj"), @"pack\" + relativeFile, overridden);

    private static SnapshotPackEntry DataEntry(params string[] keys) =>
        PackEntry(V, "db/x_tables/data__", X, 1, keys.Select(k => (object?[])[k, "b1", "n"]).ToArray());

    private static object?[][] V1(params string[] keys) => keys.Select(k => (object?[])[k, "b1", "n"]).ToArray();

    [Fact]
    public void Deleting_a_project_row_removes_it_from_its_file()
    {
        var file = TableFile(A, X, 2, [XRow("k1"), XRow("k2")]);

        var plan = Plan(Changes([Delete("k1", InProject(A))]), Snapshot([file]));

        var planned = Assert.Single(plan.Files);
        Assert.Equal([XRow("k2")], Rows(planned.NewText));
        Assert.Equal(1, plan.Summary.RowsDeleted);
        Assert.False(plan.HasQuestions);
        Assert.False(plan.HasErrors);
    }

    [Fact]
    public void Deleting_the_last_row_keeps_the_file_with_its_header()
    {
        var file = TableFile(A, X, 2, [XRow("k1")]);

        var plan = Plan(Changes([Delete("k1", InProject(A))]), Snapshot([file]));

        var planned = Assert.Single(plan.Files);
        Assert.Equal(PlannedFileKind.Changed, planned.Kind);
        Assert.Equal(TableTsv.Write(X, 2, "db/x_tables/a", Definition(X, 2), []).Text, planned.NewText);
    }

    [Fact]
    public void Deleting_a_vanilla_row_asks_for_a_copy_and_the_copy_lacks_the_row()
    {
        var entry = DataEntry("k1", "k2", "k3");
        var changes = Changes([Delete("k2", From(V, "db/x_tables/data__"), line: 4)]);

        var asked = Plan(changes, Snapshot([], entry));
        var confirmed = Plan(changes, Snapshot([], entry), answers: Confirmed);

        var question = Assert.Single(asked.CopyQuestions);
        Assert.Equal((V, "db/x_tables/data__", Data, 3), (question.Source, question.EntryPath, question.RelativeFile, question.RowCount));
        Assert.Equal(["k2"], question.DeletedKeys);
        Assert.Empty(asked.Files);
        var copy = Assert.Single(confirmed.Files);
        Assert.Equal((Data, PlannedFileKind.Copy, "db/x_tables/data__"), (copy.RelativeFile, copy.Kind, copy.SourceEntry));
        Assert.Null(copy.Previous);
        Assert.Null(copy.PreviousText);
        var table = TableTsv.Read(copy.NewText, MiniSchema).Table!;
        Assert.Equal(1, table.Version);
        Assert.Equal(V1("k1", "k3"), table.Rows.ToArray());
        Assert.Equal((1, 1), (confirmed.Summary.Copies, confirmed.Summary.RowsDeleted));
    }

    [Fact]
    public void Two_deletes_from_the_same_source_file_make_one_copy()
    {
        var provenance = From(V, "db/x_tables/data__");

        var plan = Plan(Changes([Delete("k1", provenance, 4), Delete("k3", provenance, 5)]), Snapshot([], DataEntry("k1", "k2", "k3")), answers: Confirmed);

        var copy = Assert.Single(plan.Files);
        Assert.Equal(V1("k2"), Rows(copy.NewText));
        var asked = Plan(Changes([Delete("k1", provenance, 4), Delete("k3", provenance, 5)]), Snapshot([], DataEntry("k1", "k2", "k3")));
        var question = Assert.Single(asked.CopyQuestions);
        Assert.Equal(["k1", "k3"], question.DeletedKeys);
        Assert.Equal(3, question.RowCount);
    }

    [Fact]
    public void Deleting_a_row_that_overrides_two_layers_copies_both_files()
    {
        var file = TableFile(A, X, 2, [XRow("k1"), XRow("k2")]);
        var provenance = InProject(A, new OverriddenLayer(V, "db/x_tables/data__"), new OverriddenLayer(Md, "db/x_tables/m"));
        var snapshot = Snapshot([file], DataEntry("k1", "k5"), PackEntry(Md, "db/x_tables/m", X, 1, V1("k1", "k6")));

        var asked = Plan(Changes([Delete("k1", provenance)]), snapshot);
        var plan = Plan(Changes([Delete("k1", provenance)]), snapshot, answers: Confirmed);

        Assert.Equal(2, asked.CopyQuestions.Count);
        Assert.Equal([A, Data, M], plan.Files.Select(f => f.RelativeFile).ToArray());
        Assert.Equal([PlannedFileKind.Changed, PlannedFileKind.Copy, PlannedFileKind.Copy], plan.Files.Select(f => f.Kind).ToArray());
        Assert.Equal([XRow("k2")], Rows(plan.Files[0].NewText));
        Assert.Equal(V1("k5"), Rows(plan.Files[1].NewText));
        Assert.Equal(V1("k6"), Rows(plan.Files[2].NewText));
    }

    [Fact]
    public void An_overridden_project_layer_is_served_by_key()
    {
        var file = TableFile(A, X, 2, [XRow("k1", note: "other", extra: 9), XRow("k2")]);
        var provenance = From(Md, "db/x_tables/m", new OverriddenLayer(Project("proj"), @"pack\" + A));
        var snapshot = Snapshot([file], PackEntry(Md, "db/x_tables/m", X, 1, V1("k1", "k6")));

        var plan = Plan(Changes([Delete("k1", provenance)]), snapshot, answers: Confirmed);

        Assert.Equal([A, M], plan.Files.Select(f => f.RelativeFile).ToArray());
        Assert.Equal([XRow("k2")], Rows(plan.Files[0].NewText));
        Assert.Equal(V1("k6"), Rows(plan.Files[1].NewText));
    }

    [Fact]
    public void A_row_whose_only_key_is_the_root_field_is_found_by_its_previous_cells()
    {
        var file = TableFile(@"db\z_tables\a.tsv", Z, 1, [["b1", 1], ["b1", 2]]);
        var change = Change(RowChangeKind.Delete, Z, ["b1", "2"], ["b1", "2"], From(Project("proj"), @"pack\db\z_tables\a.tsv"), 3);

        var plan = Plan(Changes([change]), Snapshot([file]));

        Assert.Equal([["b1", 1]], Rows(Assert.Single(plan.Files).NewText));
    }

    [Fact]
    public void Of_two_identical_keyless_rows_only_the_first_is_deleted()
    {
        var file = TableFile(@"db\y_tables\a.tsv", Y, 1, [["b1", "a", "b"], ["b1", "a", "b"]]);
        var change = Change(RowChangeKind.Delete, Y, ["b1", "a", "b"], ["b1", "a", "b"], From(Project("proj"), @"pack\db\y_tables\a.tsv"), 3);

        var plan = Plan(Changes([change]), Snapshot([file]));

        Assert.Equal([["b1", "a", "b"]], Rows(Assert.Single(plan.Files).NewText));
    }

    [Fact]
    public void A_missing_row_in_the_source_is_an_error()
    {
        var plan = Plan(Changes([Delete("k9", From(V, "db/x_tables/data__"), line: 6)]), Snapshot([], DataEntry("k1")), answers: Confirmed);

        var error = Assert.Single(plan.Errors);
        Assert.Equal((WriteErrorKind.RowNotInFile, 6, Data), (error.Kind, error.Line, error.RelativeFile));
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void A_pack_entry_missing_from_the_snapshot_is_an_error()
    {
        var plan = Plan(Changes([Delete("k1", From(V, "db/x_tables/data__"), line: 6)]), Snapshot([]), answers: Confirmed);

        var error = Assert.Single(plan.Errors);
        Assert.Equal((WriteErrorKind.SnapshotProblem, 6), (error.Kind, error.Line));
    }

    [Fact]
    public void A_project_file_with_the_name_of_the_copy_is_the_target_instead()
    {
        var existing = TableFile(Data, X, 1, [["k1", "b1", "n"], ["k2", "b1", "n"]], isCopy: true);

        var asked = Plan(Changes([Delete("k1", From(V, "db/x_tables/data__"))]), Snapshot([existing], DataEntry("k1", "k2")));

        Assert.Empty(asked.CopyQuestions);
        var planned = Assert.Single(asked.Files);
        Assert.Equal((PlannedFileKind.Changed, existing.Fingerprint), (planned.Kind, planned.Previous));
        Assert.Equal(V1("k2"), Rows(planned.NewText));
    }

    [Fact]
    public void Copy_questions_are_collected_beside_target_questions()
    {
        var snapshot = Snapshot([TableFile(A, X, 2, [XRow("k5")]), TableFile(@"db\x_tables\b.tsv", X, 2, [XRow("k6")])], DataEntry("k1"));
        var changes = Changes([Change(RowChangeKind.Insert, X, ["k9", "b1", "n", "1"], null, null, 3), Delete("k1", From(V, "db/x_tables/data__"), 4)]);

        var plan = Plan(changes, snapshot);

        Assert.Single(plan.TargetQuestions);
        Assert.Single(plan.CopyQuestions);
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void Deleting_a_row_of_an_outdated_mod_entry_is_an_error()
    {
        var entry = PackEntry(Md, "db/x_tables/m", X, 1, V1("k1", "k2"));
        var changes = Changes([Delete("k1", From(Md, "db/x_tables/m"), line: 4)]);

        var plan = ProjectWritePlan.Compute(changes, Snapshot([], entry), MiniSchema, GraphWithGame, "Proj", Confirmed);

        var error = Assert.Single(plan.Errors, e => e.Kind == WriteErrorKind.OutdatedTableVersion);
        Assert.Equal("db/x_tables/m", error.RelativeFile);
        Assert.StartsWith("the mod entry is outdated: ", error.Detail);
        Assert.Empty(plan.Files);
    }
}

using TotalWarModTool.Objects;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Tables;
using static TotalWarModTool.Editing.Tests.WriteFixture;

namespace TotalWarModTool.Editing.Tests;

public class ProjectWritePlanTests
{
    private const string A = @"db\x_tables\a.tsv";
    private const string B = @"db\x_tables\b.tsv";

    private static readonly RowProvenance InA = From(Project("proj"), @"pack\" + A);
    private static readonly RowProvenance InB = From(Project("proj"), @"pack\" + B);
    private static readonly RowProvenance FromVanilla = From(Vanilla("v.pack"), "db/x_tables/data__");

    internal static SnapshotTableFile TableFile(string relativeFile, string table, int version, object?[][] rows, bool isCopy = false)
    {
        var packPath = ProjectLayout.FromRelativeFile(relativeFile).PackPath;
        var text = TableTsv.Write(table, version, packPath, Definition(table, version), rows).Text!;
        var parsed = TableTsv.Read(text, MiniSchema).Table!;
        return new SnapshotTableFile(relativeFile, packPath, new FileFingerprint(@"C:\p\pack\" + relativeFile, text.Length, DateTime.UnixEpoch), text, parsed, isCopy);
    }

    internal static ProjectSnapshot Snapshot(params SnapshotTableFile[] files) => new(files, [], [], []);

    internal static WritePlan Plan(ObjectChanges changes, ProjectSnapshot snapshot, string projectName = "Proj", WriteAnswers? answers = null) =>
        ProjectWritePlan.Compute(changes, snapshot, MiniSchema, Graph, projectName, answers ?? WriteAnswers.None);

    private static WritePlan PlanWithGame(ObjectChanges changes, ProjectSnapshot snapshot) =>
        ProjectWritePlan.Compute(changes, snapshot, MiniSchema, GraphWithGame, "Proj", WriteAnswers.None);

    internal static object?[][] Rows(string text) => TableTsv.Read(text, MiniSchema).Table!.Rows.ToArray();

    private static RowChange SetX(string key, string note, string extra, RowProvenance provenance, int line = 3, string[]? previous = null, params string[] changed) =>
        Change(RowChangeKind.Set, X, [key, "b1", note, extra], previous ?? [key, "b1", "n", "1"], provenance, line, changed.Length == 0 ? ["note"] : changed);

    private static RowChange InsertX(string key, int line = 5) =>
        Change(RowChangeKind.Insert, X, [key, "b1", "n", "1"], null, null, line);

    [Fact]
    public void A_set_of_a_project_row_replaces_the_changed_fields_in_its_file()
    {
        var file = TableFile(A, X, 2, [XRow("k1"), XRow("k2", note: "m", extra: 2)]);

        var plan = Plan(Changes([SetX("k1", "x", "1", InA)]), Snapshot(file));

        Assert.Empty(plan.Errors);
        var planned = Assert.Single(plan.Files);
        Assert.Equal(A, planned.RelativeFile);
        Assert.Equal(PlannedFileKind.Changed, planned.Kind);
        Assert.Equal(file.Fingerprint, planned.Previous);
        Assert.Equal(file.Text, planned.PreviousText);
        Assert.Equal([XRow("k1", note: "x"), XRow("k2", note: "m", extra: 2)], Rows(planned.NewText));
        Assert.Equal(1, plan.Summary.RowsSet);
        Assert.Equal(1, plan.Summary.FilesChanged);
    }

    [Fact]
    public void An_override_row_of_a_vanilla_row_is_appended_to_the_only_project_file()
    {
        var file = TableFile(A, X, 2, [XRow("k2")]);

        var plan = Plan(Changes([SetX("k1", "x", "5", FromVanilla)]), Snapshot(file));

        var planned = Assert.Single(plan.Files);
        Assert.Equal([XRow("k2"), XRow("k1", note: "x", extra: 5)], Rows(planned.NewText));
    }

    [Fact]
    public void An_override_row_is_mapped_onto_the_columns_of_an_older_file()
    {
        var file = TableFile(A, X, 1, [["k2", "b1", "n"]]);

        var plan = Plan(Changes([SetX("k1", "x", "0", FromVanilla)]), Snapshot(file));

        var planned = Assert.Single(plan.Files);
        var table = TableTsv.Read(planned.NewText, MiniSchema).Table!;
        Assert.Equal(1, table.Version);
        Assert.Equal([["k2", "b1", "n"], ["k1", "b1", "x"]], table.Rows.ToArray());
    }

    [Fact]
    public void An_override_row_with_a_value_the_file_version_cannot_hold_is_an_error()
    {
        // The vanilla row has extra = 5; the v1 file has no extra column. Appending the row would silently turn 5 into 0 in the game.
        var file = TableFile(A, X, 1, [["k2", "b1", "n"]]);

        var plan = Plan(Changes([SetX("k1", "x", "5", FromVanilla, line: 6), InsertX("k9", line: 8) with { Cells = ["k9", "b1", "n", "3"] }]), Snapshot(file));

        Assert.Equal(2, plan.Errors.Count);
        Assert.All(plan.Errors, e => Assert.Equal((WriteErrorKind.FieldNotInFileVersion, A), (e.Kind, e.RelativeFile)));
        Assert.Equal([6, 8], plan.Errors.Select(e => e.Line));
        Assert.Contains("extra", plan.Errors[0].Detail);
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void A_key_rename_chain_inside_one_file_is_accepted()
    {
        // The diff emits the inserts of a section before its deletes: k1 -> k2 and k2 -> k3 in one edit.
        var file = TableFile(A, X, 2, [XRow("k1"), XRow("k2")]);
        var changes = Changes(
        [
            InsertX("k2"),
            InsertX("k3"),
            Change(RowChangeKind.Delete, X, ["k1", "b1", "n", "1"], ["k1", "b1", "n", "1"], InA, 2),
            Change(RowChangeKind.Delete, X, ["k2", "b1", "n", "1"], ["k2", "b1", "n", "1"], InA, 3),
        ]);

        var plan = Plan(changes, Snapshot(file));

        Assert.Empty(plan.Errors);
        Assert.Equal([XRow("k2"), XRow("k3")], Rows(Assert.Single(plan.Files).NewText));
    }

    [Fact]
    public void A_set_of_a_field_the_file_version_lacks_is_an_error()
    {
        var file = TableFile(A, X, 1, [["k1", "b1", "n"]]);

        var plan = Plan(Changes([SetX("k1", "n", "9", InA, line: 4, changed: "extra")]), Snapshot(file));

        var error = Assert.Single(plan.Errors);
        Assert.Equal((WriteErrorKind.FieldNotInFileVersion, 4, A), (error.Kind, error.Line, error.RelativeFile));
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void An_override_set_of_a_field_the_file_version_lacks_is_an_error()
    {
        var file = TableFile(A, X, 1, [["k2", "b1", "n"]]);

        var plan = Plan(Changes([SetX("k1", "n", "9", FromVanilla, line: 4, changed: "extra")]), Snapshot(file));

        var error = Assert.Single(plan.Errors);
        Assert.Equal((WriteErrorKind.FieldNotInFileVersion, 4, A), (error.Kind, error.Line, error.RelativeFile));
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void An_insert_without_a_project_file_creates_one_named_after_the_project()
    {
        var plan = Plan(Changes([InsertX("k9")]), Snapshot(), "Flo Mod");

        var planned = Assert.Single(plan.Files);
        Assert.Equal(@"db\x_tables\Flo Mod.tsv", planned.RelativeFile);
        Assert.Equal(PlannedFileKind.Created, planned.Kind);
        Assert.Null(planned.Previous);
        Assert.Null(planned.PreviousText);
        var table = TableTsv.Read(planned.NewText, MiniSchema).Table!;
        Assert.Equal(2, table.Version);
        Assert.Equal([XRow("k9")], table.Rows.ToArray());
        Assert.Equal(1, plan.Summary.FilesCreated);
        Assert.Equal(1, plan.Summary.RowsInserted);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("   ")]
    public void An_invalid_project_name_falls_back_to_twmt(string name)
    {
        var plan = Plan(Changes([InsertX("k9")]), Snapshot(), name);

        Assert.Equal(@"db\x_tables\twmt.tsv", Assert.Single(plan.Files).RelativeFile);
    }

    [Fact]
    public void A_copy_does_not_count_as_the_project_file_of_the_table()
    {
        var copy = TableFile(@"db\x_tables\data__.tsv", X, 2, [XRow("k1")], isCopy: true);
        var own = TableFile(A, X, 2, [XRow("k2")]);

        var onlyCopy = Plan(Changes([InsertX("k9")]), Snapshot(copy));
        var both = Plan(Changes([InsertX("k9")]), Snapshot(copy, own));

        Assert.Equal(PlannedFileKind.Created, Assert.Single(onlyCopy.Files).Kind);
        Assert.Empty(both.TargetQuestions);
        Assert.Equal(A, Assert.Single(both.Files).RelativeFile);
    }

    [Fact]
    public void Several_project_files_ask_which_one_and_the_answer_decides()
    {
        var snapshot = Snapshot(TableFile(A, X, 2, [XRow("k1")]), TableFile(B, X, 2, [XRow("k2")]));
        var changes = Changes([InsertX("k9", line: 7)]);

        var asked = Plan(changes, snapshot);
        var answered = Plan(changes, snapshot, answers: new WriteAnswers(new Dictionary<string, string> { [X] = B }, false));
        var wrong = Plan(changes, snapshot, answers: new WriteAnswers(new Dictionary<string, string> { [X] = @"db\x_tables\c.tsv" }, false));

        var question = Assert.Single(asked.TargetQuestions);
        Assert.Equal((X, false), (question.Table, question.IsLoc));
        Assert.Equal([A, B], question.Candidates);
        Assert.Equal([7], question.Lines);
        Assert.Empty(asked.Files);
        Assert.Equal(B, Assert.Single(answered.Files).RelativeFile);
        Assert.Equal(WriteErrorKind.NoAnswer, Assert.Single(wrong.Errors).Kind);
    }

    [Fact]
    public void A_key_that_the_target_file_already_holds_is_an_error()
    {
        var file = TableFile(A, X, 2, [XRow("k9")]);
        var keyless = TableFile(@"db\y_tables\a.tsv", Y, 1, []);
        RowChange InsertY(int line) => Change(RowChangeKind.Insert, Y, ["b1", "a", "b"], null, null, line);

        var duplicate = Plan(Changes([InsertX("k9", line: 6)]), Snapshot(file));
        var twice = Plan(Changes([InsertY(2), InsertY(3)]), Snapshot(keyless));

        var error = Assert.Single(duplicate.Errors);
        Assert.Equal((WriteErrorKind.KeyAlreadyInFile, 6, A), (error.Kind, error.Line, error.RelativeFile));
        Assert.Contains("k9", error.Detail);
        Assert.Empty(duplicate.Files);
        Assert.Empty(twice.Errors);
        Assert.Equal(2, Rows(Assert.Single(twice.Files).NewText).Length);
    }

    [Fact]
    public void A_colour_channel_out_of_range_is_an_error_before_writing()
    {
        RowChange InsertColour(string red, int line) => Change(RowChangeKind.Insert, Colour, ["c1", red, "0", "0"], null, null, line);

        var bad = Plan(Changes([InsertColour("300", 4)]), Snapshot());
        var good = Plan(Changes([InsertColour("255", 4)]), Snapshot());

        var error = Assert.Single(bad.Errors);
        Assert.Equal((WriteErrorKind.ColourChannelOutOfRange, 4), (error.Kind, error.Line));
        Assert.NotNull(error.RelativeFile);
        Assert.Empty(bad.Files);
        Assert.Empty(good.Errors);
        Assert.Single(good.Files);
    }

    [Fact]
    public void Snapshot_problems_become_errors_and_nothing_is_planned()
    {
        var snapshot = new ProjectSnapshot([TableFile(A, X, 2, [XRow("k1")])], [], [],
            [new SnapshotProblem(SnapshotProblemKind.ProjectFileInvalid, @"db\x_tables\z.tsv", null, "line 1: bad")]);

        var plan = Plan(Changes([InsertX("k9")]), snapshot);

        var error = Assert.Single(plan.Errors);
        Assert.Equal(WriteErrorKind.SnapshotProblem, error.Kind);
        Assert.Equal(@"db\x_tables\z.tsv", error.RelativeFile);
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void Changes_with_errors_are_refused()
    {
        var changes = new ObjectChanges([], [], [new BlockDiagnostic(1, DiagnosticSeverity.Error, DiagnosticKind.RootKeyChanged, "x")]);

        Assert.Throws<ArgumentException>(() => Plan(changes, Snapshot()));
    }

    [Fact]
    public void Files_are_sorted_and_an_unchanged_file_is_not_planned()
    {
        var snapshot = Snapshot(TableFile(A, X, 2, [XRow("k1")]), TableFile(B, X, 2, [XRow("k2")]), TableFile(@"db\x_tables\c.tsv", X, 2, [XRow("k3")]));
        var changes = Changes([SetX("k2", "x", "1", InB, previous: ["k2", "b1", "n", "1"]), InsertX("k9")]);

        var plan = Plan(changes, snapshot, answers: new WriteAnswers(new Dictionary<string, string> { [X] = A }, false));

        Assert.Equal([A, B], plan.Files.Select(f => f.RelativeFile).ToArray());
    }

    [Fact]
    public void An_insert_into_a_project_file_in_another_version_is_an_error()
    {
        var file = TableFile(A, X, 1, [["k1", "b1", "n"]]);

        var plan = PlanWithGame(Changes([InsertX("k9", line: 7), InsertX("k8", line: 8)]), Snapshot(file));

        var error = Assert.Single(plan.Errors, e => e.Kind == WriteErrorKind.OutdatedTableVersion);
        Assert.Equal(A, error.RelativeFile);
        Assert.Equal(7, error.Line);
        Assert.Contains("version 1", error.Detail);
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void A_delete_in_a_project_file_in_another_version_is_an_error()
    {
        var file = TableFile(A, X, 1, [["k1", "b1", "n"]]);
        var delete = Change(RowChangeKind.Delete, X, ["k1", "b1", "n", "1"], ["k1", "b1", "n", "1"], InA, 4);

        var plan = PlanWithGame(Changes([delete]), Snapshot(file));

        var error = Assert.Single(plan.Errors, e => e.Kind == WriteErrorKind.OutdatedTableVersion);
        Assert.Equal(A, error.RelativeFile);
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void A_new_file_is_created_in_the_game_version()
    {
        var plan = PlanWithGame(Changes([InsertX("k9")]), Snapshot());

        Assert.Empty(plan.Errors);
        var planned = Assert.Single(plan.Files);
        Assert.Equal(PlannedFileKind.Created, planned.Kind);
        Assert.Equal(2, TableTsv.Read(planned.NewText, MiniSchema).Table!.Version);
    }

    [Fact]
    public void A_file_of_a_table_the_game_lacks_takes_changes()
    {
        var file = TableFile(@"db\y_tables.tsv", Y, 1, [["b1", "a", "b"]]);

        var plan = PlanWithGame(Changes([Change(RowChangeKind.Insert, Y, ["b1", "a", "b"], null, null, 2)]), Snapshot(file));

        Assert.Empty(plan.Errors);
        Assert.Single(plan.Files);
    }
}

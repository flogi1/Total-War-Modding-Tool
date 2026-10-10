using TotalWarModTool.Objects;
using TotalWarModTool.Projects;
using static TotalWarModTool.Editing.Tests.WriteFixture;

namespace TotalWarModTool.Editing.Tests;

/// <summary>Block text → diff → snapshot → plan → write → rebuild → undo, on real files in a temp folder.</summary>
public sealed class ObjectWriteEndToEndTests : IDisposable
{
    private const string ProjectName = "E2E";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly HashSet<string> VanillaEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "db/building_levels_tables/data__", "db/x_tables/data__",
    };

    private readonly TempDir _tmp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly TableStore _store = new(MiniSchema, Graph);
    private readonly LoadOrder _order;
    private readonly string _project;

    public ObjectWriteEndToEndTests()
    {
        var pack = WritePack(_tmp.File("vanilla.pack"), ("data__", Levels, 1, [["b1", 100]]), ("data__", X, 2, [XRow("k1")]));
        _project = _tmp.File("proj");
        WriteProjectTable(_project, X, 2, "a", XRow("k2", note: "m", extra: 2));
        _order = new LoadOrder([Vanilla(pack), Project(_project)], null, []);
    }

    public void Dispose() => _tmp.Dispose();

    private string TrashRoot => _tmp.File("trash");

    private string ProjectFile(string relativeFile) => Path.Combine(_project, "pack", relativeFile);

    private ObjectView Build()
    {
        _store.Clear();
        return new ObjectBuilder(Graph, _store, new LocStore()).Build(new ObjectId(Levels, ["b1"]), _order, null, None);
    }

    private static string KeysOf(ObjectView view) =>
        string.Join(",", view.Sections.Single(s => s.SourceTable == X).Rows.Select(r => r.Fields.Single(f => f.Name == "key").Text));

    private static string Cost(ObjectView view) => view.Root!.Fields.Single(f => f.Name == "create_cost").Text;

    private ObjectChanges Diff(ObjectView view, Func<string, string> edit)
    {
        var text = edit(ObjectText.RenderBlock(view, null).Text);
        var changes = ObjectDiff.Compute(view, ObjectTextParser.Parse(text, view, null, Graph), null, Graph, null);
        Assert.False(changes.HasErrors, string.Join("; ", changes.Diagnostics.Select(d => d.Kind + "@" + d.Line)));
        return changes;
    }

    private WritePlan Plan(ObjectChanges changes, WriteAnswers answers)
    {
        var snapshot = ProjectSnapshotReader.Read(_project, MiniSchema, SnapshotRequest.For(changes), VanillaEntries.Contains, None);
        return ProjectWritePlan.Compute(changes, snapshot, MiniSchema, Graph, ProjectName, answers);
    }

    [Fact]
    public void A_block_edit_is_written_rebuilt_and_undone()
    {
        var view = Build();
        var before = File.ReadAllText(ProjectFile(@"db\x_tables\a.tsv"));
        var changes = Diff(view, text => text
            .Replace("create_cost = 100", "create_cost = 150", StringComparison.Ordinal)
            .Replace("    note = n\n    extra = 1", "    note = x\n    extra = 1", StringComparison.Ordinal)
            .Replace("\n  key = k2\n    note = m\n    extra = 2", string.Empty, StringComparison.Ordinal));

        var asked = Plan(changes, WriteAnswers.None);
        var plan = Plan(changes, WriteAnswers.None);
        var outcome = ProjectWriter.Apply(plan, Guid.NewGuid(), _project, "e2e", TrashRoot, _time, None);

        Assert.False(asked.HasQuestions);
        Assert.False(plan.HasErrors);
        Assert.Null(outcome.Failure);
        var levelsFile = $@"db\{Levels}\{ProjectName}.tsv";
        Assert.Equal([levelsFile, @"db\x_tables\a.tsv"], plan.Files.Select(f => f.RelativeFile).ToArray());
        var written = ProjectSnapshotReader.Read(_project, MiniSchema, new SnapshotRequest(new HashSet<string>([X, Levels]), false, []), static _ => false, None);
        Assert.Equal([XRow("k1", note: "x")], written.Tables.Single(t => t.RelativeFile == @"db\x_tables\a.tsv").Table.Rows.ToArray());
        Assert.Equal([["b1", 150]], written.Tables.Single(t => t.RelativeFile == levelsFile).Table.Rows.ToArray());

        var rebuilt = Build();
        Assert.Equal("150", Cost(rebuilt));
        var k1 = rebuilt.Sections.Single(s => s.SourceTable == X).Rows.Single();
        Assert.Equal(SourceKind.Project, k1.Provenance.Source.Kind);
        Assert.Equal(SourceKind.Vanilla, Assert.Single(k1.Provenance.Overridden).Kind);
        Assert.Equal("k1", KeysOf(rebuilt));

        outcome.Step!.Undo();

        Assert.Equal(before, File.ReadAllText(ProjectFile(@"db\x_tables\a.tsv")));
        Assert.False(File.Exists(ProjectFile(levelsFile)));
        Assert.Single(Directory.GetFiles(TrashRoot, "*", SearchOption.AllDirectories));
        var restored = Build();
        Assert.Equal("100", Cost(restored));
        Assert.Equal("k1,k2", KeysOf(restored));
    }

    [Fact]
    public void Deleting_a_vanilla_row_copies_the_file_and_the_rebuilt_object_lacks_the_row()
    {
        var view = Build();
        var changes = Diff(view, text => text.Replace("  key = k1\n    note = n\n    extra = 1\n", string.Empty, StringComparison.Ordinal));

        var asked = Plan(changes, WriteAnswers.None);
        var plan = Plan(changes, new WriteAnswers(new Dictionary<string, string>(), true));
        var outcome = ProjectWriter.Apply(plan, Guid.NewGuid(), _project, "e2e", TrashRoot, _time, None);

        var question = Assert.Single(asked.CopyQuestions);
        Assert.Equal(@"db\x_tables\data__.tsv", question.RelativeFile);
        Assert.Equal(["k1"], question.DeletedKeys);
        Assert.Null(outcome.Failure);
        var copy = Assert.Single(plan.Files);
        Assert.Equal(PlannedFileKind.Copy, copy.Kind);
        Assert.Equal("k2", KeysOf(Build()));

        outcome.Step!.Undo();

        Assert.Equal("k1,k2", KeysOf(Build()));
    }
}

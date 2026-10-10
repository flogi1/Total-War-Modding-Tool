using System.Text;
using TotalWarModTool.Projects;

namespace TotalWarModTool.Editing.Tests;

public sealed class ProjectWriterTests : IDisposable
{
    private const string A = @"db\x_tables\a.tsv";
    private const string B = @"db\x_tables\b.tsv";
    private const string N = @"db\y_tables\n.tsv";
    private const string C = @"db\z_tables\c.tsv";

    private readonly TempDir _tmp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

    public ProjectWriterTests() => Directory.CreateDirectory(Project);

    private string Project => _tmp.File("proj");

    private string TrashRoot => _tmp.File("trash");

    public void Dispose() => _tmp.Dispose();

    private string PathOf(string relativeFile) => Path.Combine(Project, "pack", relativeFile);

    private string Text(string relativeFile) => File.ReadAllText(PathOf(relativeFile));

    /// <summary>An existing file with the text; the planned change replaces it.</summary>
    private PlannedFile Changed(string relativeFile, string old, string next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(relativeFile))!);
        File.WriteAllText(PathOf(relativeFile), old);
        return new PlannedFile(relativeFile, PlannedFileKind.Changed, ProjectFileWriter.Fingerprint(Project, relativeFile), old, next);
    }

    private static PlannedFile Created(string relativeFile, string next) => new(relativeFile, PlannedFileKind.Created, null, null, next);

    private static PlannedFile Copy(string relativeFile, string next) => new(relativeFile, PlannedFileKind.Copy, null, null, next, "db/z_tables/data__");

    private static WritePlan PlanOf(params PlannedFile[] files) =>
        new(files, [], [], [], new WriteSummary(0, 0, 0, 0, 0, 0, 0, 0));

    private WriteOutcome Apply(WritePlan plan, CancellationToken cancellationToken = default) =>
        ProjectWriter.Apply(plan, Guid.NewGuid(), Project, "test", TrashRoot, _time, cancellationToken);

    [Fact]
    public void Every_file_of_the_plan_is_written_and_the_step_holds_the_previous_texts()
    {
        var outcome = Apply(PlanOf(Changed(A, "old", "new"), Created(N, "created"), Copy(C, "copied")));

        Assert.Null(outcome.Failure);
        Assert.Equal("new", Text(A));
        Assert.Equal("created", Text(N));
        Assert.Equal("copied", Text(C));
        var step = outcome.Step!;
        Assert.Equal([A, N, C], step.Files);

        step.Undo();

        Assert.Equal("old", Text(A));
        Assert.False(File.Exists(PathOf(N)));
        Assert.False(File.Exists(PathOf(C)));
        Assert.Equal(2, step.InTrash.Count);
    }

    [Fact]
    public void A_changed_fingerprint_aborts_before_anything_is_written()
    {
        var changed = Changed(A, "old", "new");
        File.WriteAllText(PathOf(A), "an outside edit that is longer");

        var outcome = Apply(PlanOf(changed, Created(B, "created")));

        Assert.Null(outcome.Step);
        Assert.Equal((WriteFailureKind.FileChanged, A), (outcome.Failure!.Kind, outcome.Failure.File));
        Assert.False(File.Exists(PathOf(B)));
        Assert.Equal("an outside edit that is longer", Text(A));
    }

    [Fact]
    public void A_file_that_appeared_at_a_created_target_aborts()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(N))!);
        File.WriteAllText(PathOf(N), "someone was faster");

        var outcome = Apply(PlanOf(Created(N, "created")));

        Assert.Equal(WriteFailureKind.FileChanged, outcome.Failure!.Kind);
        Assert.Equal("someone was faster", Text(N));
    }

    [Fact]
    public void A_locked_second_file_rolls_the_first_back()
    {
        var first = Changed(A, "old a", "new a");
        var second = Changed(B, "old b", "new b");

        WriteOutcome outcome;
        using (new FileStream(PathOf(B), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = Apply(PlanOf(first, second));
        }

        Assert.Null(outcome.Step);
        Assert.Equal(WriteFailureKind.WriteFailed, outcome.Failure!.Kind);
        Assert.Equal([(A, WrittenFileState.RolledBack), (B, WrittenFileState.Unchanged)], outcome.Failure.Files);
        Assert.Equal("old a", Text(A));
    }

    [Fact]
    public void A_created_file_is_trashed_on_rollback()
    {
        var locked = Changed(B, "old b", "new b");

        WriteOutcome outcome;
        using (new FileStream(PathOf(B), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = Apply(PlanOf(Created(N, "created"), locked));
        }

        Assert.Equal([(N, WrittenFileState.RolledBack), (B, WrittenFileState.Unchanged)], outcome.Failure!.Files);
        Assert.False(File.Exists(PathOf(N)));
        Assert.Single(Directory.GetFiles(TrashRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_plan_with_questions_or_errors_is_refused()
    {
        var file = Created(N, "created");
        var empty = new WriteSummary(0, 0, 0, 0, 0, 0, 0, 0);

        Assert.Throws<ArgumentException>(() => Apply(new WritePlan([file], [new TargetFileQuestion("x_tables", false, [A, B], [1])], [], [], empty)));
        Assert.Throws<ArgumentException>(() => Apply(new WritePlan([file], [], [], [new WriteError(WriteErrorKind.NoAnswer, 1, null, "x")], empty)));
        Assert.Throws<ArgumentException>(() => Apply(PlanOf()));
    }

    [Fact]
    public void A_changed_file_without_its_previous_text_or_fingerprint_is_refused()
    {
        // The rollback writes PreviousText back and the check compares Previous: a plan without them could not be taken back.
        var changed = Changed(A, "old", "new");

        Assert.Throws<ArgumentException>(() => Apply(PlanOf(changed with { PreviousText = null })));
        Assert.Throws<ArgumentException>(() => Apply(PlanOf(changed with { Previous = null })));
        Assert.Equal("old", File.ReadAllText(PathOf(A)));
    }

    [Fact]
    public void Cancellation_before_the_first_write_writes_nothing()
    {
        var changed = Changed(A, "old", "new");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => Apply(PlanOf(changed, Created(N, "created")), cancelled.Token));

        Assert.Equal("old", Text(A));
        Assert.False(File.Exists(PathOf(N)));
    }
}

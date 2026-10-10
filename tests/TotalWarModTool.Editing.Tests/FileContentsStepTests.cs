using System.Text;
using TotalWarModTool.Projects;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.Editing.Tests;

public sealed class FileContentsStepTests : IDisposable
{
    private const string A = @"db\x_tables\a.tsv";
    private const string B = @"db\x_tables\b.tsv";

    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly TempDir _tmp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _projectId = Guid.NewGuid();

    public FileContentsStepTests() => Directory.CreateDirectory(Project);

    private string Project => _tmp.File("proj");

    private string TrashRoot => _tmp.File("trash");

    public void Dispose() => _tmp.Dispose();

    private string PathOf(string relativeFile) => Path.Combine(Project, "pack", relativeFile);

    private string Text(string relativeFile) => File.ReadAllText(PathOf(relativeFile));

    /// <summary>A file that existed with the old text; the write left the next text.</summary>
    private FileContentsEntry Existing(string relativeFile, string old, string next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(relativeFile))!);
        File.WriteAllText(PathOf(relativeFile), old);
        var result = ProjectFileWriter.WriteText(Project, relativeFile, next);
        Assert.Equal(FileWriteProblem.None, result.Problem);
        return new FileContentsEntry(relativeFile, old, next, result.Fingerprint!, []);
    }

    /// <summary>A file the write created.</summary>
    private FileContentsEntry Created(string relativeFile, string next)
    {
        var result = ProjectFileOps.Create(Project, relativeFile, Utf8.GetBytes(next), CancellationToken.None);
        Assert.Equal(FileOpProblem.None, result.Problem);
        return new FileContentsEntry(relativeFile, null, next, result.Fingerprint!, result.CreatedFolders);
    }

    private FileContentsStep Step(params FileContentsEntry[] entries) => new(_projectId, Project, "test", entries, TrashRoot, _time);

    [Fact]
    public void Undo_writes_the_previous_text_back_and_redo_the_next()
    {
        var step = Step(Existing(A, "old", "new"));

        step.Undo();
        var afterUndo = Text(A);
        step.Do();

        Assert.Equal("old", afterUndo);
        Assert.Equal("new", Text(A));
        Assert.Empty(step.InTrash);
        Assert.Equal([A], step.Files);
    }

    [Fact]
    public void Undo_moves_a_created_file_to_the_trash_and_redo_brings_it_back()
    {
        var step = Step(Created(A, "created"));

        step.Undo();

        Assert.False(File.Exists(PathOf(A)));
        Assert.Single(step.InTrash);
        Assert.False(Directory.Exists(Path.Combine(Project, "pack", "db")));

        step.Do();

        Assert.Equal("created", Text(A));
        Assert.Empty(step.InTrash);
    }

    [Fact]
    public void Undo_of_two_created_files_removes_the_shared_parent_folder()
    {
        // The first create made pack\db and pack\db\y_tables, the second only pack\db\z_tables: db is in the first file's list only.
        var step = Step(Created(@"db\y_tables\n.tsv", "n"), Created(@"db\z_tables\c.tsv", "c"));

        step.Undo();

        Assert.False(Directory.Exists(Path.Combine(Project, "pack", "db")));
        Assert.Equal(2, step.InTrash.Count);

        step.Do();

        Assert.Equal("n", Text(@"db\y_tables\n.tsv"));
        Assert.Equal("c", Text(@"db\z_tables\c.tsv"));
    }

    [Fact]
    public void Undo_leaves_the_step_when_a_file_changed_since()
    {
        var step = Step(Existing(A, "old", "new"));
        File.WriteAllText(PathOf(A), "someone else wrote something longer");
        var history = new ActionHistory();
        history.Record(step);

        var thrown = Assert.Throws<FileContentsChangedException>(step.Undo);

        Assert.Equal(A, thrown.RelativeFile);
        Assert.Equal("someone else wrote something longer", Text(A));
        Assert.ThrowsAny<StepDeferredException>(() => history.Undo());
        Assert.Same(step, history.NextUndo);
    }

    [Fact]
    public void Undo_leaves_the_step_when_a_created_file_is_gone()
    {
        var step = Step(Created(A, "created"));
        File.Delete(PathOf(A));

        var thrown = Assert.Throws<FileContentsChangedException>(step.Undo);

        Assert.Equal(A, thrown.RelativeFile);
        Assert.Empty(step.InTrash);
        Assert.False(Directory.Exists(TrashRoot));
    }

    [Fact]
    public void Nothing_is_written_when_the_second_file_changed()
    {
        var step = Step(Existing(A, "old a", "new a"), Existing(B, "old b", "new b"));
        File.WriteAllText(PathOf(B), "changed by someone else, longer");

        Assert.Throws<FileContentsChangedException>(step.Undo);

        Assert.Equal("new a", Text(A));
    }

    [Fact]
    public void A_locked_first_file_defers_the_step()
    {
        var step = Step(Existing(A, "old", "new"));

        using (new FileStream(PathOf(A), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var thrown = Assert.ThrowsAny<StepDeferredException>(step.Undo);
            Assert.IsNotType<FileContentsChangedException>(thrown);
        }

        step.Undo();
        Assert.Equal("old", Text(A));
    }

    [Fact]
    public void A_failure_after_the_first_file_drops_the_step_and_names_every_file()
    {
        var step = Step(Existing(A, "old a", "new a"), Existing(B, "old b", "new b"));

        InvalidOperationException thrown;
        using (new FileStream(PathOf(B), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            thrown = Assert.Throws<InvalidOperationException>(step.Undo);
        }

        Assert.Contains("a.tsv: written", thrown.Message);
        Assert.Contains("b.tsv: failed", thrown.Message);
        Assert.Equal("old a", Text(A));
    }

    [Fact]
    public void Redo_after_undo_checks_the_file_the_undo_left()
    {
        var step = Step(Existing(A, "old", "new"));
        step.Undo();
        File.WriteAllText(PathOf(A), "an outside edit that is longer");

        Assert.Throws<FileContentsChangedException>(step.Do);

        Assert.Equal("an outside edit that is longer", Text(A));
    }
}

using TotalWarModTool.Editing;
using TotalWarModTool.Objects;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ObjectApplyControllerTests
{
    private const string XEntry = "db/x_tables/data__";
    private const string ProjectFileA = @"db\x_tables\a.tsv";

    // The text of x_tables:k1 in the fixture, line by line (1-based line numbers of the diagnostics):
    //   1 x k1 / 2   key = k1 / 3   label = First / 4 (blank) / 5 y / 6   value = 1 / 7 (blank) / 8   value = 2
    private const string LabelLine = "  label = First";
    private const string ValueOne = "  value = 1";
    private const string SecondRow = "\n\n  value = 2";

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, MainSetup Main, ObjectsController Objects, ObjectApplyController Apply,
        FakeObjectDialogs Dialogs, ProjectState Project, string VanillaPath) : IDisposable
    {
        public TabsViewModel Tabs => Main.Main.Tabs;

        public ActionHistory History => Main.History!;

        public FakeOperationRunner Runner => Game.Runner;

        public AppTexts Texts => Game.Texts;

        public string ProjectFile(string relative) => Path.Combine(Project.Folder, "pack", relative);

        public void Dispose() => Game.Dispose();

        /// <summary>How many builds of the object with this key went through the runner.</summary>
        public int Builds(string key) => Runner.Titles.Count(title => title == Texts.ObjectsBuilding(key));

        /// <summary>The rows of a project table file.</summary>
        public IReadOnlyList<object?[]> RowsOf(string relative) =>
            TableTsv.Read(File.ReadAllText(ProjectFile(relative)), Workspace.Schema!.Schema!).Table!.Rows;
    }

    // The game of ObjectsControllerTests plus a project (the active one) and the apply controller wired like the composition root wires it.
    // projectFiles are written below the pack folder of the project before anything is built.
    private static async Task<Setup> CreateAsync(params (string RelativeFile, string Table, object?[][] Rows)[] projectFiles)
    {
        var game = new TestGame(withSchema: false);
        ObjectsTestSchema.WriteSchema(game);
        var vanilla = ObjectsTestSchema.WriteVanillaPack(game, "data.pack", ObjectsTestSchema.XRows, ObjectsTestSchema.YRows);
        File.WriteAllText(Path.Combine(game.InstallPath, LoadOrderResolver.UsedModsFileName), string.Empty);
        ObjectsTestSchema.WriteLocPack(game);
        var workspace = game.NewController(profile: ObjectsTestSchema.Profile);
        await workspace.LoadAsync();
        var main = game.NewMain(workspace, withDocuments: true);
        var project = game.NewProject(main.Projects, "Flo");
        foreach (var (relative, table, rows) in projectFiles)
        {
            var definition = table == "x_tables" ? ObjectsTestSchema.X : ObjectsTestSchema.Y;
            var path = Path.Combine(project.Folder, "pack", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, TableTsv.Write(table, 0, relative.Replace('\\', '/')[..^".tsv".Length], definition, rows).Text);
        }

        var objects = new ObjectsController(workspace, main.Projects, main.Documents, main.Main.Tabs, game.Runner, game.Paths, game.Log, game.Texts,
            key => key, main.Main.OpenEntryAsync, main.Main.OpenProjectFileAsync);
        main.Main.Tabs.ObjectOpener = objects;
        var dialogs = new FakeObjectDialogs();
        var apply = new ObjectApplyController(objects, main.Projects, main.Documents!, main.History!, game.Runner, dialogs, game.Root.File("trash"),
            game.Log, game.Texts, TimeProvider.System);
        objects.ApplyHandler = apply.ApplyAsync;
        objects.LeaveHandler = apply.LeaveAsync;
        return new Setup(game, workspace, main, objects, apply, dialogs, project, vanilla);
    }

    private static (string RelativeFile, string Table, object?[][] Rows) AFile(params object?[][] rows) => (ProjectFileA, "x_tables", rows);

    // Opens x_tables of the vanilla pack and shows k1 as an object; the tab is built and selected.
    private static async Task<ObjectTabViewModel> ShowK1Async(Setup setup)
    {
        var pack = setup.Workspace.Catalog!.Packs.Single(p => string.Equals(p.Path, setup.VanillaPath, StringComparison.OrdinalIgnoreCase));
        await setup.Main.Main.OpenEntryAsync(pack, XEntry);
        var table = Assert.IsType<TableTabViewModel>(setup.Tabs.SelectedTab);
        var index = Enumerable.Range(0, 100).First(i => table.KeyOfRow(i) is { } rowKey && rowKey.KeyValues.SequenceEqual(new[] { "k1" }));
        setup.Objects.Show(table, table.KeyOfRow(index)!);
        var tab = Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        return tab;
    }

    // Replaces text in the editor text and proves that it matched.
    private static void Edit(ObjectTabViewModel tab, string from, string to)
    {
        Assert.Contains(from, tab.Text);
        var before = tab.Text;
        tab.Text = before.Replace(from, to);
        Assert.NotEqual(before, tab.Text);
    }

    // Runs the apply the way the button does (through the handler the controller put on the tab) and proves that it could run.
    private static async Task ApplyAsync(ObjectTabViewModel tab)
    {
        Assert.True(tab.HasUnappliedText);
        Assert.Null(tab.ApplyDisabledReason);
        await tab.ApplyHandler!(tab);
    }

    private static void AssertNothingWritten(Setup setup, ObjectTabViewModel tab)
    {
        Assert.False(setup.History.CanUndo);
        Assert.True(tab.HasUnappliedText);
        Assert.Null(tab.ResultLine);
        Assert.Contains(setup.Texts.ObjectsApplyChecking, setup.Runner.Titles);
        Assert.DoesNotContain(setup.Texts.ObjectsApplyWriting, setup.Runner.Titles);
    }

    [Fact] public async Task Applying_a_changed_field_writes_an_override_row_rebuilds_the_tab_and_records_one_step()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        var changedFor = new List<Guid>();
        setup.Apply.FilesChanged += (_, id) => changedFor.Add(id);
        Edit(tab, LabelLine, "  label = Changed");

        await ApplyAsync(tab);

        Assert.Contains(setup.Texts.ObjectsApplyChecking, setup.Runner.Titles);
        Assert.Contains(setup.Texts.ObjectsApplyWriting, setup.Runner.Titles);
        Assert.Equal(setup.Texts.ObjectsBuilding("k1"), setup.Runner.Titles[^1]);
        var rows = setup.RowsOf(ProjectFileA);
        Assert.Equal(["k2", "k1"], rows.Select(row => (string)row[0]!));
        Assert.Equal("Changed", rows[1][1]);
        var step = Assert.IsType<FileContentsStep>(setup.History.NextUndo);
        Assert.Equal([ProjectFileA], step.Files);
        Assert.Equal(SourceKind.Project, tab.Result!.View.Root!.Provenance.Source.Kind);
        Assert.False(tab.HasUnappliedText);
        Assert.StartsWith("1 rows in 1 files changed", tab.ResultLine);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.TargetAsks);
        Assert.Equal([setup.Project.Id], changedFor);
        Assert.Contains("label = Changed", tab.Text);
    }

    [Fact] public async Task Applying_without_a_project_file_creates_one_named_after_the_project_and_the_result_line_names_it()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);
        var changedFor = new List<Guid>();
        setup.Apply.FilesChanged += (_, id) => changedFor.Add(id);
        Edit(tab, LabelLine, "  label = Changed");

        await ApplyAsync(tab);

        var created = Assert.Single(Directory.GetFiles(Path.Combine(setup.Project.Folder, "pack", "db", "x_tables")));
        Assert.Equal(".tsv", Path.GetExtension(created));
        Assert.Contains(@"db\x_tables\" + Path.GetFileName(created), tab.ResultLine);
        Assert.Contains("1 files created", tab.ResultLine);
        Assert.Equal([setup.Project.Id], changedFor);
        Assert.False(tab.HasUnappliedText);
    }

    [Fact] public async Task Parse_errors_show_a_message_and_write_nothing()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        var before = File.ReadAllText(setup.ProjectFile(ProjectFileA));
        Edit(tab, LabelLine, LabelLine + "\n  garbage");

        await ApplyAsync(tab);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ObjectsApplyErrorsTitle, title);
        Assert.Contains(setup.Texts.ObjectsApplyErrorLine(4, setup.Texts.DiagnosticText(DiagnosticKind.UnknownLineForm)), message);
        Assert.Equal(before, File.ReadAllText(setup.ProjectFile(ProjectFileA)));
        AssertNothingWritten(setup, tab);
    }

    [Fact] public async Task A_type_error_of_the_diff_shows_the_line_and_the_kind()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        Edit(tab, ValueOne, "  value = abc");

        await ApplyAsync(tab);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ObjectsApplyErrorsTitle, title);
        Assert.Contains(setup.Texts.ObjectsApplyErrorLine(6, setup.Texts.DiagnosticText(DiagnosticKind.InvalidValue)), message);
        AssertNothingWritten(setup, tab);
    }

    [Fact] public async Task A_text_that_differs_only_in_trailing_whitespace_says_nothing_to_write()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        var before = File.ReadAllText(setup.ProjectFile(ProjectFileA));
        Edit(tab, LabelLine, LabelLine + "   ");

        await ApplyAsync(tab);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal((setup.Texts.ObjectsApply, setup.Texts.ObjectsApplyResultNothing), (title, message));
        Assert.Equal(before, File.ReadAllText(setup.ProjectFile(ProjectFileA)));
        AssertNothingWritten(setup, tab);
    }

    [Fact] public async Task Several_project_files_ask_which_one_and_cancelling_the_target_question_writes_nothing()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]), (@"db\x_tables\b.tsv", "x_tables", [["k3", "Other"]]));
        var tab = await ShowK1Async(setup);
        var beforeA = File.ReadAllText(setup.ProjectFile(ProjectFileA));
        var beforeB = File.ReadAllText(setup.ProjectFile(@"db\x_tables\b.tsv"));
        Edit(tab, LabelLine, "  label = Changed");

        await ApplyAsync(tab);

        var ask = Assert.Single(setup.Dialogs.TargetAsks);
        Assert.Equal(("x_tables", false), (ask.TableOrLoc, ask.IsLoc));
        Assert.Equal([ProjectFileA, @"db\x_tables\b.tsv"], ask.Candidates);
        Assert.Equal(beforeA, File.ReadAllText(setup.ProjectFile(ProjectFileA)));
        Assert.Equal(beforeB, File.ReadAllText(setup.ProjectFile(@"db\x_tables\b.tsv")));
        AssertNothingWritten(setup, tab);

        setup.Dialogs.TargetAnswer = candidates => candidates.Single(candidate => candidate.EndsWith("b.tsv", StringComparison.Ordinal));
        await ApplyAsync(tab);

        Assert.Equal(2, setup.Dialogs.TargetAsks.Count);
        Assert.Equal(["k3", "k1"], setup.RowsOf(@"db\x_tables\b.tsv").Select(row => (string)row[0]!));
        Assert.Equal(beforeA, File.ReadAllText(setup.ProjectFile(ProjectFileA)));
        Assert.False(tab.HasUnappliedText);
    }

    [Fact] public async Task Deleting_a_vanilla_row_asks_for_the_copy_and_cancelling_the_copy_question_writes_nothing()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        Edit(tab, SecondRow, string.Empty);

        await ApplyAsync(tab);

        var copy = Assert.Single(setup.Dialogs.CopyAsks);
        var line = Assert.Single(copy);
        Assert.Contains("data__", line);
        Assert.Contains("2 rows", line);
        Assert.False(File.Exists(setup.ProjectFile(@"db\y_tables\data__.tsv")));
        AssertNothingWritten(setup, tab);

        setup.Dialogs.CopiesAnswer = true;
        await ApplyAsync(tab);

        Assert.Equal(2, setup.Dialogs.CopyAsks.Count);
        Assert.Single(setup.RowsOf(@"db\y_tables\data__.tsv"));
        Assert.Single(Assert.Single(tab.Result!.View.Sections).Rows);
        Assert.False(tab.HasUnappliedText);

        setup.History.Undo();
        await setup.Objects.BuildAsync(tab);

        Assert.Equal(2, Assert.Single(tab.Result!.View.Sections).Rows.Count);
        Assert.False(File.Exists(setup.ProjectFile(@"db\y_tables\data__.tsv")));
    }

    [Fact] public async Task An_unsaved_grid_tab_of_a_target_file_blocks_the_apply()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        await setup.Main.Main.OpenProjectFileAsync(setup.Project.Id, ProjectFileA);
        var grid = Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.SelectedTab);
        grid.Rows[0][1] = "Edited in the grid";
        Assert.True(grid.IsModified);
        var tab = await ShowK1Async(setup);
        var before = File.ReadAllText(setup.ProjectFile(ProjectFileA));
        Edit(tab, LabelLine, "  label = Changed");

        await ApplyAsync(tab);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ObjectsApplyErrorsTitle, title);
        Assert.Equal(setup.Texts.ObjectsApplyGridUnsaved("db/x_tables/a.tsv"), message);
        Assert.Same(grid, setup.Tabs.SelectedTab);
        Assert.Equal(before, File.ReadAllText(setup.ProjectFile(ProjectFileA)));

        // The edit of the grid is the only step in the history; the apply recorded none.
        Assert.IsNotType<FileContentsStep>(setup.History.NextUndo);
        Assert.True(tab.HasUnappliedText);
        Assert.Null(tab.ResultLine);
        Assert.Contains(setup.Texts.ObjectsApplyChecking, setup.Runner.Titles);
        Assert.DoesNotContain(setup.Texts.ObjectsApplyWriting, setup.Runner.Titles);
    }

    [Fact] public async Task A_file_changed_between_snapshot_and_write_is_reported_and_nothing_is_written()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        const string Foreign = "someone else wrote a longer text than the file had before";
        setup.Runner.BeforeRun = title =>
        {
            if (title == setup.Texts.ObjectsApplyWriting)
            {
                File.WriteAllText(setup.ProjectFile(ProjectFileA), Foreign);
            }
        };
        Edit(tab, LabelLine, "  label = Changed");

        await ApplyAsync(tab);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ObjectsApplyFailedTitle, title);
        Assert.Equal(setup.Texts.ObjectsApplyFileChanged(ProjectFileA), message);
        Assert.Equal(Foreign, File.ReadAllText(setup.ProjectFile(ProjectFileA)));
        Assert.Contains(setup.Texts.ObjectsApplyWriting, setup.Runner.Titles);
        Assert.False(setup.History.CanUndo);
        Assert.True(tab.HasUnappliedText);
        Assert.Null(tab.ResultLine);
    }

    [Fact] public async Task A_file_that_cannot_be_written_restores_the_written_ones_and_names_the_problem()
    {
        // Two changed files: the label goes into a.tsv, the new y row into p.tsv. p.tsv is held open for reading, so it can be read but not written.
        using var setup = await CreateAsync(AFile(["k2", "Project label"]), (@"db\y_tables\p.tsv", "y_tables", [["k9", 5]]));
        var tab = await ShowK1Async(setup);
        var beforeA = File.ReadAllText(setup.ProjectFile(ProjectFileA));
        var beforeP = File.ReadAllText(setup.ProjectFile(@"db\y_tables\p.tsv"));
        FileStream? hold = null;
        setup.Runner.BeforeRun = title =>
        {
            if (title == setup.Texts.ObjectsApplyWriting)
            {
                hold = new FileStream(setup.ProjectFile(@"db\y_tables\p.tsv"), FileMode.Open, FileAccess.Read, FileShare.Read);
            }
        };
        Edit(tab, LabelLine, "  label = Changed");
        Edit(tab, "  value = 2", "  value = 2\n\n  value = 5");

        try
        {
            await ApplyAsync(tab);
        }
        finally
        {
            hold?.Dispose();
        }

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ObjectsApplyFailedTitle, title);
        Assert.StartsWith(setup.Texts.ObjectsApplyWriteFailed(string.Empty).TrimEnd(), message, StringComparison.Ordinal);
        Assert.Equal(beforeA, File.ReadAllText(setup.ProjectFile(ProjectFileA)));
        Assert.Equal(beforeP, File.ReadAllText(setup.ProjectFile(@"db\y_tables\p.tsv")));
        Assert.False(setup.History.CanUndo);
        Assert.True(tab.HasUnappliedText);
        Assert.False(tab.IsStale);
    }

    [Fact] public async Task A_rollback_failure_names_every_file_and_its_state()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var failure = new WriteFailure(WriteFailureKind.RollbackFailed, null, "could not restore",
            [(ProjectFileA, WrittenFileState.RollbackFailed), (@"db\y_tables\p.tsv", WrittenFileState.RolledBack), (@"db\x_tables\z.tsv", WrittenFileState.Unchanged)]);

        var message = setup.Apply.FailureMessage(failure);

        Assert.StartsWith(setup.Texts.ObjectsApplyRollbackFailed("could not restore"), message);
        Assert.Contains(setup.Texts.ObjectsApplyFileState(ProjectFileA, setup.Texts.ObjectsFileStateRollbackFailed), message);
        Assert.Contains(setup.Texts.ObjectsApplyFileState(@"db\y_tables\p.tsv", setup.Texts.ObjectsFileStateRolledBack), message);
        Assert.Contains(setup.Texts.ObjectsApplyFileState(@"db\x_tables\z.tsv", setup.Texts.ObjectsFileStateUnchanged), message);
        Assert.Equal(setup.Texts.ObjectsApplyFileChanged("a"), setup.Apply.FailureMessage(new WriteFailure(WriteFailureKind.FileChanged, "a", "x", [])));
        Assert.Equal(setup.Texts.ObjectsApplyFileUnknown("a", "why"), setup.Apply.FailureMessage(new WriteFailure(WriteFailureKind.FileUnknown, "a", "why", [])));
    }

    [Fact] public async Task A_cancelled_write_leaves_the_text_unapplied()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        var before = File.ReadAllText(setup.ProjectFile(ProjectFileA));
        setup.Runner.BeforeRun = title =>
        {
            if (title == setup.Texts.ObjectsApplyWriting)
            {
                setup.Runner.CancelNext = true;
            }
        };
        Edit(tab, LabelLine, "  label = Changed");

        await ApplyAsync(tab);

        Assert.Contains(setup.Texts.ObjectsApplyWriting, setup.Runner.Titles);
        Assert.Equal(before, File.ReadAllText(setup.ProjectFile(ProjectFileA)));
        Assert.False(setup.History.CanUndo);
        Assert.True(tab.HasUnappliedText);
        Assert.Null(tab.ResultLine);
        Assert.Empty(setup.Dialogs.Messages);
    }

    [Fact] public async Task An_apply_whose_tab_moved_on_meanwhile_writes_nothing()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        var before = File.ReadAllText(setup.ProjectFile(ProjectFileA));
        Edit(tab, LabelLine, "  label = Changed");
        setup.Dialogs.UnappliedAnswer = SaveChoice.Discard;
        var moved = false;
        setup.Runner.OnReport = (_, _) =>
        {
            if (!moved)
            {
                moved = true;
                tab.NavigateTo(new ObjectId("x_tables", ["k2"]), newTab: false);          // while the check of k1 runs
            }
        };

        await ApplyAsync(tab);

        Assert.True(moved);
        Assert.Equal("k2", tab.Current.JoinedKey);
        Assert.DoesNotContain(setup.Texts.ObjectsApplyWriting, setup.Runner.Titles);
        Assert.Equal(before, File.ReadAllText(setup.ProjectFile(ProjectFileA)));
        Assert.False(setup.History.CanUndo);
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains("moved on", StringComparison.Ordinal));
    }

    [Fact] public async Task Apply_is_disabled_without_an_active_project_and_the_reason_says_so()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        Edit(tab, LabelLine, "  label = Changed");
        Assert.Null(tab.ApplyDisabledReason);

        Assert.True(setup.Main.Projects.Remove(setup.Project.Id).Ok);

        Assert.Equal(setup.Texts.ObjectsApplyNoProject, tab.ApplyDisabledReason);
        var titles = setup.Runner.Titles.Count;
        await setup.Apply.ApplyAsync(tab);
        Assert.Equal(titles, setup.Runner.Titles.Count);
        Assert.True(tab.HasUnappliedText);
        Assert.False(setup.History.CanUndo);
    }

    [Fact] public async Task Leave_asks_and_applies_discards_or_cancels()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        Assert.True(await setup.Apply.LeaveAsync(tab));                         // nothing unapplied: no question
        Assert.Empty(setup.Dialogs.UnappliedAsks);

        Edit(tab, LabelLine, "  label = Changed");
        var changed = tab.Text;

        setup.Dialogs.UnappliedAnswer = SaveChoice.Cancel;
        Assert.False(await setup.Apply.LeaveAsync(tab));
        Assert.Equal(changed, tab.Text);
        Assert.Equal(["k1"], setup.Dialogs.UnappliedAsks);

        setup.Dialogs.UnappliedAnswer = SaveChoice.Discard;
        Assert.True(await setup.Apply.LeaveAsync(tab));
        Assert.Equal(tab.BaselineText, tab.Text);
        Assert.False(setup.History.CanUndo);

        Edit(tab, LabelLine, "  label = Changed");
        setup.Dialogs.UnappliedAnswer = SaveChoice.Save;
        Assert.True(await setup.Apply.LeaveAsync(tab));
        Assert.False(tab.HasUnappliedText);
        Assert.Equal("Changed", setup.RowsOf(ProjectFileA)[1][1]);
        Assert.True(setup.History.CanUndo);
    }

    [Fact] public async Task Leave_with_save_tells_the_reason_when_the_text_cannot_be_applied()
    {
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        Edit(tab, LabelLine, "  label = Changed");
        tab.MarkStale();
        setup.Dialogs.UnappliedAnswer = SaveChoice.Save;

        Assert.False(await setup.Apply.LeaveAsync(tab));

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal((setup.Texts.ObjectsApplyErrorsTitle, setup.Texts.ObjectsApplyStale), (title, message));
        Assert.True(tab.HasUnappliedText);
        Assert.False(setup.History.CanUndo);
    }

    [Fact] public async Task Warnings_of_the_diff_are_counted_in_the_result_line()
    {
        // A missing root field is a warning ("the field keeps its value"); the new y row is the real change.
        using var setup = await CreateAsync(AFile(["k2", "Project label"]));
        var tab = await ShowK1Async(setup);
        Edit(tab, LabelLine + "\n", string.Empty);
        Edit(tab, "  value = 2", "  value = 2\n\n  value = 5");

        await ApplyAsync(tab);

        Assert.Empty(setup.Dialogs.Messages);
        Assert.EndsWith(setup.Texts.ObjectsApplyResultWarnings(1), tab.ResultLine);
        Assert.True(setup.History.CanUndo);
    }
}

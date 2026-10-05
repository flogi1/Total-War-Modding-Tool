using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ProjectFilesControllerTests
{
    private const int Value = 0;
    private const string TableA = @"db\x_tables\a.tsv";
    private const string TableB = @"db\x_tables\b.tsv";

    private static readonly TableDefinition Def = new(0, [new FieldDefinition("value", FieldType.I32, nameof(FieldType.I32), false, null)]);

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, ProjectsController Projects, TabsViewModel Tabs, ActionHistory History,
        FakeEditDialogs Dialogs, DocumentsController Documents, ProjectFilesController Files, ProjectState Project, string Trash) : IDisposable
    {
        public Guid Id => Project.Id;

        public TabSource Source => TabSource.ForProject(Project.Id, Project.DisplayName);

        public string PathOf(string relativeFile) => Path.Combine(Project.Folder, "pack", relativeFile);

        public bool Exists(string relativeFile) => File.Exists(PathOf(relativeFile));

        public void Dispose() => Game.Dispose();

        // Every file and folder of the project folder, relative, sorted: what a failed or cancelled action must leave as it was.
        public string[] Snapshot() =>
            Directory.EnumerateFileSystemEntries(Project.Folder, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Project.Folder, path)).Order(StringComparer.Ordinal).ToArray();

        public string[] TrashFiles() =>
            Directory.Exists(Trash) ? Directory.GetFiles(Trash, "*", SearchOption.AllDirectories) : [];
    }

    // A loaded TestGame with a project "Alpha" that holds the given files below its pack folder, the controllers wired as the app wires them,
    // and a trash folder in the temp folder of the game.
    private static async Task<Setup> CreateAsync(bool withSchema, params (string RelativeFile, string Text)[] files)
    {
        var game = new TestGame(withSchema);
        var workspace = game.NewController();
        await workspace.LoadAsync();
        var projects = game.NewProjects(workspace);
        var project = game.NewProject(projects, "Alpha", files);
        var tabs = new TabsViewModel(game.Texts);
        var history = new ActionHistory();
        var dialogs = new FakeEditDialogs();
        var documents = new DocumentsController(tabs, projects, history, game.Runner, dialogs, game.Log, game.Texts);
        var trash = game.Root.File("trash");
        var files2 = new ProjectFilesController(projects, workspace, documents, history, game.Runner, trash, game.Log, game.Texts, TimeProvider.System);
        return new Setup(game, workspace, projects, tabs, history, dialogs, documents, files2, project, trash);
    }

    private static Task<Setup> CreateAsync(params (string RelativeFile, string Text)[] files) => CreateAsync(true, files);

    private static string TableText(string packPath, int value) => TableTsv.Write("x_tables", 0, packPath, Def, [[value]]).Text!;

    private static (string, string) Table(string relativeFile, int value) =>
        (relativeFile, TableText(relativeFile.Replace('\\', '/').Replace(".tsv", string.Empty), value));

    private static async Task<EditableRowsTabViewModel> OpenAsync(Setup setup, string relativeFile)
    {
        var opened = await setup.Projects.OpenFileAsync(setup.Id, relativeFile);
        Assert.NotNull(opened);
        setup.Tabs.Add(setup.Source, opened, setup.Workspace.Schema);
        return Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.Tabs[^1]);
    }

    // A pack with the given entries in the data folder, listed by the workspace; the runner titles are cleared afterwards.
    private static async Task<PackSource> AddPackAsync(Setup setup, params (string Path, byte[] Bytes)[] entries)
    {
        setup.Game.AddPack("a.pack", entries);
        await setup.Workspace.RefreshAsync();
        setup.Game.Runner.Titles.Clear();
        return setup.Workspace.Catalog!.Packs.Single(pack => pack.Name == "a.pack");
    }

    private static FileActionResult Ok(FileActionResult? result)
    {
        Assert.NotNull(result);
        Assert.True(result.Ok, result.Message);
        return result;
    }

    private static FileActionResult Refused(FileActionResult? result)
    {
        Assert.NotNull(result);
        Assert.False(result.Ok);
        Assert.False(string.IsNullOrEmpty(result.Message));
        return result;
    }

    // ---- new files ---------------------------------------------------------------------------------------------------------------

    [Fact] public async Task A_new_table_exists_on_disk_is_one_undo_step_and_raises_FilesChanged()
    {
        using var setup = await CreateAsync();
        var raised = new List<Guid>();
        setup.Files.FilesChanged += (_, id) => raised.Add(id);

        var result = Ok(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "mine"));

        Assert.Equal(@"db\x_tables\mine.tsv", result.RelativeFile);
        Assert.Null(result.Note);
        Assert.True(setup.Exists(@"db\x_tables\mine.tsv"));
        var read = TableTsv.Read(File.ReadAllText(setup.PathOf(@"db\x_tables\mine.tsv")), setup.Workspace.Schema!.Schema!);
        Assert.Empty(read.Table!.Rows);
        Assert.Equal(["Create mine"], setup.Game.Runner.Titles);
        Assert.Equal([setup.Id], raised);
        Assert.True(setup.History.CanUndo);
        Assert.IsType<FileStep>(setup.History.NextUndo);

        setup.Documents.UndoCommand.Execute(null);

        Assert.False(setup.Exists(@"db\x_tables\mine.tsv"));
        Assert.False(setup.History.CanUndo);                                    // one step, not two
        Assert.True(setup.History.CanRedo);
        Assert.Equal([setup.Id, setup.Id], raised);
    }

    [Fact] public async Task Undo_of_a_new_table_moves_it_to_the_trash_and_removes_the_folders_it_created_and_redo_brings_it_back()
    {
        using var setup = await CreateAsync();
        Ok(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "mine"));
        var bytes = File.ReadAllBytes(setup.PathOf(@"db\x_tables\mine.tsv"));
        Assert.True(Directory.Exists(setup.PathOf("db")));

        setup.Documents.UndoCommand.Execute(null);

        Assert.False(setup.Exists(@"db\x_tables\mine.tsv"));
        Assert.False(Directory.Exists(setup.PathOf(@"db\x_tables")));
        Assert.False(Directory.Exists(setup.PathOf("db")));                     // both folders were made by the creation
        var trashed = Assert.Single(setup.TrashFiles());
        Assert.Equal(bytes, File.ReadAllBytes(trashed));
        Assert.StartsWith(setup.Trash, trashed, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(setup.Game.Log.Errors);

        setup.Documents.RedoCommand.Execute(null);

        Assert.Equal(bytes, File.ReadAllBytes(setup.PathOf(@"db\x_tables\mine.tsv")));
        Assert.Empty(setup.TrashFiles());

        // And the folders the redo made are the ones the next undo removes again.
        setup.Documents.UndoCommand.Execute(null);
        Assert.False(Directory.Exists(setup.PathOf("db")));
        Assert.Empty(setup.Dialogs.Messages);
    }

    [Fact] public async Task A_new_loc_file_lands_in_text_db()
    {
        using var setup = await CreateAsync();

        var result = Ok(await setup.Files.CreateLocAsync(setup.Id, "mine"));

        Assert.Equal(@"text\db\mine.loc.tsv", result.RelativeFile);
        Assert.True(setup.Exists(@"text\db\mine.loc.tsv"));
        Assert.Equal(["Create mine"], setup.Game.Runner.Titles);
        Assert.Contains("text/db/mine.loc", File.ReadAllText(setup.PathOf(@"text\db\mine.loc.tsv")), StringComparison.Ordinal);
    }

    [Fact] public async Task A_taken_name_is_refused_with_a_message_and_records_no_step()
    {
        using var setup = await CreateAsync();
        Ok(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "mine"));
        var raised = 0;
        setup.Files.FilesChanged += (_, _) => raised++;
        var before = setup.Snapshot();

        var again = Refused(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "MINE"));

        Assert.StartsWith(setup.Game.Texts.FileProblemTargetExists, again.Message, StringComparison.Ordinal);
        Assert.Contains("already at the target", again.Message, StringComparison.Ordinal);   // the technical detail follows
        Assert.Equal(before, setup.Snapshot());
        Assert.Equal(0, raised);
        Assert.Equal(setup.Game.Texts.FileProblemTargetExists, setup.Files.CheckNewName(setup.Id, @"db\x_tables", "mine", ".tsv"));
        setup.Documents.UndoCommand.Execute(null);
        Assert.False(setup.History.CanUndo);                                    // only the first creation is a step
    }

    [Fact] public async Task A_name_or_a_table_that_cannot_be_made_is_refused_before_anything_is_written()
    {
        using var setup = await CreateAsync();
        var before = setup.Snapshot();

        var dot = Refused(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "a.b"));
        var unknown = Refused(await setup.Files.CreateTableAsync(setup.Id, "nope_tables", "mine"));
        var loc = Refused(await setup.Files.CreateLocAsync(setup.Id, ""));

        Assert.StartsWith(setup.Game.Texts.FileCannotCreate(string.Empty), dot.Message, StringComparison.Ordinal);
        Assert.Contains("table is not in the schema", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("empty", loc.Message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
        Assert.False(setup.History.CanUndo);
    }

    [Fact] public async Task Without_a_schema_no_table_can_be_created_and_none_is_listed()
    {
        using var setup = await CreateAsync(withSchema: false);

        Assert.Empty(setup.Files.CreatableTables);
        var result = Refused(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "mine"));

        Assert.Equal(setup.Game.Texts.FileNoSchema, result.Message);
        Assert.NotNull(Ok(await setup.Files.CreateLocAsync(setup.Id, "mine")));  // a loc file needs no schema
    }

    [Fact] public async Task The_creatable_tables_are_prepared_through_the_runner_once_per_schema_instance()
    {
        using var setup = await CreateAsync();
        var title = setup.Game.Texts.OperationPrepareTables;

        Assert.Empty(setup.Files.CreatableTables);                               // the property never computes: nothing was prepared yet
        Assert.Empty(setup.Game.Runner.Titles);

        var first = await setup.Files.GetCreatableTablesAsync();

        Assert.Equal(["x_tables"], first);
        Assert.Equal([title], setup.Game.Runner.Titles);
        Assert.Same(first, setup.Files.CreatableTables);

        // The second dialog uses the answer: no run.
        Assert.Same(first, await setup.Files.GetCreatableTablesAsync());
        Assert.Equal([title], setup.Game.Runner.Titles);

        // A schema that was loaded again is another instance: the list is prepared again, and not before it is asked for.
        await setup.Workspace.ReloadSchemaAsync();
        setup.Game.Runner.Titles.Clear();

        Assert.Empty(setup.Files.CreatableTables);
        var again = await setup.Files.GetCreatableTablesAsync();

        Assert.Equal(["x_tables"], again);
        Assert.NotSame(first, again);
        Assert.Equal([title], setup.Game.Runner.Titles);
    }

    [Fact] public async Task Preparing_the_tables_gives_nothing_while_busy_or_after_a_cancel_and_keeps_no_list()
    {
        using var setup = await CreateAsync();

        setup.Game.Runner.IsRunning = true;
        Assert.Null(await setup.Files.GetCreatableTablesAsync());
        setup.Game.Runner.IsRunning = false;
        Assert.Empty(setup.Game.Runner.Titles);                                  // nothing was started

        setup.Game.Runner.CancelNext = true;
        Assert.Null(await setup.Files.GetCreatableTablesAsync());
        Assert.Empty(setup.Files.CreatableTables);                               // a cancelled run leaves no list behind

        Assert.Equal(["x_tables"], await setup.Files.GetCreatableTablesAsync());  // and the next try prepares it
    }

    [Fact] public async Task Without_a_schema_the_prepared_list_is_empty_and_nothing_runs()
    {
        using var setup = await CreateAsync(withSchema: false);

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(await setup.Files.GetCreatableTablesAsync()));
        Assert.Empty(setup.Game.Runner.Titles);
    }

    [Fact] public async Task An_open_tab_with_unsaved_work_is_the_reason_not_to_rename_or_delete_the_file()
    {
        using var setup = await CreateAsync(Table(TableA, 42), Table(TableB, 7));
        var a = await OpenAsync(setup, TableA);
        await OpenAsync(setup, TableB);

        Assert.Null(setup.Files.OpenTabReason(setup.Id, TableA));                // open, but nothing to lose
        Assert.Null(setup.Files.OpenTabReason(setup.Id, @"script\other.txt"));    // no tab at all

        a.Rows[0][Value] = "50";

        Assert.Equal(setup.Game.Texts.FileTabUnsaved(TableA.Replace('\\', '/')), setup.Files.OpenTabReason(setup.Id, TableA));
        Assert.Equal(setup.Game.Texts.FileTabUnsaved(TableA.Replace('\\', '/')), setup.Files.OpenTabReason(setup.Id, TableA.Replace('\\', '/')));
        Assert.Null(setup.Files.OpenTabReason(setup.Id, TableB));
    }

    // ---- adopting an entry of a pack ---------------------------------------------------------------------------------------------

    [Fact] public async Task Adopt_of_a_table_writes_the_text_form_under_the_new_name()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes));

        var result = Ok(await setup.Files.AdoptAsync(setup.Id, pack, "db/x_tables/data__", "mine"));

        Assert.Equal(@"db\x_tables\mine.tsv", result.RelativeFile);
        Assert.Null(result.Note);
        Assert.Equal(TableText("db/x_tables/mine", 42), File.ReadAllText(setup.PathOf(@"db\x_tables\mine.tsv")));
        Assert.Equal(["Open data__", "Add mine to the project"], setup.Game.Runner.Titles);
        Assert.IsType<FileStep>(setup.History.NextUndo);

        setup.Documents.UndoCommand.Execute(null);
        Assert.False(setup.Exists(@"db\x_tables\mine.tsv"));
    }

    [Fact] public async Task Adopt_of_a_table_keeps_its_name_without_a_new_name()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes));

        var result = Ok(await setup.Files.AdoptAsync(setup.Id, pack, "db/x_tables/data__", null));

        Assert.Equal(@"db\x_tables\data__.tsv", result.RelativeFile);
        Assert.True(setup.Exists(@"db\x_tables\data__.tsv"));
    }

    [Fact] public async Task Adopt_of_a_script_keeps_its_path()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("script/campaign/main.lua", "print(1)"u8.ToArray()));

        var result = Ok(await setup.Files.AdoptAsync(setup.Id, pack, "script/campaign/main.lua", null));

        Assert.Equal(@"script\campaign\main.lua", result.RelativeFile);
        Assert.Equal("print(1)"u8.ToArray(), File.ReadAllBytes(setup.PathOf(@"script\campaign\main.lua")));
        Assert.Null(result.Note);

        // A script keeps its name: a new name is refused and nothing is written.
        var before = setup.Snapshot();
        var renamed = Refused(await setup.Files.AdoptAsync(setup.Id, pack, "script/campaign/main.lua", "other"));
        Assert.StartsWith(setup.Game.Texts.FileCannotCreate(string.Empty), renamed.Message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
    }

    [Fact] public async Task Adopt_of_a_table_the_dialect_cannot_carry_reports_the_note()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", [1, 2, 3]));   // not a table of the schema

        var result = Ok(await setup.Files.AdoptAsync(setup.Id, pack, "db/x_tables/data__", null));

        Assert.Equal(@"db\x_tables\data__", result.RelativeFile);                   // it stays a binary file, with its own name
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(setup.PathOf(@"db\x_tables\data__")));
        Assert.NotNull(result.Note);
        Assert.StartsWith(setup.Game.Texts.FileAddedBinary(string.Empty), result.Note, StringComparison.Ordinal);
    }

    [Fact] public async Task An_unreadable_entry_is_not_adopted_and_nothing_is_written()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("a.txt", [1]));
        var before = setup.Snapshot();

        var result = Refused(await setup.Files.AdoptAsync(setup.Id, pack, "gone.txt", null));

        Assert.StartsWith(setup.Game.Texts.EntryProblemCorrupt, result.Message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
        Assert.False(setup.History.CanUndo);
        Assert.Equal(["Open gone.txt"], setup.Game.Runner.Titles);              // the second run never started
        Assert.Single(setup.Game.Log.Warnings, line => line.Contains("gone.txt", StringComparison.Ordinal));   // logged once, by the workspace
    }

    [Fact] public async Task A_cancelled_adopt_leaves_nothing_behind()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes));
        var before = setup.Snapshot();
        var raised = 0;
        setup.Files.FilesChanged += (_, _) => raised++;
        // The read run reports nothing; the first report is the one of the write run, which is cancelled right there.
        setup.Game.Runner.OnReport = (_, cancel) => cancel();

        var result = await setup.Files.AdoptAsync(setup.Id, pack, "db/x_tables/data__", "mine");

        Assert.Null(result);
        Assert.Equal(before, setup.Snapshot());                                 // no file, no folder
        Assert.False(setup.History.CanUndo);
        Assert.Equal(0, raised);
        Assert.Empty(setup.TrashFiles());
    }

    // ---- delete ------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Delete_moves_the_file_to_the_trash_and_undo_restores_the_same_bytes()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"), ("script\\b.txt", "other"));
        var bytes = File.ReadAllBytes(setup.PathOf(@"script\a.txt"));
        var raised = new List<Guid>();
        setup.Files.FilesChanged += (_, id) => raised.Add(id);

        var result = Ok(await setup.Files.DeleteAsync(setup.Id, @"script\a.txt"));

        Assert.Equal(@"script\a.txt", result.RelativeFile);
        Assert.False(setup.Exists(@"script\a.txt"));
        Assert.True(setup.Exists(@"script\b.txt"));
        Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(setup.TrashFiles())));
        Assert.Equal(["Delete a.txt"], setup.Game.Runner.Titles);
        Assert.Equal([setup.Id], raised);

        setup.Documents.UndoCommand.Execute(null);

        Assert.Equal(bytes, File.ReadAllBytes(setup.PathOf(@"script\a.txt")));
        Assert.Empty(setup.TrashFiles());
        Assert.Equal([setup.Id, setup.Id], raised);

        // Redo deletes again (a new entry of the trash), and the next undo brings that one back.
        setup.Documents.RedoCommand.Execute(null);
        Assert.False(setup.Exists(@"script\a.txt"));
        Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(setup.TrashFiles())));
        setup.Documents.UndoCommand.Execute(null);
        Assert.Equal(bytes, File.ReadAllBytes(setup.PathOf(@"script\a.txt")));
        Assert.Empty(setup.Dialogs.Messages);
    }

    [Fact] public async Task Delete_of_a_file_that_is_not_there_says_so_and_records_no_step()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"));

        var result = Refused(await setup.Files.DeleteAsync(setup.Id, @"script\gone.txt"));

        Assert.StartsWith(setup.Game.Texts.FileProblemSourceMissing, result.Message, StringComparison.Ordinal);
        Assert.False(setup.History.CanUndo);
        Assert.Empty(setup.TrashFiles());
    }

    [Fact] public async Task Delete_closes_an_unmodified_tab_and_refuses_a_modified_one()
    {
        using var setup = await CreateAsync(Table(TableA, 42), Table(TableB, 7));
        var a = await OpenAsync(setup, TableA);
        var b = await OpenAsync(setup, TableB);
        b.Rows[0][Value] = "8";

        Ok(await setup.Files.DeleteAsync(setup.Id, TableA));

        Assert.DoesNotContain(a, setup.Tabs.Tabs);
        Assert.False(setup.Exists(TableA));

        var refused = Refused(await setup.Files.DeleteAsync(setup.Id, TableB));

        Assert.Equal(setup.Game.Texts.FileTabUnsaved(TableB.Replace('\\', '/')), refused.Message);
        Assert.True(setup.Exists(TableB));
        Assert.Contains(b, setup.Tabs.Tabs);
        Assert.Equal("8", b.Rows[0][Value]);

        // A cell that is being edited counts as well.
        Assert.True(await setup.Documents.SaveAsync([b]));
        b.CommitPendingEdit = () => true;
        b.SetPendingEdit(true);
        Refused(await setup.Files.DeleteAsync(setup.Id, TableB));
        Assert.True(setup.Exists(TableB));
        b.SetPendingEdit(false);
        Ok(await setup.Files.DeleteAsync(setup.Id, TableB));
        Assert.DoesNotContain(b, setup.Tabs.Tabs);
    }

    [Fact] public async Task A_step_that_cannot_run_any_more_is_dropped_with_a_message()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"));
        Ok(await setup.Files.DeleteAsync(setup.Id, @"script\a.txt"));
        File.Delete(Assert.Single(setup.TrashFiles()));                         // somebody emptied the trash by hand

        setup.Documents.UndoCommand.Execute(null);

        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Game.Texts.EditUndoFailedTitle, message.Title);
        Assert.Contains("SourceMissing", message.Message, StringComparison.Ordinal);
        Assert.False(setup.History.CanUndo);
        Assert.False(setup.History.CanRedo);
        Assert.False(setup.Exists(@"script\a.txt"));                            // nothing was created
    }

    [Fact] public async Task Undo_of_a_delete_does_not_overwrite_a_file_created_meanwhile()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"));
        Ok(await setup.Files.DeleteAsync(setup.Id, @"script\a.txt"));
        File.WriteAllText(setup.PathOf(@"script\a.txt"), "new");                // another program was faster

        setup.Documents.UndoCommand.Execute(null);

        Assert.Equal("new", File.ReadAllText(setup.PathOf(@"script\a.txt")));
        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Contains("TargetExists", message.Message, StringComparison.Ordinal);
        Assert.False(setup.History.CanUndo);
        Assert.Equal("hello"u8.ToArray(), File.ReadAllBytes(Assert.Single(setup.TrashFiles())));   // the deleted bytes are still safe
    }

    // ---- a dropped step: where the file is, and what is never trashed -----------------------------------------------------------------

    [Fact] public async Task A_file_left_in_the_trash_by_a_dropped_step_is_named_in_the_message_and_the_log()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"));
        Ok(await setup.Files.DeleteAsync(setup.Id, @"script\a.txt"));
        var trashed = Assert.Single(setup.TrashFiles());
        File.WriteAllText(setup.PathOf(@"script\a.txt"), "new");                // another program was faster

        setup.Documents.UndoCommand.Execute(null);

        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Contains(trashed, message.Message, StringComparison.Ordinal);    // the user can find the old file
        Assert.Single(setup.Game.Log.Warnings, line => line.Contains(trashed, StringComparison.Ordinal));
        Assert.Equal("new", File.ReadAllText(setup.PathOf(@"script\a.txt")));
        Assert.Equal("hello"u8.ToArray(), File.ReadAllBytes(trashed));
        Assert.False(setup.History.CanUndo);
        Assert.False(setup.History.CanRedo);
    }

    [Fact] public async Task Steps_of_a_project_that_left_the_list_log_the_files_that_stay_in_the_trash()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"));
        var other = setup.Game.NewProject(setup.Projects, "Beta", ("script\\b.txt", "world"));
        Ok(await setup.Files.DeleteAsync(other.Id, @"script\b.txt"));
        var trashed = Assert.Single(setup.TrashFiles());
        Ok(await setup.Files.CreateLocAsync(other.Id, "mine"));                 // a step whose file is not in the trash: no line

        Assert.True(setup.Projects.Remove(other.Id).Ok);

        Assert.False(setup.History.CanUndo);
        Assert.Single(setup.Game.Log.Warnings, line => line.Contains(trashed, StringComparison.Ordinal));
        Assert.Equal("world"u8.ToArray(), File.ReadAllBytes(trashed));
    }

    [Fact] public async Task Undo_of_a_new_file_that_was_saved_to_meanwhile_does_not_trash_the_saved_work()
    {
        using var setup = await CreateAsync();
        var created = Ok(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "mine"));
        var file = created.RelativeFile!;
        var tab = await OpenAsync(setup, file);
        tab.InsertRowCommand.Execute(null);
        Assert.True(await setup.Documents.SaveAsync([tab]));
        setup.Tabs.Close(tab);
        var saved = File.ReadAllBytes(setup.PathOf(file));

        setup.Documents.UndoCommand.Execute(null);

        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Game.Texts.EditUndoFailedTitle, message.Title);
        Assert.Equal(setup.Game.Texts.FileStepFileChanged(file), message.Message);
        Assert.Equal(saved, File.ReadAllBytes(setup.PathOf(file)));             // the saved work is where it was
        Assert.Empty(setup.TrashFiles());
        Assert.False(setup.History.CanUndo);
        Assert.False(setup.History.CanRedo);                                    // the step was dropped
    }

    [Fact] public async Task Redo_of_a_delete_does_not_trash_a_restored_file_that_was_edited_meanwhile()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"));
        Ok(await setup.Files.DeleteAsync(setup.Id, @"script\a.txt"));
        setup.Documents.UndoCommand.Execute(null);                              // restored
        File.WriteAllText(setup.PathOf(@"script\a.txt"), "edited by another program");

        setup.Documents.RedoCommand.Execute(null);

        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Game.Texts.FileStepFileChanged(@"script\a.txt"), message.Message);
        Assert.Equal("edited by another program", File.ReadAllText(setup.PathOf(@"script\a.txt")));
        Assert.Empty(setup.TrashFiles());
        Assert.False(setup.History.CanUndo);
        Assert.False(setup.History.CanRedo);
    }

    [Fact] public async Task A_file_that_another_program_wrote_after_a_dropped_step_is_not_trashed_by_the_undo_below_it()
    {
        using var setup = await CreateAsync();
        var created = Ok(await setup.Files.CreateLocAsync(setup.Id, "mine"));
        var file = created.RelativeFile!;
        Ok(await setup.Files.DeleteAsync(setup.Id, file));
        var trashed = Assert.Single(setup.TrashFiles());
        File.WriteAllText(setup.PathOf(file), "from another program");

        setup.Documents.UndoCommand.Execute(null);                              // the delete cannot be taken back: dropped, the old file is named

        // The creation below it deals with the same file: it is removed too, so no later undo can trash the foreign file.
        var message = Assert.Single(setup.Dialogs.Messages).Message;
        Assert.Contains(trashed, message, StringComparison.Ordinal);
        Assert.EndsWith(setup.Game.Texts.FileFurtherStepsRemoved(file), message, StringComparison.Ordinal);
        Assert.False(setup.History.CanUndo);
        Assert.False(setup.History.CanRedo);
        Assert.Equal("from another program", File.ReadAllText(setup.PathOf(file)));
        Assert.Single(setup.TrashFiles());                                      // only the file the delete put there
    }

    // ---- steps that leave the history: the files they hold in the trash are named in the log ----------------------------------------------

    [Fact] public async Task A_new_step_that_discards_a_file_in_the_trash_from_the_redo_stack_logs_where_it_is()
    {
        using var setup = await CreateAsync(("script\\b.txt", "world"));
        Ok(await setup.Files.CreateLocAsync(setup.Id, "mine"));
        setup.Documents.UndoCommand.Execute(null);                              // the new file is in the trash, its step waits for a redo
        var trashed = Assert.Single(setup.TrashFiles());
        Assert.True(setup.History.CanRedo);
        var before = setup.Game.Log.Warnings.Count;

        Ok(await setup.Files.CreateLocAsync(setup.Id, "other"));

        Assert.False(setup.History.CanRedo);                                    // the step is gone
        var line = Assert.Single(setup.Game.Log.Warnings.Skip(before));
        Assert.Contains(trashed, line, StringComparison.Ordinal);               // the full trash path
        Assert.Contains(@"text\db\mine.loc.tsv", line, StringComparison.Ordinal);
        Assert.True(File.Exists(trashed));                                      // the file itself is untouched

        // A step on the redo stack that holds no file in the trash leaves no line: the delete is undone, its file is back in the project.
        Ok(await setup.Files.DeleteAsync(setup.Id, @"script\b.txt"));
        setup.Documents.UndoCommand.Execute(null);
        Assert.True(setup.History.CanRedo);
        before = setup.Game.Log.Warnings.Count;

        Ok(await setup.Files.CreateLocAsync(setup.Id, "third"));

        Assert.False(setup.History.CanRedo);
        Assert.Equal(before, setup.Game.Log.Warnings.Count);
    }

    [Fact] public async Task A_dropped_step_takes_the_other_steps_of_the_same_file_with_it_and_leaves_the_foreign_file_alone()
    {
        using var setup = await CreateAsync();
        var beta = setup.Game.NewProject(setup.Projects, "Beta");
        Ok(await setup.Files.CreateLocAsync(setup.Id, "other"));                // another file of the project: stays
        Ok(await setup.Files.CreateLocAsync(beta.Id, "a"));                     // the same file name in another project: stays
        var created = Ok(await setup.Files.CreateLocAsync(setup.Id, "a"));
        var file = created.RelativeFile!;
        var renamed = Ok(await setup.Files.RenameAsync(setup.Id, file, "b"));
        setup.Documents.UndoCommand.Execute(null);                              // b -> a
        setup.Documents.UndoCommand.Execute(null);                              // the file a goes to the trash
        var trashed = Assert.Single(setup.TrashFiles());
        Directory.CreateDirectory(Path.GetDirectoryName(setup.PathOf(file))!);
        File.WriteAllText(setup.PathOf(file), "from another program");           // another program wrote a
        Assert.True(setup.History.CanRedo);
        var before = setup.Game.Log.Warnings.Count;

        setup.Documents.RedoCommand.Execute(null);                              // the creation cannot be repeated: dropped

        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Contains(trashed, message.Message, StringComparison.Ordinal);    // the file that stays in the trash
        Assert.EndsWith(setup.Game.Texts.FileFurtherStepsRemoved(file), message.Message, StringComparison.Ordinal);
        Assert.False(setup.History.CanRedo);                                    // the rename a -> b is gone as well: it would rename the foreign file
        Assert.Equal("from another program", File.ReadAllText(setup.PathOf(file)));
        Assert.False(setup.Exists(renamed.RelativeFile!));
        Assert.True(setup.History.CanUndo);
        Assert.Equal(beta.Id, Assert.IsType<FileStep>(setup.History.NextUndo).ProjectId);

        // One line for the step that was removed as well, one for the file the dropped step left in the trash.
        var lines = setup.Game.Log.Warnings.Skip(before).ToList();
        Assert.Single(lines, line => line.Contains("Rename", StringComparison.Ordinal) && line.Contains(@"text\db\b.loc.tsv", StringComparison.Ordinal));
        Assert.Single(lines, line => line.Contains(trashed, StringComparison.Ordinal));

        // The steps that stay still work.
        setup.Documents.UndoCommand.Execute(null);
        setup.Documents.UndoCommand.Execute(null);
        Assert.Single(setup.Dialogs.Messages);
        Assert.False(setup.History.CanUndo);
    }

    [Fact] public async Task A_dropped_step_also_takes_a_step_of_a_name_the_file_had_later_with_it()
    {
        using var setup = await CreateAsync();
        var created = Ok(await setup.Files.CreateLocAsync(setup.Id, "a"));
        var first = Ok(await setup.Files.RenameAsync(setup.Id, created.RelativeFile!, "b"));
        Ok(await setup.Files.RenameAsync(setup.Id, first.RelativeFile!, "c"));  // a -> b -> c
        setup.Documents.UndoCommand.Execute(null);
        setup.Documents.UndoCommand.Execute(null);
        setup.Documents.UndoCommand.Execute(null);                              // the file a is in the trash
        Directory.CreateDirectory(Path.GetDirectoryName(setup.PathOf(created.RelativeFile!))!);
        File.WriteAllText(setup.PathOf(created.RelativeFile!), "from another program");

        setup.Documents.RedoCommand.Execute(null);

        Assert.False(setup.History.CanRedo);                                    // both renames are gone
        Assert.False(setup.History.CanUndo);
        Assert.Equal("from another program", File.ReadAllText(setup.PathOf(created.RelativeFile!)));
        Assert.Single(setup.Dialogs.Messages);
    }

    // ---- rename ------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task CheckNewName_takes_a_slash_in_the_folder_as_a_backslash()
    {
        using var setup = await CreateAsync(Table(TableA, 42), Table(TableB, 7));

        Assert.Equal(setup.Game.Texts.FileNameUnchanged, setup.Files.CheckNewName(setup.Id, "db/x_tables", "a", ".tsv", TableA));
        Assert.Equal(setup.Game.Texts.FileNameUnchanged, setup.Files.CheckNewName(setup.Id, "db/x_tables/", "a", ".tsv", TableA));
        Assert.Null(setup.Files.CheckNewName(setup.Id, "db/x_tables", "A", ".tsv", TableA));                         // only the case changes
        Assert.Equal(setup.Game.Texts.FileProblemTargetExists, setup.Files.CheckNewName(setup.Id, "db/x_tables", "b", ".tsv", TableA));
        Assert.Null(setup.Files.CheckNewName(setup.Id, "db/x_tables", "fresh", ".tsv"));
    }

    [Fact] public async Task CheckNewName_knows_the_current_file_of_a_rename()
    {
        using var setup = await CreateAsync(Table(TableA, 42), Table(TableB, 7));

        Assert.Equal(setup.Game.Texts.FileNameUnchanged, setup.Files.CheckNewName(setup.Id, @"db\x_tables", "a", ".tsv", TableA));
        Assert.Equal(setup.Game.Texts.FileNameUnchanged, setup.Files.CheckNewName(setup.Id, @"db\x_tables", "a", ".tsv", "db/x_tables/a.tsv"));
        Assert.Null(setup.Files.CheckNewName(setup.Id, @"db\x_tables", "A", ".tsv", TableA));                       // only the case changes
        Assert.Equal(setup.Game.Texts.FileProblemTargetExists, setup.Files.CheckNewName(setup.Id, @"db\x_tables", "B", ".tsv", TableA));
        Assert.Equal(setup.Game.Texts.FileProblemTargetExists, setup.Files.CheckNewName(setup.Id, @"db\x_tables", "a", ".tsv"));  // a new file: taken
    }

    [Fact] public async Task A_rename_that_only_changes_the_case_works_and_its_undo_closes_the_tab_and_reopens_the_old_spelling()
    {
        using var setup = await CreateAsync(Table(TableA, 42));
        var requests = new List<(Guid ProjectId, string RelativeFile)>();
        setup.Files.OpenRequested += (_, request) => requests.Add(request);
        var a = await OpenAsync(setup, TableA);

        var renamed = Ok(await setup.Files.RenameAsync(setup.Id, TableA, "A"));

        Assert.Equal(@"db\x_tables\A.tsv", renamed.RelativeFile);
        Assert.Contains(@"A.tsv", Directory.GetFiles(Path.GetDirectoryName(setup.PathOf(TableA))!).Select(Path.GetFileName));
        Assert.DoesNotContain(a, setup.Tabs.Tabs);
        Assert.Equal((setup.Id, @"db\x_tables\A.tsv"), Assert.Single(requests));
        var upper = await OpenAsync(setup, @"db\x_tables\A.tsv");

        setup.Documents.UndoCommand.Execute(null);

        Assert.Contains(@"a.tsv", Directory.GetFiles(Path.GetDirectoryName(setup.PathOf(TableA))!).Select(Path.GetFileName));
        Assert.DoesNotContain(upper, setup.Tabs.Tabs);                          // a rename of the spelling counts as a rename
        Assert.Equal((setup.Id, TableA), requests[^1]);
        Assert.Equal(2, requests.Count);
    }

    [Fact] public async Task Rename_changes_only_the_name_part_and_undo_renames_back()
    {
        using var setup = await CreateAsync(Table(TableA, 42), (@"text\db\a.loc.tsv", "loc"), (@"script\a.b.lua", "lua"), (@"script\plain", "plain"));
        var raised = new List<Guid>();
        setup.Files.FilesChanged += (_, id) => raised.Add(id);
        var bytes = File.ReadAllBytes(setup.PathOf(TableA));

        var table = Ok(await setup.Files.RenameAsync(setup.Id, TableA, "z"));
        var loc = Ok(await setup.Files.RenameAsync(setup.Id, @"text\db\a.loc.tsv", "z"));
        var script = Ok(await setup.Files.RenameAsync(setup.Id, @"script\a.b.lua", "c.d"));
        var plain = Ok(await setup.Files.RenameAsync(setup.Id, @"script\plain", "other"));

        Assert.Equal(@"db\x_tables\z.tsv", table.RelativeFile);
        Assert.Equal(@"text\db\z.loc.tsv", loc.RelativeFile);
        Assert.Equal(@"script\c.d.lua", script.RelativeFile);
        Assert.Equal(@"script\other", plain.RelativeFile);
        Assert.Equal(bytes, File.ReadAllBytes(setup.PathOf(@"db\x_tables\z.tsv")));   // the content is not touched
        Assert.False(setup.Exists(TableA));
        Assert.Equal(["Rename a.tsv", "Rename a.loc.tsv", "Rename a.b.lua", "Rename plain"], setup.Game.Runner.Titles);
        Assert.Equal(4, raised.Count);

        for (var i = 0; i < 4; i++)
        {
            setup.Documents.UndoCommand.Execute(null);
        }

        Assert.True(setup.Exists(TableA));
        Assert.True(setup.Exists(@"text\db\a.loc.tsv"));
        Assert.True(setup.Exists(@"script\a.b.lua"));
        Assert.True(setup.Exists(@"script\plain"));
        Assert.False(setup.Exists(@"db\x_tables\z.tsv"));
        Assert.Equal(bytes, File.ReadAllBytes(setup.PathOf(TableA)));
        Assert.Empty(setup.Dialogs.Messages);

        setup.Documents.RedoCommand.Execute(null);                              // the step undone last is the first rename
        Assert.False(setup.Exists(TableA));
        Assert.True(setup.Exists(@"db\x_tables\z.tsv"));
        Assert.True(setup.Exists(@"script\plain"));
    }

    [Fact] public async Task Rename_reopens_the_tab_under_the_new_name()
    {
        using var setup = await CreateAsync(Table(TableA, 42), Table(TableB, 7));
        var a = await OpenAsync(setup, TableA);
        var requests = new List<(Guid ProjectId, string RelativeFile)>();
        setup.Files.OpenRequested += (_, request) => requests.Add(request);

        // A file that is not open is renamed without a request.
        Ok(await setup.Files.RenameAsync(setup.Id, TableB, "y"));
        Assert.Empty(requests);
        Assert.Contains(a, setup.Tabs.Tabs);

        Ok(await setup.Files.RenameAsync(setup.Id, TableA, "z"));

        Assert.DoesNotContain(a, setup.Tabs.Tabs);                              // the tab of the old name is closed
        Assert.Equal((setup.Id, @"db\x_tables\z.tsv"), Assert.Single(requests));
        Assert.True(setup.Exists(@"db\x_tables\z.tsv"));
    }

    [Fact] public async Task Undo_of_a_rename_closes_the_tab_of_the_new_name_and_asks_to_open_the_old_one()
    {
        using var setup = await CreateAsync(Table(TableA, 42));
        Ok(await setup.Files.RenameAsync(setup.Id, TableA, "z"));
        var z = await OpenAsync(setup, @"db\x_tables\z.tsv");
        var requests = new List<(Guid ProjectId, string RelativeFile)>();
        setup.Files.OpenRequested += (_, request) => requests.Add(request);
        var changed = new List<Guid>();
        setup.Files.FilesChanged += (_, id) => changed.Add(id);

        setup.Documents.UndoCommand.Execute(null);

        Assert.DoesNotContain(z, setup.Tabs.Tabs);
        Assert.Equal((setup.Id, TableA), Assert.Single(requests));
        Assert.Equal([setup.Id], changed);
        Assert.True(setup.Exists(TableA));
    }

    [Fact] public async Task Rename_of_a_table_refuses_a_name_with_a_dot_and_a_taken_name()
    {
        using var setup = await CreateAsync(Table(TableA, 42), Table(TableB, 7));
        var before = setup.Snapshot();

        var dot = Refused(await setup.Files.RenameAsync(setup.Id, TableA, "x.y"));
        var taken = Refused(await setup.Files.RenameAsync(setup.Id, TableA, "B"));
        var same = Refused(await setup.Files.RenameAsync(setup.Id, TableA, "a"));
        var empty = Refused(await setup.Files.RenameAsync(setup.Id, TableA, ""));

        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), dot.Message, StringComparison.Ordinal);
        Assert.Contains("dot", dot.Message, StringComparison.Ordinal);
        Assert.Equal(setup.Game.Texts.FileProblemTargetExists, taken.Message);
        Assert.Equal(setup.Game.Texts.FileNameUnchanged, same.Message);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), empty.Message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
        Assert.False(setup.History.CanUndo);
        Assert.Empty(setup.Game.Runner.Titles);
    }

    [Fact] public async Task Rename_refuses_a_name_that_changes_the_kind_leaves_a_folder_or_is_not_a_file_name()
    {
        using var setup = await CreateAsync((@"db\x_tables\data__", "raw"), (@"text\db\plain", "raw"), (@"script\a.lua", "lua"));
        var before = setup.Snapshot();

        // A raw file named like the text form of a table (or a loc file) would be taken for one.
        var kind = Refused(await setup.Files.RenameAsync(setup.Id, @"db\x_tables\data__", "q.tsv"));
        var loc = Refused(await setup.Files.RenameAsync(setup.Id, @"text\db\plain", "q.loc.tsv"));
        var slash = Refused(await setup.Files.RenameAsync(setup.Id, @"script\a.lua", @"..\b"));
        var folder = Refused(await setup.Files.RenameAsync(setup.Id, @"script\a.lua", "sub/b"));
        var ignored = Refused(await setup.Files.RenameAsync(setup.Id, @"script\a.lua", "~$x"));   // a project leaves out lock files and backups

        Assert.Equal(setup.Game.Texts.FileRenameChangesKind, kind.Message);
        Assert.Equal(setup.Game.Texts.FileRenameChangesKind, loc.Message);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), slash.Message, StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), folder.Message, StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), ignored.Message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
    }

    [Fact] public async Task Rename_of_a_file_with_a_modified_tab_is_refused()
    {
        using var setup = await CreateAsync(Table(TableA, 42));
        var a = await OpenAsync(setup, TableA);
        a.Rows[0][Value] = "50";

        var result = Refused(await setup.Files.RenameAsync(setup.Id, TableA, "z"));

        Assert.Equal(setup.Game.Texts.FileTabUnsaved(TableA.Replace('\\', '/')), result.Message);
        Assert.True(setup.Exists(TableA));
        Assert.Contains(a, setup.Tabs.Tabs);
        Assert.DoesNotContain(setup.Game.Runner.Titles, title => title.StartsWith("Rename", StringComparison.Ordinal));
    }

    // ---- undo and redo with open tabs ----------------------------------------------------------------------------------------------

    // The brief's sequence is "edit a cell, undo twice": the first undo takes the edit back and the document is clean again, so the second
    // undo meets an unmodified tab and runs. A tab is modified against its last save, so the test saves after the edit: then the undo of the edit
    // makes the tab differ from the save, and the next undo, the one of the file step, is refused.
    [Fact] public async Task Undo_of_a_file_step_with_a_modified_tab_is_refused_and_the_step_stays()
    {
        using var setup = await CreateAsync();
        var created = Ok(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "mine"));
        var tab = await OpenAsync(setup, created.RelativeFile!);
        tab.InsertRowCommand.Execute(null);
        Assert.True(await setup.Documents.SaveAsync([tab]));
        setup.Documents.UndoCommand.Execute(null);                              // the new row is gone: the tab differs from the saved file
        Assert.True(tab.IsModified);

        setup.Documents.UndoCommand.Execute(null);                              // the next step is the creation of the file

        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Game.Texts.EditUndoBlockedTitle, message.Title);
        Assert.Equal(setup.Game.Texts.FileTabUnsaved(created.RelativeFile!.Replace('\\', '/')), message.Message);
        Assert.True(setup.Exists(created.RelativeFile!));
        Assert.Contains(tab, setup.Tabs.Tabs);
        Assert.True(setup.History.CanUndo);                                     // the step stays
        Assert.IsType<FileStep>(setup.History.NextUndo);
        Assert.Empty(setup.TrashFiles());

        // Saved, the tab no longer blocks the step. But the file is not the one the step created any more (the save wrote the user's work into
        // it), so the undo does not trash it: the step is dropped and the file stays (see Undo_of_a_new_file_that_was_saved_to_meanwhile...).
        Assert.True(await setup.Documents.SaveAsync([tab]));
        setup.Documents.UndoCommand.Execute(null);
        Assert.Equal(2, setup.Dialogs.Messages.Count);
        Assert.Equal(setup.Game.Texts.FileStepFileChanged(created.RelativeFile!), setup.Dialogs.Messages[1].Message);
        Assert.True(setup.Exists(created.RelativeFile!));
        Assert.Contains(tab, setup.Tabs.Tabs);                                  // the step did not run: its tab stays
        Assert.Empty(setup.TrashFiles());
        Assert.False(setup.History.CanUndo);
    }

    [Fact] public async Task The_undo_of_a_new_file_closes_an_unmodified_tab_of_it_and_the_redo_leaves_no_tab()
    {
        using var setup = await CreateAsync();
        var created = Ok(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "mine"));
        var tab = await OpenAsync(setup, created.RelativeFile!);

        setup.Documents.UndoCommand.Execute(null);

        Assert.DoesNotContain(tab, setup.Tabs.Tabs);
        Assert.False(setup.Exists(created.RelativeFile!));

        setup.Documents.RedoCommand.Execute(null);
        Assert.True(setup.Exists(created.RelativeFile!));
        Assert.Empty(setup.Tabs.Tabs);
        Assert.Empty(setup.Dialogs.Messages);
    }

    // ---- the list of projects and the busy state -----------------------------------------------------------------------------------

    [Fact] public async Task File_steps_of_a_project_that_left_the_list_are_removed()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"));
        var other = setup.Game.NewProject(setup.Projects, "Beta", ("script\\b.txt", "world"));
        Ok(await setup.Files.CreateLocAsync(setup.Id, "mine"));
        Ok(await setup.Files.DeleteAsync(other.Id, @"script\b.txt"));
        Ok(await setup.Files.CreateLocAsync(other.Id, "other"));
        setup.Documents.UndoCommand.Execute(null);                              // a step of Beta is on the redo stack
        Assert.True(setup.History.CanRedo);

        Assert.True(setup.Projects.Remove(other.Id).Ok);

        Assert.False(setup.History.CanRedo);                                    // the step of Beta on the redo stack is gone
        Assert.True(setup.History.CanUndo);
        Assert.Equal(setup.Id, Assert.IsType<FileStep>(setup.History.NextUndo).ProjectId);
        setup.Documents.UndoCommand.Execute(null);                              // the step of Alpha stays: the delete step of Beta is gone below it
        Assert.False(setup.History.CanUndo);
        Assert.False(setup.Exists(@"text\db\mine.loc.tsv"));
        Assert.Empty(setup.Dialogs.Messages);
    }

    [Fact] public async Task Nothing_starts_while_an_operation_runs()
    {
        using var setup = await CreateAsync(Table(TableA, 42), ("script\\a.txt", "hello"));
        var pack = new PackSource("x.pack", "x.pack", PackOrigin.DataFolderMod, null, null, null, PackProblem.None, null);
        var before = setup.Snapshot();
        Assert.Null(setup.Files.DisabledReason(setup.Id));
        setup.Game.Runner.IsRunning = true;

        Assert.Equal(setup.Game.Texts.ProjectBusy, setup.Files.DisabledReason(setup.Id));
        Assert.Null(await setup.Files.CreateTableAsync(setup.Id, "x_tables", "mine"));
        Assert.Null(await setup.Files.CreateLocAsync(setup.Id, "mine"));
        Assert.Null(await setup.Files.AdoptAsync(setup.Id, pack, "a.txt", null));
        Assert.Null(await setup.Files.RenameAsync(setup.Id, TableA, "z"));
        Assert.Null(await setup.Files.DeleteAsync(setup.Id, @"script\a.txt"));

        setup.Game.Runner.IsRunning = false;
        Assert.Empty(setup.Game.Runner.Titles);
        Assert.Equal(before, setup.Snapshot());
        Assert.False(setup.History.CanUndo);
        Assert.Null(setup.Files.DisabledReason(setup.Id));
    }

    [Fact] public async Task An_unknown_or_unusable_project_is_a_message_not_a_run()
    {
        using var setup = await CreateAsync(("script\\a.txt", "hello"));
        var unknown = Guid.NewGuid();

        Assert.Equal(setup.Game.Texts.ProjectUnknown, setup.Files.DisabledReason(unknown));
        Assert.Equal(setup.Game.Texts.ProjectUnknown, Refused(await setup.Files.CreateLocAsync(unknown, "mine")).Message);
        Assert.Equal(setup.Game.Texts.ProjectUnknown, setup.Files.CheckNewName(unknown, "", "x", ".tsv"));

        File.Delete(Path.Combine(setup.Project.Folder, ProjectFile.FileName));
        setup.Projects.Reload();

        Assert.StartsWith(setup.Game.Texts.ProjectProblemNotFound, setup.Files.DisabledReason(setup.Id), StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.ProjectProblemNotFound, Refused(await setup.Files.DeleteAsync(setup.Id, @"script\a.txt")).Message, StringComparison.Ordinal);
        Assert.True(File.Exists(setup.PathOf(@"script\a.txt")));
        Assert.Empty(setup.Game.Runner.Titles);
    }

    // ---- the trash at start --------------------------------------------------------------------------------------------------------

    private sealed class ThrowingTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new IOException("the clock failed");
    }

    private static ProjectFilesController ControllerWith(Setup setup, TimeProvider time) =>
        new(setup.Projects, setup.Workspace, setup.Documents, setup.History, setup.Game.Runner, setup.Trash, setup.Game.Log, setup.Game.Texts, time);

    // An entry folder of the trash as ProjectTrash names it (UTC time and 8 lower case hex characters) with one file in it.
    private static string TrashEntry(Setup setup, DateTimeOffset stamp, string hex)
    {
        var entry = Path.Combine(setup.Trash, setup.Id.ToString("D"), stamp.UtcDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + hex);
        Directory.CreateDirectory(entry);
        File.WriteAllText(Path.Combine(entry, "a.txt"), "x");
        return entry;
    }

    [Fact] public async Task CleanupTrashAsync_removes_entries_older_than_30_days_silently_and_logs_the_count()
    {
        using var setup = await CreateAsync();
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var old = TrashEntry(setup, now - TimeSpan.FromDays(31), "0a1b2c3d");
        var young = TrashEntry(setup, now - TimeSpan.FromDays(29), "4e5f6a7b");
        var files = ControllerWith(setup, new FakeTimeProvider(now));
        setup.Game.Runner.Titles.Clear();

        await files.CleanupTrashAsync();

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(young));
        Assert.Empty(setup.Game.Runner.Titles);                                  // silent: no runner, no window
        Assert.Empty(setup.Dialogs.Messages);
        var line = Assert.Single(setup.Game.Log.Infos, info => info.Contains("trash", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("1", line, StringComparison.Ordinal);
        Assert.Empty(setup.Game.Log.Warnings);
        Assert.Empty(setup.Game.Log.Errors);

        // Nothing left to remove: nothing is logged.
        var infos = setup.Game.Log.Infos.Count;
        await files.CleanupTrashAsync();
        Assert.Equal(infos, setup.Game.Log.Infos.Count);
        Assert.True(Directory.Exists(young));
    }

    [Fact] public async Task CleanupTrashAsync_without_a_trash_folder_does_nothing()
    {
        using var setup = await CreateAsync();
        var files = ControllerWith(setup, new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        var infos = setup.Game.Log.Infos.Count;

        await files.CleanupTrashAsync();

        Assert.False(Directory.Exists(setup.Trash));                             // the cleanup creates nothing
        Assert.Equal(infos, setup.Game.Log.Infos.Count);
        Assert.Empty(setup.Game.Log.Warnings);
    }

    [Fact] public async Task A_failing_cleanup_of_the_trash_only_logs_and_never_throws()
    {
        using var setup = await CreateAsync();
        var young = TrashEntry(setup, DateTimeOffset.UtcNow, "0a1b2c3d");        // the trash exists, so the cleanup reaches the clock
        var files = ControllerWith(setup, new ThrowingTime());

        await files.CleanupTrashAsync();

        var line = Assert.Single(setup.Game.Log.Warnings);
        Assert.Contains("clock", line, StringComparison.Ordinal);
        Assert.True(Directory.Exists(young));
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Game.Runner.Titles);
    }

    // ---- names -------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task CheckNewName_judges_the_name_the_folder_and_the_extension()
    {
        using var setup = await CreateAsync(Table(TableA, 42), ("script\\a.txt", "hello"));

        Assert.Null(setup.Files.CheckNewName(setup.Id, @"db\x_tables", "fresh", ".tsv"));
        Assert.Null(setup.Files.CheckNewName(setup.Id, @"text\db", "fresh", ".loc.tsv"));
        Assert.Null(setup.Files.CheckNewName(setup.Id, "", "fresh", ".lua"));                     // the pack folder itself
        Assert.Null(setup.Files.CheckNewName(setup.Id, "script", "a.b", ".lua"));                 // a dot is fine for other files
        Assert.Equal(setup.Game.Texts.FileProblemTargetExists, setup.Files.CheckNewName(setup.Id, @"db\x_tables", "A", ".tsv"));
        Assert.Equal(setup.Game.Texts.FileProblemTargetExists, setup.Files.CheckNewName(setup.Id, "script", "a", ".txt"));
        Assert.Equal(setup.Game.Texts.FileProblemTargetExists, setup.Files.CheckNewName(setup.Id, @"script\", "a", ".txt"));   // a trailing separator is fine
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), setup.Files.CheckNewName(setup.Id, @"db\x_tables", "a.b", ".tsv"), StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), setup.Files.CheckNewName(setup.Id, @"db\x_tables", "", ".tsv"), StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), setup.Files.CheckNewName(setup.Id, "script", "", ".lua"), StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), setup.Files.CheckNewName(setup.Id, "script", "a/b", ".lua"), StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), setup.Files.CheckNewName(setup.Id, "script", "x", ".bak"), StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), setup.Files.CheckNewName(setup.Id, @"..\outside", "x", ".lua"), StringComparison.Ordinal);
        Assert.StartsWith(setup.Game.Texts.FileNameInvalid(string.Empty), setup.Files.CheckNewName(setup.Id, "script", "con", ".lua"), StringComparison.Ordinal);
    }
}

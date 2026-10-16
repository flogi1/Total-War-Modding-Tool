using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ProjectFilesViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string TableA = @"db\x_tables\a.tsv";
    private const string TableB = @"db\x_tables\b.tsv";

    private static readonly TableDefinition Def = new(0, [new FieldDefinition("value", FieldType.I32, nameof(FieldType.I32), false, null)]);

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, ProjectsController Projects, TabsViewModel Tabs, ActionHistory History,
        DocumentsController Documents, ProjectFilesController Files, FakeFileDialogs Dialogs, ProjectFilesViewModel ViewModel, ProjectState Project,
        List<(Guid ProjectId, string RelativeFile)> Opened) : IDisposable
    {
        public Guid Id => Project.Id;

        public AppTexts Texts => Game.Texts;

        public string PathOf(string relativeFile) => Path.Combine(Project.Folder, "pack", relativeFile);

        public bool Exists(string relativeFile) => File.Exists(PathOf(relativeFile));

        public void Dispose() => Game.Dispose();

        // Every file and folder of the project folder, relative, sorted: what a refused or cancelled command must leave as it was.
        public string[] Snapshot() =>
            Directory.EnumerateFileSystemEntries(Project.Folder, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Project.Folder, path)).Order(StringComparer.Ordinal).ToArray();

        // Nothing was asked, nothing was said and nothing was started.
        public void AssertSilent()
        {
            Assert.Empty(Dialogs.NameAsks);
            Assert.Empty(Dialogs.TableAsks);
            Assert.Empty(Dialogs.Confirms);
            Assert.Empty(Dialogs.Messages);
            Assert.Empty(Opened);
            Assert.Null(ViewModel.StatusMessage);
            Assert.Empty(Game.Runner.Titles);
            Assert.False(History.CanUndo);
        }
    }

    // A loaded TestGame with the project "name" that holds the given files below its pack folder, the controllers wired as the app wires them
    // and the view model over a fake dialog. The project is created through the controller directly: the name of a test project is not always
    // a good pack name.
    private static async Task<Setup> CreateAsync(bool withSchema = true, string projectName = "Alpha", params (string RelativeFile, string Text)[] files)
    {
        var game = new TestGame(withSchema);
        game.EnsureTableVersions();
        var workspace = game.NewController();
        await workspace.LoadAsync();
        var projects = game.NewProjects(workspace);
        var created = projects.Create(game.NewFolder("mod"), projectName, "mod.pack");
        Assert.True(created.Ok, created.Message);
        var project = projects.Projects[^1];
        WriteFiles(project, files);

        var tabs = new TabsViewModel(game.Texts);
        var history = new ActionHistory();
        var documents = new DocumentsController(tabs, projects, history, game.Runner, new FakeEditDialogs(), game.Log, game.Texts);
        var files2 = new ProjectFilesController(projects, workspace, documents, history, game.Runner, game.Root.File("trash"), game.Log, game.Texts, TimeProvider.System);
        var dialogs = new FakeFileDialogs();
        var viewModel = new ProjectFilesViewModel(files2, projects, dialogs, game.Texts);
        var opened = new List<(Guid, string)>();
        viewModel.OpenRequested += (_, request) => opened.Add(request);
        return new Setup(game, workspace, projects, tabs, history, documents, files2, dialogs, viewModel, project, opened);
    }

    private static Task<Setup> CreateAsync(params (string RelativeFile, string Text)[] files) => CreateAsync(true, "Alpha", files);

    private static void WriteFiles(ProjectState project, params (string RelativeFile, string Text)[] files)
    {
        foreach (var (relative, text) in files)
        {
            var path = Path.Combine(project.Folder, "pack", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }

    private static string TableText(string packPath, int value) => TableTsv.Write("x_tables", 0, packPath, Def, [[value]]).Text!;

    private static (string, string) Table(string relativeFile, int value) =>
        (relativeFile, TableText(relativeFile.Replace('\\', '/').Replace(".tsv", string.Empty), value));

    // A pack with the given entries in the data folder, listed by the workspace; the runner titles are cleared afterwards.
    private static async Task<PackSource> AddPackAsync(Setup setup, params (string Path, byte[] Bytes)[] entries)
    {
        setup.Game.AddPack("a.pack", entries);
        await setup.Workspace.RefreshAsync();
        setup.Game.Runner.Titles.Clear();
        return setup.Workspace.Catalog!.Packs.Single(pack => pack.Name == "a.pack");
    }

    // ---- new table and new loc file ----------------------------------------------------------------------------------------------

    [Fact] public async Task New_table_asks_creates_opens_and_reports()
    {
        using var setup = await CreateAsync();
        var raised = new List<Guid>();
        setup.Files.FilesChanged += (_, id) => raised.Add(id);
        setup.Dialogs.TableAnswers.Enqueue(new NewTableInput("x_tables", "mine"));

        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);

        // The question: the creatable tables of the schema, the name of the project as the suggestion.
        var ask = Assert.Single(setup.Dialogs.TableAsks);
        Assert.Equal(["x_tables"], ask.Tables);
        Assert.Equal("alpha", ask.Suggestion);

        // The file exists, is one undo step, is announced to the explorer and opened, and the status line says so.
        Assert.True(setup.Exists(@"db\x_tables\mine.tsv"));
        Assert.Equal([setup.Id], raised);
        Assert.Equal([(setup.Id, @"db\x_tables\mine.tsv")], setup.Opened);
        Assert.Equal(setup.Texts.FileCreatedStatus(@"db\x_tables\mine.tsv"), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.True(setup.History.CanUndo);

        // The check of the dialog looks in the folder of the chosen table and for the text form.
        Assert.Null(ask.Validate(new NewTableInput("x_tables", "other")));
        Assert.Equal(setup.Texts.FileProblemTargetExists, ask.Validate(new NewTableInput("x_tables", "mine")));
        Assert.Equal(setup.Texts.FileNameDot, ask.Validate(new NewTableInput("x_tables", "a.b")));
    }

    [Fact] public async Task New_loc_asks_for_a_name_creates_the_file_in_text_db_and_opens_it()
    {
        using var setup = await CreateAsync();
        setup.Dialogs.NameAnswers.Enqueue("mine");

        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);

        var ask = Assert.Single(setup.Dialogs.NameAsks);
        Assert.Equal((setup.Texts.FileNewLocTitle, setup.Texts.DialogName, "alpha"), (ask.Title, ask.Label, ask.Suggestion));
        Assert.True(setup.Exists(@"text\db\mine.loc.tsv"));
        Assert.Equal([(setup.Id, @"text\db\mine.loc.tsv")], setup.Opened);
        Assert.Equal(setup.Texts.FileCreatedStatus(@"text\db\mine.loc.tsv"), setup.ViewModel.StatusMessage);
        Assert.Null(ask.Validate("other"));
        Assert.Equal(setup.Texts.FileProblemTargetExists, ask.Validate("mine"));
        Assert.Equal(setup.Texts.FileProblemTargetExists, ask.Validate("MINE"));
    }

    [Fact] public async Task The_suggested_name_comes_from_the_project_name_and_avoids_taken_names()
    {
        // "My Mod!": lower case, every character outside a-z 0-9 _ becomes _, so the name ends in an underscore.
        using var setup = await CreateAsync(true, "My Mod!");
        setup.Dialogs.TableAnswers.Enqueue(new NewTableInput("x_tables", "my_mod_"));
        setup.Dialogs.TableAnswers.Enqueue(new NewTableInput("x_tables", "my_mod__2"));

        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);

        Assert.Equal(["my_mod_", "my_mod__2", "my_mod__3"], setup.Dialogs.TableAsks.Select(ask => ask.Suggestion));
        Assert.Single(setup.Game.Runner.Titles, setup.Texts.OperationPrepareTables);          // the table list was prepared once for the three dialogs
        Assert.True(setup.Exists(@"db\x_tables\my_mod_.tsv"));
        Assert.True(setup.Exists(@"db\x_tables\my_mod__2.tsv"));

        // The loc file asks the same, in its own folder: the table names do not count there.
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);

        Assert.Equal("my_mod_", setup.Dialogs.NameAsks[0].Suggestion);
    }

    [Fact] public async Task A_project_name_that_gives_no_usable_name_falls_back_to_new_and_a_long_one_too()
    {
        using var tooLong = await CreateAsync(true, new string('A', 120));
        using var umlauts = await CreateAsync(true, "Über Mod");

        await tooLong.ViewModel.NewLocAsync(tooLong.Id).WaitAsync(Timeout);
        await umlauts.ViewModel.NewLocAsync(umlauts.Id).WaitAsync(Timeout);

        Assert.Equal("new", tooLong.Dialogs.NameAsks[0].Suggestion);                        // 120 characters are refused: "new"
        Assert.Equal("_ber_mod", umlauts.Dialogs.NameAsks[0].Suggestion);                   // not a-z: the letter becomes _, the rest stays

        // "new" is taken as well: the number is counted up from there.
        WriteFiles(tooLong.Project, (@"text\db\new.loc.tsv", "x"));
        await tooLong.ViewModel.NewLocAsync(tooLong.Id).WaitAsync(Timeout);

        Assert.Equal("new_2", tooLong.Dialogs.NameAsks[1].Suggestion);
    }

    [Fact] public async Task A_cancelled_dialog_does_nothing()
    {
        using var setup = await CreateAsync(Table(TableA, 1));
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes));
        var before = setup.Snapshot();
        var raised = 0;
        setup.Files.FilesChanged += (_, _) => raised++;

        // No answer is prepared, so the user cancels every question.
        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);
        await setup.ViewModel.RenameAsync(setup.Id, TableA).WaitAsync(Timeout);

        Assert.Equal((1, 3), (setup.Dialogs.TableAsks.Count, setup.Dialogs.NameAsks.Count));   // each command did ask
        Assert.Equal(before, setup.Snapshot());
        Assert.Equal(0, raised);
        Assert.Empty(setup.Opened);
        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.Confirms);
        Assert.Equal([setup.Texts.OperationPrepareTables], setup.Game.Runner.Titles);      // the table list was prepared; no file run was started
        Assert.False(setup.History.CanUndo);
    }

    [Fact] public async Task A_refused_new_table_shows_the_message_and_reports_nothing()
    {
        using var setup = await CreateAsync(Table(@"db\x_tables\mine.tsv", 1));
        setup.Dialogs.TableAnswers.Enqueue(new NewTableInput("x_tables", "mine"));              // the dialog would have refused this
        var before = setup.Snapshot();

        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.StartsWith(setup.Texts.FileProblemTargetExists, message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Opened);
    }

    [Fact] public async Task Without_a_schema_the_table_dialog_opens_with_an_empty_list_and_a_loc_file_needs_no_schema()
    {
        using var setup = await CreateAsync(withSchema: false);

        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);

        // No creatable table is no reason to refuse the command: the dialog says it.
        Assert.Null(setup.ViewModel.NewFileDisabledReason(setup.Id));
        Assert.Empty(Assert.Single(setup.Dialogs.TableAsks).Tables);
        Assert.Empty(setup.Dialogs.Messages);

        setup.Dialogs.NameAnswers.Enqueue("mine");
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);

        Assert.True(setup.Exists(@"text\db\mine.loc.tsv"));
    }

    // ---- adding a pack entry to the project --------------------------------------------------------------------------------------

    [Fact] public async Task Adopt_of_a_table_asks_for_a_name_and_other_files_are_adopted_without_a_question()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes), ("script/campaign/main.lua", "print(1)"u8.ToArray()));
        setup.Dialogs.NameAnswers.Enqueue("mine");

        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);

        var ask = Assert.Single(setup.Dialogs.NameAsks);
        Assert.Equal((setup.Texts.FileAddTitle, setup.Texts.DialogName, "alpha"), (ask.Title, ask.Label, ask.Suggestion));
        Assert.Equal(TableText("db/x_tables/mine", 42), File.ReadAllText(setup.PathOf(@"db\x_tables\mine.tsv")));
        Assert.Equal([(setup.Id, @"db\x_tables\mine.tsv")], setup.Opened);
        Assert.Equal(setup.Texts.FileAddedStatus(@"db\x_tables\mine.tsv"), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Confirms);                                               // another name: nothing to confirm

        // The check looks in the folder of the table of the entry.
        Assert.Null(ask.Validate("other"));
        Assert.Equal(setup.Texts.FileProblemTargetExists, ask.Validate("mine"));

        // A script is added as it is: no question at all, the status is the new one.
        await setup.ViewModel.AdoptAsync(pack, "script/campaign/main.lua").WaitAsync(Timeout);

        Assert.Single(setup.Dialogs.NameAsks);
        Assert.Equal("print(1)"u8.ToArray(), File.ReadAllBytes(setup.PathOf(@"script\campaign\main.lua")));
        Assert.Equal([(setup.Id, @"db\x_tables\mine.tsv"), (setup.Id, @"script\campaign\main.lua")], setup.Opened);
        Assert.Equal(setup.Texts.FileAddedStatus(@"script\campaign\main.lua"), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);
    }

    [Fact] public async Task Adopt_goes_into_the_active_project()
    {
        using var setup = await CreateAsync();
        var second = setup.Game.NewProject(setup.Projects, "Beta");
        Assert.True(setup.Projects.SetActive(second.Id).Ok);
        var pack = await AddPackAsync(setup, ("script/main.lua", "print(1)"u8.ToArray()));

        await setup.ViewModel.AdoptAsync(pack, "script/main.lua").WaitAsync(Timeout);

        Assert.True(File.Exists(Path.Combine(second.Folder, "pack", "script", "main.lua")));
        Assert.False(setup.Exists(@"script\main.lua"));
        Assert.Equal([(second.Id, @"script\main.lua")], setup.Opened);
    }

    [Fact] public async Task Adopt_under_the_same_name_needs_the_confirmation_and_no_returns_to_the_name_question()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes), ("db/x_tables/more__", TestGame.TableBytes));

        // The same name, "no": the question comes again with the last input as the suggestion; the second answer is another name.
        setup.Dialogs.NameAnswers.Enqueue("data__");
        setup.Dialogs.NameAnswers.Enqueue("mine");
        setup.Dialogs.ConfirmAnswers.Enqueue(false);

        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);

        Assert.Equal(["alpha", "data__"], setup.Dialogs.NameAsks.Select(ask => ask.Suggestion));
        Assert.Equal(["db/x_tables/data__"], setup.Dialogs.Confirms);                       // the confirmation names the entry of the pack
        Assert.True(setup.Exists(@"db\x_tables\mine.tsv"));
        Assert.False(setup.Exists(@"db\x_tables\data__.tsv"));

        // The same name, "yes": the file is added under that name, after one question and one confirmation.
        setup.Dialogs.NameAnswers.Enqueue("more__");
        setup.Dialogs.ConfirmAnswers.Enqueue(true);

        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/more__").WaitAsync(Timeout);

        Assert.Equal(3, setup.Dialogs.NameAsks.Count);
        Assert.Equal(["db/x_tables/data__", "db/x_tables/more__"], setup.Dialogs.Confirms);
        Assert.True(setup.Exists(@"db\x_tables\more__.tsv"));
        Assert.Equal(setup.Texts.FileAddedStatus(@"db\x_tables\more__.tsv"), setup.ViewModel.StatusMessage);

        // Cancelling the name question after a "no" ends the command without a file.
        var before = setup.Snapshot();
        setup.Dialogs.NameAnswers.Enqueue("data__");
        setup.Dialogs.ConfirmAnswers.Enqueue(false);

        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);

        Assert.Equal(before, setup.Snapshot());
        Assert.Equal(setup.Texts.FileAddedStatus(@"db\x_tables\more__.tsv"), setup.ViewModel.StatusMessage);   // a cancelled command leaves the last line
    }

    [Fact] public async Task Adopt_into_an_existing_target_shows_a_message()
    {
        using var setup = await CreateAsync(Table(@"db\x_tables\mine.tsv", 1));
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes));
        setup.Dialogs.NameAnswers.Enqueue("mine");
        var before = setup.Snapshot();

        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);

        Assert.Equal(setup.Texts.FileProblemTargetExists, Assert.Single(setup.Dialogs.NameAsks).Validate("mine"));   // the dialog would have said so
        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.StartsWith(setup.Texts.FileProblemTargetExists, message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
        Assert.Empty(setup.Opened);
        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.False(setup.History.CanUndo);
    }

    [Fact] public async Task A_binary_table_reports_its_reason_in_the_status()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", [1, 2, 3]));            // not a table of the schema: it stays binary
        setup.Dialogs.NameAnswers.Enqueue("mine");

        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(setup.PathOf(@"db\x_tables\mine")));
        Assert.Equal([(setup.Id, @"db\x_tables\mine")], setup.Opened);
        var lines = setup.ViewModel.StatusMessage!.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal(setup.Texts.FileAddedStatus(@"db\x_tables\mine"), lines[0]);
        Assert.StartsWith(setup.Texts.FileAddedBinary(string.Empty), lines[1], StringComparison.Ordinal);
        Assert.True(lines[1].Length > setup.Texts.FileAddedBinary(string.Empty).Length);       // and the reason follows
        Assert.Empty(setup.Dialogs.Messages);
    }

    [Fact] public async Task A_pack_entry_that_cannot_be_read_shows_the_message()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("a.txt", [1]));
        var before = setup.Snapshot();

        await setup.ViewModel.AdoptAsync(pack, "gone.txt").WaitAsync(Timeout);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.StartsWith(setup.Texts.EntryProblemCorrupt, message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
        Assert.Empty(setup.Dialogs.NameAsks);                                               // "gone.txt" keeps its name: no question
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    // ---- rename and delete -------------------------------------------------------------------------------------------------------

    [Fact] public async Task Delete_does_not_ask_and_reports_how_to_get_the_file_back()
    {
        using var setup = await CreateAsync((@"script\a.txt", "one"));
        var raised = new List<Guid>();
        setup.Files.FilesChanged += (_, id) => raised.Add(id);

        await setup.ViewModel.DeleteAsync(setup.Id, @"script\a.txt").WaitAsync(Timeout);

        Assert.False(setup.Exists(@"script\a.txt"));
        Assert.Equal(setup.Texts.FileDeletedStatus(@"script\a.txt"), setup.ViewModel.StatusMessage);
        Assert.Equal([setup.Id], raised);
        Assert.Empty(setup.Dialogs.NameAsks);
        Assert.Empty(setup.Dialogs.TableAsks);
        Assert.Empty(setup.Dialogs.Confirms);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Opened);

        // The text does not promise what is not true: Undo does bring the file back.
        setup.Documents.UndoCommand.Execute(null);

        Assert.Equal("one", File.ReadAllText(setup.PathOf(@"script\a.txt")));
    }

    [Fact] public async Task A_delete_that_is_refused_shows_the_message_and_keeps_the_file()
    {
        using var setup = await CreateAsync(("script\\a.txt", "one"));

        await setup.ViewModel.DeleteAsync(setup.Id, @"script\missing.txt").WaitAsync(Timeout);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.False(string.IsNullOrEmpty(message));
        Assert.True(setup.Exists(@"script\a.txt"));
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task Rename_suggests_the_current_name_and_reports_a_refusal()
    {
        using var setup = await CreateAsync(Table(TableA, 1), Table(TableB, 2));
        setup.Dialogs.NameAnswers.Enqueue("c");

        await setup.ViewModel.RenameAsync(setup.Id, TableA).WaitAsync(Timeout);

        // The question starts with the name the file has, and its check knows the file that is renamed.
        var ask = Assert.Single(setup.Dialogs.NameAsks);
        Assert.Equal((setup.Texts.FileRenameTitle, setup.Texts.DialogName, "a"), (ask.Title, ask.Label, ask.Suggestion));
        Assert.Equal(setup.Texts.FileNameUnchanged, ask.Validate("a"));
        Assert.Equal(setup.Texts.FileProblemTargetExists, ask.Validate("b"));
        Assert.Null(ask.Validate("A"));                                                     // a change of case only is a rename
        Assert.Equal(setup.Texts.FileNameDot, ask.Validate("a.b"));

        // The file is renamed; it was not open, so nothing is opened, and the status line says what happened.
        Assert.False(setup.Exists(TableA));
        Assert.True(setup.Exists(@"db\x_tables\c.tsv"));
        Assert.Equal(setup.Texts.FileRenamedStatus(TableA, @"db\x_tables\c.tsv"), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Opened);
        Assert.Empty(setup.Dialogs.Messages);

        // A name the controller refuses (the dialog would not have let it through) is shown as a message and changes nothing.
        setup.Dialogs.NameAnswers.Enqueue("b");
        var before = setup.Snapshot();

        await setup.ViewModel.RenameAsync(setup.Id, @"db\x_tables\c.tsv").WaitAsync(Timeout);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.StartsWith(setup.Texts.FileProblemTargetExists, message, StringComparison.Ordinal);
        Assert.Equal(before, setup.Snapshot());
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task A_renamed_file_that_was_open_is_opened_again_once_through_the_controller()
    {
        using var setup = await CreateAsync(Table(TableA, 1));
        var opened = await setup.Projects.OpenFileAsync(setup.Id, TableA);
        Assert.NotNull(opened);
        setup.Tabs.Add(TabSource.ForProject(setup.Id, setup.Project.DisplayName), opened, setup.Workspace.Schema);
        setup.Game.Runner.Titles.Clear();
        setup.Dialogs.NameAnswers.Enqueue("c");

        await setup.ViewModel.RenameAsync(setup.Id, TableA).WaitAsync(Timeout);

        // The controller asks to open it under the new name and the view model passes that on; it adds no request of its own.
        Assert.Equal([(setup.Id, @"db\x_tables\c.tsv")], setup.Opened);
        Assert.Equal(setup.Texts.FileRenamedStatus(TableA, @"db\x_tables\c.tsv"), setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task Rename_keeps_the_extension_of_every_kind_of_file()
    {
        using var setup = await CreateAsync(("text\\db\\l.loc.tsv", "x"), ("script\\a.txt", "x"), ("script\\.gitkeep", "x"), ("script\\n.tar.gz", "x"));

        await setup.ViewModel.RenameAsync(setup.Id, @"text\db\l.loc.tsv").WaitAsync(Timeout);
        await setup.ViewModel.RenameAsync(setup.Id, @"script\a.txt").WaitAsync(Timeout);
        await setup.ViewModel.RenameAsync(setup.Id, @"script\.gitkeep").WaitAsync(Timeout);
        await setup.ViewModel.RenameAsync(setup.Id, @"script\n.tar.gz").WaitAsync(Timeout);

        // The editable part is the file name without its extension; the name that gives back the same file is "unchanged".
        Assert.Equal(["l", "a", ".gitkeep", "n.tar"], setup.Dialogs.NameAsks.Select(ask => ask.Suggestion));
        Assert.All(setup.Dialogs.NameAsks, ask => Assert.Equal(setup.Texts.FileNameUnchanged, ask.Validate(ask.Suggestion)));
    }

    // ---- when a command is not available -----------------------------------------------------------------------------------------

    [Fact] public async Task Disabled_reasons_name_no_active_project_an_unusable_project_and_busy()
    {
        using var setup = await CreateAsync(("script\\a.txt", "one"));
        var pack = await AddPackAsync(setup, ("script/main.lua", "print(1)"u8.ToArray()));

        // Everything is possible.
        Assert.Null(setup.ViewModel.NewFileDisabledReason(setup.Id));
        Assert.Null(setup.ViewModel.AdoptDisabledReason);
        Assert.Null(setup.ViewModel.FileActionDisabledReason(setup.Id, @"script\a.txt"));

        // A project that is not in the list.
        var unknown = Guid.NewGuid();
        Assert.Equal(setup.Texts.ProjectUnknown, setup.ViewModel.NewFileDisabledReason(unknown));
        Assert.Equal(setup.Texts.ProjectUnknown, setup.ViewModel.FileActionDisabledReason(unknown, @"script\a.txt"));

        // Another operation runs: the reason is the same text everywhere, and no command asks anything or starts anything.
        setup.Game.Runner.IsRunning = true;
        Assert.Equal(setup.Texts.ProjectBusy, setup.ViewModel.NewFileDisabledReason(setup.Id));
        Assert.Equal(setup.Texts.ProjectBusy, setup.ViewModel.AdoptDisabledReason);
        Assert.Equal(setup.Texts.ProjectBusy, setup.ViewModel.FileActionDisabledReason(setup.Id, @"script\a.txt"));
        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.AdoptAsync(pack, "script/main.lua").WaitAsync(Timeout);
        await setup.ViewModel.RenameAsync(setup.Id, @"script\a.txt").WaitAsync(Timeout);
        await setup.ViewModel.DeleteAsync(setup.Id, @"script\a.txt").WaitAsync(Timeout);
        setup.Game.Runner.IsRunning = false;
        setup.AssertSilent();
        Assert.True(setup.Exists(@"script\a.txt"));

        // The project file is gone: the project cannot be used, and its problem is the reason.
        File.Delete(Path.Combine(setup.Project.Folder, ProjectFile.FileName));
        setup.Projects.Reload();
        Assert.StartsWith(setup.Texts.ProjectProblemNotFound, setup.ViewModel.NewFileDisabledReason(setup.Id), StringComparison.Ordinal);
        Assert.StartsWith(setup.Texts.ProjectProblemNotFound, setup.ViewModel.AdoptDisabledReason, StringComparison.Ordinal);
        Assert.StartsWith(setup.Texts.ProjectProblemNotFound, setup.ViewModel.FileActionDisabledReason(setup.Id, @"script\a.txt"), StringComparison.Ordinal);

        // A command that is started anyway says why instead of asking for a name it cannot use.
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.AdoptAsync(pack, "script/main.lua").WaitAsync(Timeout);

        Assert.Empty(setup.Dialogs.NameAsks);
        Assert.Equal(2, setup.Dialogs.Messages.Count);
        Assert.All(setup.Dialogs.Messages, message => Assert.StartsWith(setup.Texts.ProjectProblemNotFound, message.Message, StringComparison.Ordinal));

        // No project is active (the list is empty): adding says why instead of doing nothing, and asks nothing.
        setup.Dialogs.Messages.Clear();
        Assert.True(setup.Projects.Remove(setup.Id).Ok);
        Assert.Equal(setup.Texts.ProjectNoneActive, setup.ViewModel.AdoptDisabledReason);
        await setup.ViewModel.AdoptAsync(pack, "script/main.lua").WaitAsync(Timeout);

        Assert.Empty(setup.Dialogs.NameAsks);
        Assert.Equal([(setup.Texts.ProjectMessageTitle, setup.Texts.ProjectNoneActive)], setup.Dialogs.Messages);
        Assert.Empty(setup.Opened);
    }

    [Fact] public async Task A_file_with_unsaved_work_in_a_tab_is_refused_before_any_dialog()
    {
        using var setup = await CreateAsync(Table(TableA, 42), Table(TableB, 7));
        var opened = await setup.Projects.OpenFileAsync(setup.Id, TableA);
        Assert.NotNull(opened);
        setup.Tabs.Add(TabSource.ForProject(setup.Id, setup.Project.DisplayName), opened, setup.Workspace.Schema);
        var tab = Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.Tabs[^1]);
        var reason = setup.Texts.FileTabUnsaved(TableA.Replace('\\', '/'));

        // Open with nothing to lose: possible.
        Assert.Null(setup.ViewModel.FileActionDisabledReason(setup.Id, TableA));

        tab.Rows[0][0] = "50";

        Assert.Equal(reason, setup.ViewModel.FileActionDisabledReason(setup.Id, TableA));
        Assert.Null(setup.ViewModel.FileActionDisabledReason(setup.Id, TableB));               // another file, no tab
        Assert.Equal(setup.Texts.ProjectBusy, WithBusy(setup, () => setup.ViewModel.FileActionDisabledReason(setup.Id, TableA)));   // busy comes first
        setup.Game.Runner.Titles.Clear();
        setup.Dialogs.NameAnswers.Enqueue("c");

        await setup.ViewModel.RenameAsync(setup.Id, TableA).WaitAsync(Timeout);
        await setup.ViewModel.DeleteAsync(setup.Id, TableA).WaitAsync(Timeout);

        // Both say why and neither asked for a name that could not be used.
        Assert.Equal([(setup.Texts.ProjectMessageTitle, reason), (setup.Texts.ProjectMessageTitle, reason)], setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.NameAsks);
        Assert.True(setup.Exists(TableA));
        Assert.Empty(setup.Game.Runner.Titles);
        Assert.Null(setup.ViewModel.StatusMessage);

        // The other file is renamed as usual.
        await setup.ViewModel.RenameAsync(setup.Id, TableB).WaitAsync(Timeout);

        Assert.Single(setup.Dialogs.NameAsks);
    }

    private static T WithBusy<T>(Setup setup, Func<T> read)
    {
        setup.Game.Runner.IsRunning = true;
        try
        {
            return read();
        }
        finally
        {
            setup.Game.Runner.IsRunning = false;
        }
    }

    [Fact] public async Task A_command_that_turns_busy_while_its_dialog_is_open_says_so_instead_of_dropping_the_input()
    {
        using var setup = await CreateAsync(Table(TableA, 1));
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes));
        setup.Dialogs.WhileAsking = () => setup.Game.Runner.IsRunning = true;                    // another operation starts while the user types
        setup.Dialogs.NameAnswers.Enqueue("mine");
        setup.Dialogs.NameAnswers.Enqueue("mine");
        setup.Dialogs.NameAnswers.Enqueue("mine");
        setup.Dialogs.TableAnswers.Enqueue(new NewTableInput("x_tables", "mine"));
        setup.Game.Runner.Titles.Clear();
        var busy = (setup.Texts.ProjectMessageTitle, setup.Texts.ProjectBusy);

        // The table list is prepared before the dialog; the dialog is the first thing that can be overtaken.
        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);
        setup.Game.Runner.IsRunning = false;
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);
        setup.Game.Runner.IsRunning = false;
        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);
        setup.Game.Runner.IsRunning = false;
        await setup.ViewModel.RenameAsync(setup.Id, TableA).WaitAsync(Timeout);
        setup.Game.Runner.IsRunning = false;

        Assert.Equal([busy, busy, busy, busy], setup.Dialogs.Messages);
        Assert.False(setup.Exists(@"db\x_tables\mine.tsv"));
        Assert.False(setup.Exists(@"text\db\mine.loc.tsv"));
        Assert.True(setup.Exists(TableA));
        Assert.Empty(setup.Opened);
        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.False(setup.History.CanUndo);
    }

    [Fact] public async Task The_status_stays_until_a_command_really_starts()
    {
        using var setup = await CreateAsync((@"script\a.txt", "one"));
        var changed = new List<string?>(); setup.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await setup.ViewModel.DeleteAsync(setup.Id, @"script\a.txt").WaitAsync(Timeout);
        var deleted = setup.ViewModel.StatusMessage;

        Assert.Equal(setup.Texts.FileDeletedStatus(@"script\a.txt"), deleted);
        Assert.Contains(nameof(ProjectFilesViewModel.StatusMessage), changed);

        // A command the user cancels, and one that cannot start, leave the last result line alone.
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.RenameAsync(setup.Id, @"script\missing.txt").WaitAsync(Timeout);   // cancelled in the dialog
        setup.Game.Runner.IsRunning = true;
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);                          // another operation runs: cannot start
        setup.Game.Runner.IsRunning = false;

        Assert.Equal(deleted, setup.ViewModel.StatusMessage);

        // One that starts replaces it, also when the action ends with nothing to say (here a message).
        setup.Dialogs.NameAnswers.Enqueue("mine");
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);

        Assert.Equal(setup.Texts.FileCreatedStatus(@"text\db\mine.loc.tsv"), setup.ViewModel.StatusMessage);

        setup.Dialogs.NameAnswers.Enqueue("mine");                                              // taken: the action is refused after the dialog
        await setup.ViewModel.NewLocAsync(setup.Id).WaitAsync(Timeout);

        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.Single(setup.Dialogs.Messages);
    }

    [Fact] public async Task The_table_list_is_prepared_in_the_background_once_per_schema_and_a_cancel_shows_no_dialog()
    {
        using var setup = await CreateAsync();
        var title = setup.Texts.OperationPrepareTables;

        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);
        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);

        // Two dialogs, one preparation, and it ran before the first dialog opened.
        Assert.Equal(2, setup.Dialogs.TableAsks.Count);
        Assert.Equal([title], setup.Game.Runner.Titles);
        Assert.Equal(["x_tables"], setup.Dialogs.TableAsks[1].Tables);

        // Another schema instance: prepared again.
        await setup.Workspace.ReloadSchemaAsync();
        setup.Game.Runner.Titles.Clear();
        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);

        Assert.Equal(3, setup.Dialogs.TableAsks.Count);
        Assert.Equal([title], setup.Game.Runner.Titles);

        // The user cancels the preparation: no dialog, no message, and the status stays.
        await setup.Workspace.ReloadSchemaAsync();
        setup.Game.Runner.CancelNext = true;
        setup.Game.Runner.Titles.Clear();
        await setup.ViewModel.NewTableAsync(setup.Id).WaitAsync(Timeout);

        Assert.Equal(3, setup.Dialogs.TableAsks.Count);
        Assert.Equal([title], setup.Game.Runner.Titles);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    // ---- adding a pack entry: the same name, and both forms of a name -----------------------------------------------------------

    [Fact] public async Task The_same_name_is_confirmed_ignoring_case_because_pack_paths_are_case_insensitive()
    {
        using var setup = await CreateAsync();
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes), ("text/db/names.loc", [1, 2, 3]));

        // "DATA__" for "data__": the game takes both for the same file, so the whole game table is replaced: ask.
        setup.Dialogs.NameAnswers.Enqueue("DATA__");
        setup.Dialogs.ConfirmAnswers.Enqueue(false);
        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);

        Assert.Equal(["db/x_tables/data__"], setup.Dialogs.Confirms);
        Assert.Equal(["alpha", "DATA__"], setup.Dialogs.NameAsks.Select(ask => ask.Suggestion));
        Assert.False(setup.Exists(@"db\x_tables\DATA__.tsv"));

        // The same holds for a loc file.
        setup.Dialogs.NameAnswers.Enqueue("NAMES");
        setup.Dialogs.ConfirmAnswers.Enqueue(false);
        await setup.ViewModel.AdoptAsync(pack, "text/db/names.loc").WaitAsync(Timeout);

        Assert.Equal(["db/x_tables/data__", "text/db/names.loc"], setup.Dialogs.Confirms);

        // Yes adds it under the spelling that was typed.
        setup.Dialogs.NameAnswers.Enqueue("DATA__");
        setup.Dialogs.ConfirmAnswers.Enqueue(true);
        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);

        Assert.True(setup.Exists(@"db\x_tables\DATA__.tsv"));
    }

    [Fact] public async Task The_name_check_of_an_added_table_or_loc_file_refuses_a_name_that_is_taken_in_either_form()
    {
        using var setup = await CreateAsync((@"db\x_tables\rawone", "x"), (@"db\x_tables\textone.tsv", "x"), (@"text\db\rawloc.loc", "x"), (@"text\db\textloc.loc.tsv", "x"));
        var pack = await AddPackAsync(setup, ("db/x_tables/data__", TestGame.TableBytes), ("text/db/names.loc", [1, 2, 3]));

        await setup.ViewModel.AdoptAsync(pack, "db/x_tables/data__").WaitAsync(Timeout);            // cancelled: only the check is of interest
        await setup.ViewModel.AdoptAsync(pack, "text/db/names.loc").WaitAsync(Timeout);
        var (table, loc) = (setup.Dialogs.NameAsks[0].Validate, setup.Dialogs.NameAsks[1].Validate);

        // The file can become text or stay as it is: a name is free only when both are.
        Assert.Equal(setup.Texts.FileProblemTargetExists, table("rawone"));
        Assert.Equal(setup.Texts.FileProblemTargetExists, table("textone"));
        Assert.Equal(setup.Texts.FileProblemTargetExists, table("RAWONE"));
        Assert.Null(table("free"));
        Assert.Equal(setup.Texts.FileProblemTargetExists, loc("rawloc"));
        Assert.Equal(setup.Texts.FileProblemTargetExists, loc("textloc"));
        Assert.Null(loc("free"));
    }
}

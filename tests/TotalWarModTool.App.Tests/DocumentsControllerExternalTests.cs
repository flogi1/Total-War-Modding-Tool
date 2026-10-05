using TotalWarModTool.Editing;
using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class DocumentsControllerExternalTests
{
    private const int Value = 0;
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);
    private const string FileA = @"db\x_tables\a.tsv";
    private const string FileB = @"db\x_tables\b.tsv";
    private const string FileC = @"db\x_tables\c.tsv";
    private const string Binary = @"db\x_tables\data__";

    private static readonly TableDefinition Def = new(0, [new FieldDefinition("value", FieldType.I32, nameof(FieldType.I32), false, null)]);

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, ProjectsController Projects, TabsViewModel Tabs, ActionHistory History,
        FakeEditDialogs Dialogs, DocumentsController Controller, ProjectState Project) : IDisposable
    {
        public void Dispose() => Game.Dispose();

        public TabSource Source => TabSource.ForProject(Project.Id, Project.DisplayName);

        public string PathOf(string relativeFile) => Path.Combine(Project.Folder, "pack", relativeFile);

        public string Read(string relativeFile) => File.ReadAllText(PathOf(relativeFile));

        // "Another program writes the file": new content and an explicitly later write time, so that a same-length change is found by the time
        // alone and the test does not depend on the resolution of the clock.
        public void ChangeOutside(string relativeFile, string text)
        {
            var path = PathOf(relativeFile);
            var before = File.GetLastWriteTimeUtc(path);
            File.WriteAllText(path, text);
            File.SetLastWriteTimeUtc(path, before.AddMinutes(5));
        }

        public void DeleteOutside(string relativeFile) => File.Delete(PathOf(relativeFile));

        public void WriteBytes(string relativeFile, byte[] bytes)
        {
            var path = PathOf(relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }

    private static Task<Setup> CreateAsync(params (string RelativeFile, string Text)[] files) => CreateWithRunnerAsync(null, files);

    // The projects (which read the files) get their own runner when one is given; the documents controller keeps the fake of the game.
    private static async Task<Setup> CreateWithRunnerAsync(IOperationRunner? projectsRunner, params (string RelativeFile, string Text)[] files)
    {
        var game = new TestGame();
        var workspace = game.NewController();
        await workspace.LoadAsync();
        var projects = game.NewProjects(workspace, projectsRunner);
        var project = game.NewProject(projects, "Alpha", files);
        var tabs = new TabsViewModel(game.Texts);
        var history = new ActionHistory();
        var dialogs = new FakeEditDialogs();
        var controller = new DocumentsController(tabs, projects, history, game.Runner, dialogs, game.Log, game.Texts);
        return new Setup(game, workspace, projects, tabs, history, dialogs, controller, project);
    }

    private static string TableText(string packPath, int value) => TableTsv.Write("x_tables", 0, packPath, Def, [[value]]).Text!;

    private static (string, string) Table(string relativeFile, int value) =>
        (relativeFile, TableText(relativeFile.Replace('\\', '/').Replace(".tsv", string.Empty), value));

    private static string TextOf(string relativeFile, int value) => TableText(relativeFile.Replace('\\', '/').Replace(".tsv", string.Empty), value);

    private static async Task<OpenedEntry> ReadAsync(Setup setup, string relativeFile)
    {
        var opened = await setup.Projects.OpenFileAsync(setup.Project.Id, relativeFile);
        Assert.NotNull(opened);
        return opened;
    }

    private static async Task<TabViewModel> OpenTabAsync(Setup setup, string relativeFile)
    {
        setup.Tabs.Add(setup.Source, await ReadAsync(setup, relativeFile), setup.Workspace.Schema);
        return setup.Tabs.Tabs[^1];
    }

    private static async Task<EditableRowsTabViewModel> OpenAsync(Setup setup, string relativeFile) =>
        Assert.IsType<EditableRowsTabViewModel>(await OpenTabAsync(setup, relativeFile));

    // ---- the check -------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task An_outside_change_marks_an_unmodified_tab_stale_and_does_not_reload_it()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            Assert.False(a.IsStale);
            Assert.False(a.CanReload);

            setup.ChangeOutside(FileA, TextOf(FileA, 43));                      // the same length: only the write time differs
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.True(a.IsStale);
            Assert.True(a.IsStaleOutside);
            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);
            Assert.True(a.CanReload);
            Assert.True(a.ReloadCommand.CanExecute(null));
            Assert.Equal("42", a.Rows[0][Value]);                                // no automatic reload
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task A_change_of_the_length_is_found_too()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);

            setup.ChangeOutside(FileA, TextOf(FileA, 4242));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);
        }
    }

    [Fact] public async Task A_modified_tab_is_marked_too_but_does_not_offer_the_reload()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            a.Rows[0][Value] = "50";

            setup.ChangeOutside(FileA, TextOf(FileA, 43));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);
            Assert.False(a.CanReload);
            Assert.False(a.ReloadCommand.CanExecute(null));
            Assert.Equal("50", a.Rows[0][Value]);
            Assert.True(a.IsModified);
        }
    }

    [Fact] public async Task An_outside_delete_marks_the_tab_stale_with_its_own_reason()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);

            setup.DeleteOutside(FileA);
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.True(a.IsStale);
            Assert.Equal(setup.Game.Texts.TabStaleDeleted, a.StaleText);
            Assert.True(a.CanReload);
        }
    }

    [Fact] public async Task A_deleted_file_that_is_written_again_changes_the_reason_to_changed()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            setup.DeleteOutside(FileA);
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.Equal(setup.Game.Texts.TabStaleDeleted, a.StaleText);

            File.WriteAllText(setup.PathOf(FileA), TextOf(FileA, 43));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);
        }
    }

    [Fact] public async Task An_unchanged_file_marks_nothing()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            Assert.NotNull(setup.Controller.KnownFingerprint(a));
            Assert.Equal(ProjectFileWriter.Fingerprint(setup.Project.Folder, FileA), setup.Controller.KnownFingerprint(a));

            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.False(a.IsStale);
            Assert.False(b.IsStale);
            Assert.Empty(setup.Game.Log.Warnings);
        }
    }

    [Fact] public async Task A_read_only_project_tab_is_marked_too_and_has_no_reload()
    {
        var setup = await CreateAsync();
        using (setup)
        {
            setup.WriteBytes(Binary, TestGame.TableBytes);
            var binary = Assert.IsType<TableTabViewModel>(await OpenTabAsync(setup, Binary));
            Assert.NotNull(setup.Controller.KnownFingerprint(binary));

            var path = setup.PathOf(Binary);
            File.WriteAllBytes(path, [.. TestGame.TableBytes, 0]);
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.True(binary.IsStale);
            Assert.Equal(setup.Game.Texts.TabStaleChanged, binary.StaleText);
        }
    }

    [Fact] public async Task A_pack_tab_is_never_looked_at()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var entry = new OpenedEntry("a.txt", EntryKind.Text, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, null,
                new TextContent("x", "UTF-8", false, 1));
            setup.Tabs.Add(new TabSource(@"D:\data\x.pack", "x.pack"), entry, null);
            var tab = setup.Tabs.Tabs[^1];
            Assert.Null(setup.Controller.KnownFingerprint(tab));

            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.False(tab.IsStale);
        }
    }

    [Fact] public async Task A_check_is_skipped_while_an_operation_runs()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            setup.ChangeOutside(FileA, TextOf(FileA, 43));
            setup.Game.Runner.IsRunning = true;

            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.False(a.IsStale);

            setup.Game.Runner.IsRunning = false;
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            Assert.True(a.IsStale);
        }
    }

    [Fact] public async Task Two_checks_at_once_do_not_disturb_each_other()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            await OpenAsync(setup, FileA);
            setup.ChangeOutside(FileA, TextOf(FileA, 43));

            var first = setup.Controller.CheckExternalChangesAsync();           // started, waits for the pool thread
            var second = setup.Controller.CheckExternalChangesAsync();          // skipped while the first one runs
            await Task.WhenAll(first, second).WaitAsync(Guard);

            // Both finished without a problem, the mark is there once, and a third check after them works again.
            Assert.Empty(setup.Game.Log.Warnings);
            Assert.Equal(setup.Game.Texts.TabStaleChanged, setup.Tabs.Tabs[0].StaleText);
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
        }
    }

    [Fact] public async Task A_file_that_changed_between_the_read_and_the_fingerprint_marks_the_tab_at_once()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var entry = await ReadAsync(setup, FileA);
            setup.ChangeOutside(FileA, TextOf(FileA, 4242));                    // another length than what was read

            setup.Tabs.Add(setup.Source, entry, setup.Workspace.Schema);

            var a = Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.Tabs[^1]);
            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);
            Assert.True(a.CanReload);
            Assert.Equal("42", a.Rows[0][Value]);

            // Nothing trustworthy is known, so a save asks first.
            a.Rows[0][Value] = "50";
            Assert.False(await setup.Controller.SaveAsync([a]));
            Assert.Equal(["db/x_tables/a.tsv"], setup.Dialogs.ConflictAsks);
        }
    }

    [Fact] public async Task A_file_that_is_gone_between_the_read_and_the_fingerprint_marks_the_tab_as_deleted()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var entry = await ReadAsync(setup, FileA);
            setup.DeleteOutside(FileA);

            setup.Tabs.Add(setup.Source, entry, setup.Workspace.Schema);

            Assert.Equal(setup.Game.Texts.TabStaleDeleted, setup.Tabs.Tabs[^1].StaleText);
        }
    }

    [Fact] public async Task A_tab_that_only_shows_a_size_is_not_compared_with_the_size_of_the_read()
    {
        var setup = await CreateAsync();
        using (setup)
        {
            setup.WriteBytes(@"textures\a.png", [1, 2, 3]);
            var hint = await OpenTabAsync(setup, @"textures\a.png");
            Assert.IsType<InfoTabViewModel>(hint);

            Assert.False(hint.IsStale);
            Assert.NotNull(setup.Controller.KnownFingerprint(hint));
        }
    }

    // ---- marks -----------------------------------------------------------------------------------------------------------------------

    [Fact] public void Deleted_wins_over_changed_both_win_over_the_schema_mark_and_clearing_them_leaves_the_schema_mark()
    {
        using var game = new TestGame();
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "P"), "text/db/a.loc.tsv", game.Texts, _ => { },
            RowsDocument.ForLoc("text/db/a.loc", [new LocRow("k", "T", false)]), new ActionHistory());
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.MarkSchemaStale();
        Assert.Equal((true, false, game.Texts.TabStaleSchema), (tab.IsStale, tab.IsStaleOutside, tab.StaleText));

        tab.SetOutsideChange(OutsideChange.Changed);
        Assert.Equal((true, true, game.Texts.TabStaleChanged), (tab.IsStale, tab.IsStaleOutside, tab.StaleText));
        Assert.Contains(nameof(EditableRowsTabViewModel.CanReload), raised);

        tab.SetOutsideChange(OutsideChange.Deleted);
        Assert.Equal(game.Texts.TabStaleDeleted, tab.StaleText);

        tab.SetOutsideChange(OutsideChange.Changed);                            // the file came back: the reason follows
        Assert.Equal(game.Texts.TabStaleChanged, tab.StaleText);

        tab.SetOutsideChange(OutsideChange.None);
        Assert.Equal((true, false, game.Texts.TabStaleSchema), (tab.IsStale, tab.IsStaleOutside, tab.StaleText));
        Assert.False(tab.CanReload);
    }

    [Fact] public void The_pack_mark_wins_over_the_schema_mark_in_either_order()
    {
        using var game = new TestGame();
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "P"), "text/db/a.loc.tsv", game.Texts, _ => { },
            RowsDocument.ForLoc("text/db/a.loc", [new LocRow("k", "T", false)]), new ActionHistory());

        tab.MarkSchemaStale();
        tab.MarkStale();

        Assert.Equal(game.Texts.TabStale, tab.StaleText);
    }

    [Fact] public void The_reload_is_offered_only_for_an_unmodified_tab()
    {
        using var game = new TestGame();
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "P"), "text/db/a.loc.tsv", game.Texts, _ => { },
            RowsDocument.ForLoc("text/db/a.loc", [new LocRow("k", "T", false)]), new ActionHistory());
        var requested = new List<EditableRowsTabViewModel>();
        tab.ReloadHandler = requested.Add;
        var enabled = new List<bool>();
        tab.ReloadCommand.CanExecuteChanged += (_, _) => enabled.Add(tab.ReloadCommand.CanExecute(null));

        Assert.False(tab.CanReload);
        tab.SetOutsideChange(OutsideChange.Changed);
        Assert.True(tab.CanReload);
        tab.Rows[0][0] = "other";
        Assert.False(tab.CanReload);
        tab.Document.MarkSaved();
        Assert.True(tab.CanReload);

        tab.ReloadCommand.Execute(null);

        Assert.Equal([tab], requested);
        Assert.Equal([true, false, true], enabled.Where((value, i) => i == 0 || value != enabled[i - 1]));   // asked again at every change
    }

    // ---- reload ----------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Reload_shows_the_new_content_clears_the_mark_and_drops_the_old_steps()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            a.Rows[0][Value] = "50";
            setup.Tabs.SelectedTab = a;
            setup.Controller.SaveCommand.Execute(null);                         // saved, the step stays in the history
            b.Rows[0][Value] = "8";
            Assert.False(a.IsModified);
            Assert.True(setup.History.CanUndo);

            setup.ChangeOutside(FileA, TextOf(FileA, 77));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.True(a.CanReload);

            await setup.Controller.ReloadAsync(a).WaitAsync(Guard);

            Assert.Same(a, setup.Tabs.Tabs[0]);                                  // the tab stays
            Assert.Equal("77", a.Rows[0][Value]);
            Assert.False(a.IsStale);
            Assert.False(a.IsModified);
            Assert.False(a.CanReload);
            Assert.Equal(ProjectFileWriter.Fingerprint(setup.Project.Folder, FileA), setup.Controller.KnownFingerprint(a));

            // Only the step of b is left: the undo reverts b and then the history is empty.
            setup.Controller.UndoCommand.Execute(null);
            Assert.Equal("7", b.Rows[0][Value]);
            Assert.False(setup.History.CanUndo);
            Assert.Equal("77", a.Rows[0][Value]);

            // The own reload is no outside change.
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.False(a.IsStale);
        }
    }

    [Fact] public async Task Reload_through_the_button_command_runs_the_same_way()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            setup.ChangeOutside(FileA, TextOf(FileA, 77));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            a.ReloadCommand.Execute(null);                                      // the fake runner runs at once

            Assert.Equal("77", a.Rows[0][Value]);
            Assert.False(a.IsStale);
        }
    }

    [Fact] public async Task Reload_keeps_a_schema_mark_and_clears_only_the_outside_mark()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            setup.Tabs.MarkSchemaStale();
            setup.ChangeOutside(FileA, TextOf(FileA, 77));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);

            await setup.Controller.ReloadAsync(a).WaitAsync(Guard);

            Assert.Equal(setup.Game.Texts.TabStaleSchema, a.StaleText);
            Assert.False(a.IsStaleOutside);
        }
    }

    [Fact] public async Task Reload_of_a_file_that_became_unreadable_replaces_the_tab_with_a_hint_tab_at_the_same_place()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            a.Rows[0][Value] = "50";
            setup.Tabs.SelectedTab = a;
            setup.Controller.SaveCommand.Execute(null);
            setup.ChangeOutside(FileA, "this is no table at all");
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.True(setup.History.CanUndo);

            await setup.Controller.ReloadAsync(a).WaitAsync(Guard);

            Assert.Equal(2, setup.Tabs.Tabs.Count);
            var hint = Assert.IsType<InfoTabViewModel>(setup.Tabs.Tabs[0]);
            Assert.Equal("db/x_tables/a.tsv", hint.EntryPath);
            Assert.Same(b, setup.Tabs.Tabs[1]);
            Assert.Same(hint, setup.Tabs.SelectedTab);                           // it was the selected one
            Assert.False(hint.IsStale);
            Assert.False(setup.History.CanUndo);                                 // the steps of the old document are gone
            Assert.NotNull(setup.Controller.KnownFingerprint(hint));
            Assert.Null(setup.Controller.KnownFingerprint(a));                   // the old tab is not followed any more
        }
    }

    [Fact] public async Task Reload_of_a_deleted_file_replaces_the_tab_with_a_hint_tab()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            setup.DeleteOutside(FileA);
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);

            await setup.Controller.ReloadAsync(a).WaitAsync(Guard);

            var hint = Assert.IsType<InfoTabViewModel>(Assert.Single(setup.Tabs.Tabs));
            Assert.Same(hint, setup.Tabs.SelectedTab);
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task Reload_while_an_operation_runs_says_so_and_changes_nothing()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            setup.ChangeOutside(FileA, TextOf(FileA, 77));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            setup.Game.Runner.IsRunning = true;

            await setup.Controller.ReloadAsync(a).WaitAsync(Guard);

            Assert.Equal((setup.Game.Texts.ReloadFailedTitle, setup.Game.Texts.ProjectBusy), Assert.Single(setup.Dialogs.Messages));
            Assert.Equal("42", a.Rows[0][Value]);
        }
    }

    [Fact] public async Task Reload_does_nothing_for_a_tab_that_is_not_stale_or_that_became_modified_before_the_handler_ran()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            setup.ChangeOutside(FileA, TextOf(FileA, 77));
            var runs = setup.Game.Runner.Titles.Count;

            await setup.Controller.ReloadAsync(a).WaitAsync(Guard);              // not marked yet: no reload button, no reload
            Assert.Equal("42", a.Rows[0][Value]);

            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.True(a.CanReload);
            a.Rows[0][Value] = "50";                                            // the user typed before the handler of the click ran
            Assert.False(a.CanReload);

            a.ReloadHandler!(a);
            await setup.Controller.ReloadAsync(a).WaitAsync(Guard);

            Assert.Equal("50", a.Rows[0][Value]);
            Assert.True(a.IsModified);
            Assert.Equal(runs, setup.Game.Runner.Titles.Count);                  // the file was not even read
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task A_tab_that_becomes_modified_while_its_file_is_read_keeps_its_content()
    {
        var runner = new ManualOperationRunner();
        var setup = await CreateWithRunnerAsync(runner, Table(FileA, 42));
        using (setup)
        {
            var opening = OpenAsync(setup, FileA);
            await runner.WaitForRun().WaitAsync(Guard);
            runner.Complete();
            var a = await opening.WaitAsync(Guard);
            setup.ChangeOutside(FileA, TextOf(FileA, 77));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.True(a.CanReload);

            var reload = setup.Controller.ReloadAsync(a);                        // waits for the read
            await runner.WaitForRun().WaitAsync(Guard);
            a.Rows[0][Value] = "50";
            runner.Complete();
            await reload.WaitAsync(Guard);

            Assert.Equal("50", a.Rows[0][Value]);
            Assert.True(a.IsModified);
            Assert.Same(a, setup.Tabs.Tabs[0]);
            Assert.True(a.IsStaleOutside);
        }
    }

    // ---- saving over an outside change -----------------------------------------------------------------------------------------------

    [Fact] public async Task Saving_over_an_outside_change_asks_first()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7), Table(FileC, 3));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            var c = await OpenAsync(setup, FileC);
            foreach (var (tab, file) in new[] { (a, FileA), (b, FileB), (c, FileC) })
            {
                tab.Rows[0][Value] = "50";
                setup.ChangeOutside(file, TextOf(file, 99));
            }

            // Cancel: the file keeps the outside content, the tab stays modified, no run, no message.
            var runs = setup.Game.Runner.Titles.Count;
            Assert.False(await setup.Controller.SaveAsync([a]));
            Assert.Equal(["db/x_tables/a.tsv"], setup.Dialogs.ConflictAsks);
            Assert.Equal(TextOf(FileA, 99), setup.Read(FileA));
            Assert.True(a.IsModified);
            Assert.Equal(runs, setup.Game.Runner.Titles.Count);
            Assert.Empty(setup.Dialogs.Messages);

            // Overwrite: the file has the content of the tab.
            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Overwrite);
            Assert.True(await setup.Controller.SaveAsync([b]));
            Assert.Equal(b.Document.ToText().Text, setup.Read(FileB));
            Assert.Contains("50", setup.Read(FileB));
            Assert.False(b.IsModified);
            Assert.False(b.IsStale);
            Assert.Empty(setup.Dialogs.Messages);

            // Reload: the tab has the outside content and is not modified; the save itself did not happen.
            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Reload);
            runs = setup.Game.Runner.Titles.Count;
            Assert.False(await setup.Controller.SaveAsync([c]));
            Assert.Equal("99", c.Rows[0][Value]);
            Assert.False(c.IsModified);
            Assert.False(c.IsStale);
            Assert.Equal(TextOf(FileC, 99), setup.Read(FileC));
            Assert.Equal(3, setup.Dialogs.ConflictAsks.Count);
            Assert.DoesNotContain(setup.Game.Texts.OperationSaveFiles, setup.Game.Runner.Titles.Skip(runs));   // only the reading of the file ran
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task Closing_with_Save_keeps_a_tab_that_the_user_chose_to_reload_or_to_cancel()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            setup.ChangeOutside(FileA, TextOf(FileA, 99));
            setup.ChangeOutside(FileB, TextOf(FileB, 98));
            setup.Dialogs.UnsavedAnswer = SaveChoice.Save;

            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Reload);
            setup.Tabs.Close(a);
            Assert.Contains(a, setup.Tabs.Tabs);
            Assert.Equal("99", a.Rows[0][Value]);

            setup.Tabs.Close(b);                                                // the default answer of the fake is Cancel
            Assert.Contains(b, setup.Tabs.Tabs);
            Assert.True(b.IsModified);

            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Overwrite);
            setup.Tabs.Close(b);
            Assert.DoesNotContain(b, setup.Tabs.Tabs);
            Assert.Equal(TextOf(FileB, 8), setup.Read(FileB));
        }
    }

    [Fact] public async Task Saving_a_deleted_file_asks_before_it_is_created_again()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            a.Rows[0][Value] = "50";
            setup.DeleteOutside(FileA);

            Assert.False(await setup.Controller.SaveAsync([a]));                // the fake says no

            Assert.Equal(["db/x_tables/a.tsv"], setup.Dialogs.CreateAgainAsks);
            Assert.False(File.Exists(setup.PathOf(FileA)));
            Assert.True(a.IsModified);
            Assert.Empty(setup.Dialogs.Messages);

            setup.Dialogs.CreateAgainAnswers.Enqueue(true);
            Assert.True(await setup.Controller.SaveAsync([a]));

            Assert.Equal(2, setup.Dialogs.CreateAgainAsks.Count);
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.False(a.IsModified);
            Assert.False(a.IsStale);
            Assert.Empty(setup.Dialogs.ConflictAsks);
        }
    }

    [Fact] public async Task Saving_a_deleted_file_whose_folder_is_gone_reports_the_problem_of_the_write()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            a.Rows[0][Value] = "50";
            Directory.Delete(Path.GetDirectoryName(setup.PathOf(FileA))!, recursive: true);
            setup.Dialogs.CreateAgainAnswer = true;

            Assert.False(await setup.Controller.SaveAsync([a]));

            Assert.Equal(setup.Game.Texts.SaveFailedTitle, Assert.Single(setup.Dialogs.Messages).Title);
            Assert.True(a.IsModified);
        }
    }

    [Fact] public async Task Save_all_asks_per_conflict_and_saves_the_rest()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7), Table(FileC, 3));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            var c = await OpenAsync(setup, FileC);
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            c.Rows[0][Value] = "4";
            setup.ChangeOutside(FileA, TextOf(FileA, 99));
            setup.ChangeOutside(FileB, TextOf(FileB, 98));
            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Overwrite);     // a
            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Cancel);        // b
            var runs = setup.Game.Runner.Titles.Count;
            var reports = setup.Game.Runner.Reports.Count;

            Assert.False(await setup.Controller.SaveAsync([a, b, c]));

            Assert.Equal(["db/x_tables/a.tsv", "db/x_tables/b.tsv"], setup.Dialogs.ConflictAsks);   // one question per affected file, c has none
            Assert.Equal(1, setup.Game.Runner.Titles.Skip(runs).Count(title => title == setup.Game.Texts.OperationSaveFiles));
            Assert.All(setup.Game.Runner.Reports.Skip(reports), report => Assert.Equal(2L, report.Total));   // a and c in one run
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.Equal(c.Document.ToText().Text, setup.Read(FileC));
            Assert.Equal(TextOf(FileB, 98), setup.Read(FileB));
            Assert.False(a.IsModified);
            Assert.True(b.IsModified);
            Assert.False(c.IsModified);
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task Save_all_with_a_deleted_and_a_changed_file_asks_both_kinds_of_question()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            setup.ChangeOutside(FileA, TextOf(FileA, 99));
            setup.DeleteOutside(FileB);
            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Overwrite);
            setup.Dialogs.CreateAgainAnswers.Enqueue(true);

            Assert.True(await setup.Controller.SaveAsync([a, b]));

            Assert.Equal(["db/x_tables/a.tsv"], setup.Dialogs.ConflictAsks);
            Assert.Equal(["db/x_tables/b.tsv"], setup.Dialogs.CreateAgainAsks);
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.Equal(b.Document.ToText().Text, setup.Read(FileB));
        }
    }

    [Fact] public async Task A_save_updates_the_fingerprint_so_the_own_write_is_not_an_outside_change()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var atOpen = setup.Controller.KnownFingerprint(a);
            a.Rows[0][Value] = "50";

            Assert.True(await setup.Controller.SaveAsync([a]));

            var written = ProjectFileWriter.Fingerprint(setup.Project.Folder, FileA);
            Assert.Equal(written, setup.Controller.KnownFingerprint(a));
            Assert.NotEqual(atOpen, written);
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.False(a.IsStale);

            // A second save asks nothing.
            a.Rows[0][Value] = "51";
            Assert.True(await setup.Controller.SaveAsync([a]));
            Assert.Empty(setup.Dialogs.ConflictAsks);
            Assert.Empty(setup.Dialogs.CreateAgainAsks);
        }
    }

    [Fact] public async Task A_stale_mark_is_cleared_by_a_successful_save_over_the_outside_change()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            a.Rows[0][Value] = "50";
            setup.ChangeOutside(FileA, TextOf(FileA, 99));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.True(a.IsStale);
            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Overwrite);

            Assert.True(await setup.Controller.SaveAsync([a]));

            Assert.False(a.IsStale);
        }
    }

    // ---- a file that changes while a question is open --------------------------------------------------------------------------------

    [Theory]
    [InlineData(ConflictChoice.Overwrite)]
    [InlineData(ConflictChoice.Cancel)]
    public async Task A_file_that_changes_while_the_question_for_another_file_is_open_is_not_written(ConflictChoice answer)
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            setup.ChangeOutside(FileB, TextOf(FileB, 98));                      // b is asked about; a looks unchanged
            setup.Dialogs.ConflictAnswer = answer;
            setup.Dialogs.WhileAsking = () => setup.ChangeOutside(FileA, TextOf(FileA, 99));   // the question for b is open: another program writes a

            Assert.False(await setup.Controller.SaveAsync([a, b]));

            Assert.Equal(["db/x_tables/b.tsv"], setup.Dialogs.ConflictAsks);
            Assert.Equal(TextOf(FileA, 99), setup.Read(FileA));                  // the outside content is still there
            Assert.True(a.IsModified);
            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);
            var (title, message) = Assert.Single(setup.Dialogs.Messages);
            Assert.Equal(setup.Game.Texts.SaveFailedTitle, title);
            Assert.Contains("db/x_tables/a.tsv", message);
            Assert.Contains(setup.Game.Texts.SaveProblemChangedAgain, message);
            Assert.DoesNotContain("db/x_tables/b.tsv", message);
            if (answer == ConflictChoice.Overwrite)
            {
                Assert.Equal(b.Document.ToText().Text, setup.Read(FileB));
                Assert.False(b.IsModified);
            }
            else
            {
                Assert.Equal(TextOf(FileB, 98), setup.Read(FileB));
                Assert.True(b.IsModified);
            }

            // The stored state of a is still the old one, so the next save asks.
            setup.Dialogs.WhileAsking = null;
            setup.Dialogs.ConflictAnswers.Enqueue(ConflictChoice.Overwrite);
            Assert.True(await setup.Controller.SaveAsync([a]));
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.Equal(["db/x_tables/b.tsv", "db/x_tables/a.tsv"], setup.Dialogs.ConflictAsks);
        }
    }

    [Fact] public async Task A_file_that_changes_again_while_its_own_Overwrite_question_is_open_is_not_written()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            a.Rows[0][Value] = "50";
            setup.ChangeOutside(FileA, TextOf(FileA, 98));
            setup.Dialogs.ConflictAnswer = ConflictChoice.Overwrite;
            setup.Dialogs.WhileAsking = () => setup.ChangeOutside(FileA, TextOf(FileA, 97));   // newer than what the user was asked about

            Assert.False(await setup.Controller.SaveAsync([a]));

            Assert.Equal(TextOf(FileA, 97), setup.Read(FileA));
            Assert.True(a.IsModified);
            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);
            Assert.Contains(setup.Game.Texts.SaveProblemChangedAgain, Assert.Single(setup.Dialogs.Messages).Message);
        }
    }

    [Fact] public async Task A_file_that_is_written_again_outside_while_the_create_again_question_is_open_is_not_overwritten()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            a.Rows[0][Value] = "50";
            setup.DeleteOutside(FileA);
            setup.Dialogs.CreateAgainAnswer = true;
            setup.Dialogs.WhileAsking = () => File.WriteAllText(setup.PathOf(FileA), TextOf(FileA, 77));

            Assert.False(await setup.Controller.SaveAsync([a]));

            Assert.Equal(TextOf(FileA, 77), setup.Read(FileA));
            Assert.True(a.IsModified);
            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);       // it is there again, but not as the tab read it
            Assert.Contains(setup.Game.Texts.SaveProblemChangedAgain, Assert.Single(setup.Dialogs.Messages).Message);
        }
    }

    [Fact] public async Task A_change_found_at_save_time_marks_the_tab_even_when_the_user_cancels()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            setup.ChangeOutside(FileA, TextOf(FileA, 99));
            setup.DeleteOutside(FileB);

            Assert.False(await setup.Controller.SaveAsync([a]));                // the fake answers Cancel
            Assert.False(await setup.Controller.SaveAsync([b]));                // and no

            Assert.Equal(setup.Game.Texts.TabStaleChanged, a.StaleText);
            Assert.Equal(setup.Game.Texts.TabStaleDeleted, b.StaleText);
            Assert.True(a.IsModified);
            Assert.True(b.IsModified);
        }
    }

    // ---- a file that cannot be told --------------------------------------------------------------------------------------------------

    [Fact] public async Task A_file_that_cannot_be_asked_is_neither_marked_deleted_nor_written_over()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            a.Rows[0][Value] = "50";
            var path = setup.PathOf(FileA);
            File.Delete(path);
            Directory.CreateDirectory(path);                                    // a folder in the place of the file: the state is unknown

            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
            Assert.False(a.IsStale);

            Assert.False(await setup.Controller.SaveAsync([a]));

            Assert.Empty(setup.Dialogs.ConflictAsks);
            Assert.Empty(setup.Dialogs.CreateAgainAsks);
            var (title, message) = Assert.Single(setup.Dialogs.Messages);
            Assert.Equal(setup.Game.Texts.SaveFailedTitle, title);
            Assert.Contains(setup.Game.Texts.SaveProblemCannotCheck, message);
            Assert.Contains("db/x_tables/a.tsv", message);
            Assert.True(a.IsModified);
            Assert.True(Directory.Exists(path));
            Assert.False(a.IsStale);
        }
    }

    [Fact] public async Task A_tab_with_no_known_state_and_a_file_that_cannot_be_asked_is_not_written()
    {
        var setup = await CreateAsync();
        using (setup)
        {
            var document = RowsDocument.ForLoc("text/db/a.loc", [new LocRow("k", "Text", false)]);
            var tab = new EditableRowsTabViewModel(setup.Source, "db/x_tables/a:b.tsv", setup.Game.Texts, _ => { }, document, setup.History);
            setup.Tabs.Tabs.Add(tab);                                           // an unsafe path (an alternate data stream): nothing can be asked
            Assert.Null(setup.Controller.KnownFingerprint(tab));
            tab.Rows[0][0] = "other";

            Assert.False(await setup.Controller.SaveAsync([tab]));

            Assert.Empty(setup.Dialogs.ConflictAsks);
            Assert.Empty(setup.Dialogs.CreateAgainAsks);
            Assert.Contains(setup.Game.Texts.SaveProblemCannotCheck, Assert.Single(setup.Dialogs.Messages).Message);
            Assert.True(tab.IsModified);
            Assert.False(tab.IsStale);
        }
    }

    [Fact] public async Task A_tab_that_left_the_list_is_forgotten()
    {
        var setup = await CreateAsync(Table(FileA, 42));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            Assert.NotNull(setup.Controller.KnownFingerprint(a));

            setup.Tabs.CloseWithoutAsking(a);

            Assert.Null(setup.Controller.KnownFingerprint(a));
            await setup.Controller.CheckExternalChangesAsync().WaitAsync(Guard);
        }
    }
}

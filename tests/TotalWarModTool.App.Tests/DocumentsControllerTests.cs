using TotalWarModTool.Editing;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class DocumentsControllerTests
{
    private const int Value = 0;

    private static readonly TableDefinition Def = new(0, [new FieldDefinition("value", FieldType.I32, nameof(FieldType.I32), false, null)]);

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, ProjectsController Projects, TabsViewModel Tabs, ActionHistory History,
        FakeEditDialogs Dialogs, DocumentsController Controller, ProjectState Project) : IDisposable
    {
        public void Dispose() => Game.Dispose();

        public TabSource Source => TabSource.ForProject(Project.Id, Project.DisplayName);
    }

    // A loaded TestGame with a project "Alpha" that holds the given files below its pack folder, and the controller wired as the app wires it.
    private static async Task<Setup> CreateAsync(params (string RelativeFile, string Text)[] files)
    {
        var game = new TestGame();
        var workspace = game.NewController();
        await workspace.LoadAsync();
        var projects = game.NewProjects(workspace);
        var project = game.NewProject(projects, "Alpha", files);
        var tabs = new TabsViewModel(game.Texts);
        var history = new ActionHistory();
        var dialogs = new FakeEditDialogs();
        var controller = new DocumentsController(tabs, projects, history, game.Runner, dialogs, game.Log, game.Texts);
        return new Setup(game, workspace, projects, tabs, history, dialogs, controller, project);
    }

    private static string TableText(string packPath, int value) =>
        TableTsv.Write("x_tables", 0, packPath, Def, [[value]]).Text!;

    private static string LocText(string packPath) => TableTsv.WriteLoc(packPath, [new LocRow("k", "Text", false)]).Text!;

    // Opens the file of the project in a tab the way the main view model does.
    private static async Task<TabViewModel> OpenAsync(Setup setup, string relativeFile)
    {
        var opened = await setup.Projects.OpenFileAsync(setup.Project.Id, relativeFile);
        Assert.NotNull(opened);
        setup.Tabs.Add(setup.Source, opened, setup.Workspace.Schema);
        return setup.Tabs.Tabs[^1];
    }

    private static async Task<(Setup Setup, EditableRowsTabViewModel A, EditableRowsTabViewModel B)> TwoTablesAsync()
    {
        var setup = await CreateAsync((@"db\x_tables\a.tsv", TableText("db/x_tables/a", 42)), (@"db\x_tables\b.tsv", TableText("db/x_tables/b", 7)));
        var a = Assert.IsType<EditableRowsTabViewModel>(await OpenAsync(setup, @"db\x_tables\a.tsv"));
        var b = Assert.IsType<EditableRowsTabViewModel>(await OpenAsync(setup, @"db\x_tables\b.tsv"));
        return (setup, a, b);
    }

    private sealed class FailingStep(Exception failure) : IUndoableAction
    {
        public string Description => "failing step";

        public void Do() => throw failure;

        public void Undo() => throw failure;
    }

    [Fact] public async Task A_project_table_in_text_form_opens_as_an_editable_tab()
    {
        using var setup = await CreateAsync((@"db\x_tables\a.tsv", TableText("db/x_tables/a", 42)));

        var tab = Assert.IsType<EditableRowsTabViewModel>(await OpenAsync(setup, @"db\x_tables\a.tsv"));

        Assert.Equal(setup.Project.Id, tab.ProjectId);
        Assert.Equal(@"db\x_tables\a.tsv", tab.RelativeFile);
        Assert.Equal(DocumentKind.Table, tab.Document.Kind);
        Assert.Equal(("x_tables", 0, "db/x_tables/a"), (tab.Document.TableName, tab.Document.Version, tab.Document.PackPath));
        Assert.Equal("42", Assert.Single(tab.Rows)[Value]);
        Assert.Same(tab, Assert.Single(setup.Controller.EditableTabs));
        Assert.Same(tab, setup.Tabs.SelectedTab);
    }

    [Fact] public async Task A_project_loc_file_in_text_form_opens_as_an_editable_tab()
    {
        using var setup = await CreateAsync((@"text\db\a.loc.tsv", LocText("text/db/a.loc")));

        var tab = Assert.IsType<EditableRowsTabViewModel>(await OpenAsync(setup, @"text\db\a.loc.tsv"));

        Assert.Equal(DocumentKind.Loc, tab.Document.Kind);
        Assert.Equal("text/db/a.loc", tab.Document.PackPath);
        var row = Assert.Single(tab.Rows);
        Assert.Equal("k", row[0]);
        Assert.Equal("Text", row[1]);
    }

    [Fact] public async Task A_binary_project_table_a_pack_table_and_a_broken_tsv_stay_as_they_were()
    {
        using var setup = await CreateAsync((@"db\x_tables\broken.tsv", TableText("db/x_tables/broken", 42).Replace("42", "many")));
        File.WriteAllBytes(Path.Combine(setup.Project.Folder, "pack", "db", "x_tables", "data__"), TestGame.TableBytes);
        setup.Game.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes));
        await setup.Workspace.RefreshAsync();

        // A table in binary form inside the project: decoded, shown read-only.
        Assert.IsType<TableTabViewModel>(await OpenAsync(setup, @"db\x_tables\data__"));

        // A tsv that cannot be read: the hint tab.
        Assert.IsType<InfoTabViewModel>(await OpenAsync(setup, @"db\x_tables\broken.tsv"));

        // A table of a pack: not a project, so not editable.
        var pack = Assert.Single(setup.Workspace.Catalog!.Packs);
        var opened = await setup.Workspace.OpenEntryAsync(pack, "db/x_tables/data__");
        setup.Tabs.Add(pack, opened!, setup.Workspace.Schema);
        Assert.IsType<TableTabViewModel>(setup.Tabs.Tabs[^1]);

        Assert.Empty(setup.Controller.EditableTabs);
        Assert.False(setup.Controller.HasModifiedTabs);
    }

    [Fact] public async Task Undo_switches_to_the_tab_of_the_step()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Tabs.SelectedTab = b;

            setup.Controller.UndoCommand.Execute(null);

            Assert.Same(a, setup.Tabs.SelectedTab);
            Assert.Equal("42", a.Rows[0][Value]);
            Assert.Equal("7", b.Rows[0][Value]);
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task Redo_switches_to_the_tab_of_the_step()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Controller.UndoCommand.Execute(null);
            setup.Tabs.SelectedTab = b;

            setup.Controller.RedoCommand.Execute(null);

            Assert.Same(a, setup.Tabs.SelectedTab);
            Assert.Equal("50", a.Rows[0][Value]);
        }
    }

    [Fact] public async Task Closing_a_tab_removes_its_steps()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            b.Rows[0][Value] = "8";
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "9";                                    // B, A, B

            setup.Tabs.Close(a);

            Assert.Same(b, Assert.Single(setup.Controller.EditableTabs));
            setup.Controller.UndoCommand.Execute(null);
            setup.Controller.UndoCommand.Execute(null);
            Assert.Equal("7", b.Rows[0][Value]);
            Assert.False(setup.History.CanUndo);                        // the step of A is gone

            // A redo of a closed tab's step is gone as well.
            setup.Tabs.CloseWithoutAsking(b);
            Assert.False(setup.History.CanUndo);
            Assert.False(setup.History.CanRedo);
        }
    }

    [Fact] public async Task Closing_a_tab_removes_its_redo_steps_too()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            setup.Controller.UndoCommand.Execute(null);                 // B's step is on the redo stack

            setup.Tabs.CloseWithoutAsking(b);

            Assert.False(setup.History.CanRedo);
            Assert.True(setup.History.CanUndo);                         // A's step stays
            Assert.False(setup.Controller.RedoCommand.CanExecute(null));
        }
    }

    [Fact] public async Task Undo_and_redo_are_disabled_when_empty_and_while_an_operation_runs()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            var undoRaised = 0; var redoRaised = 0;
            setup.Controller.UndoCommand.CanExecuteChanged += (_, _) => undoRaised++;
            setup.Controller.RedoCommand.CanExecuteChanged += (_, _) => redoRaised++;
            Assert.False(setup.Controller.UndoCommand.CanExecute(null));
            Assert.False(setup.Controller.RedoCommand.CanExecute(null));

            a.Rows[0][Value] = "50";
            Assert.True(setup.Controller.UndoCommand.CanExecute(null));
            Assert.False(setup.Controller.RedoCommand.CanExecute(null));
            Assert.True(undoRaised > 0);
            Assert.True(redoRaised > 0);

            setup.Controller.UndoCommand.Execute(null);
            Assert.False(setup.Controller.UndoCommand.CanExecute(null));
            Assert.True(setup.Controller.RedoCommand.CanExecute(null));

            setup.Game.Runner.IsRunning = true;
            Assert.False(setup.Controller.RedoCommand.CanExecute(null));
            setup.Controller.RedoCommand.Execute(null);                 // a direct call does nothing either
            Assert.Equal("42", a.Rows[0][Value]);
            Assert.Empty(setup.Dialogs.Messages);

            setup.Game.Runner.IsRunning = false;
            Assert.True(setup.Controller.RedoCommand.CanExecute(null));
            setup.Controller.UndoCommand.Execute(null);                 // nothing to undo: ignored, no message
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task A_failing_undo_shows_a_message_and_does_not_throw()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            // A step whose undo refuses, on top of the edit of A.
            setup.History.Record(new FailingStep(new InvalidOperationException("the row is gone " + (char)7)));
            var before = setup.Game.Log.Errors.Count;

            setup.Controller.UndoCommand.Execute(null);

            var message = Assert.Single(setup.Dialogs.Messages);
            Assert.Equal(setup.Game.Texts.EditUndoFailedTitle, message.Title);
            Assert.Contains("the row is gone \\u0007", message.Message);
            Assert.False(message.Message.Contains((char)7));
            Assert.Equal(before + 1, setup.Game.Log.Errors.Count);
            Assert.IsType<InvalidOperationException>(setup.Game.Log.Exceptions[^1]);

            // The history is as ActionHistory left it: the failing step is dropped, the step below stays.
            Assert.False(setup.History.CanRedo);
            Assert.True(setup.History.CanUndo);
            setup.Controller.UndoCommand.Execute(null);
            Assert.Equal("42", a.Rows[0][Value]);
        }
    }

    [Fact] public async Task An_exception_that_a_document_edit_cannot_throw_propagates()
    {
        var (setup, _, _) = await TwoTablesAsync();
        using (setup)
        {
            setup.History.Record(new FailingStep(new NotSupportedException("surprise")));

            var surprise = Assert.Throws<NotSupportedException>(() => setup.Controller.UndoCommand.Execute(null));
            Assert.Equal("surprise", surprise.Message);
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task A_failing_redo_is_reported_with_the_redo_texts()
    {
        var (setup, _, _) = await TwoTablesAsync();
        using (setup)
        {
            // Undo works (the step does nothing), the redo throws.
            var step = new RedoFailingStep();
            setup.History.Record(step);
            setup.Controller.UndoCommand.Execute(null);
            Assert.Empty(setup.Dialogs.Messages);

            setup.Controller.RedoCommand.Execute(null);

            var message = Assert.Single(setup.Dialogs.Messages);
            Assert.Equal(setup.Game.Texts.EditRedoFailedTitle, message.Title);
            Assert.Contains("redo refused", message.Message);
            Assert.False(setup.History.CanRedo);
        }
    }

    private sealed class RedoFailingStep : IUndoableAction
    {
        public string Description => "redo failing step";

        public void Do() => throw new InvalidOperationException("redo refused");

        public void Undo()
        {
        }
    }

    [Fact] public async Task A_table_that_the_document_refuses_opens_as_the_default_tab_and_logs_a_warning()
    {
        using var setup = await CreateAsync();
        // The reader cannot deliver this (a row with two cells for one field), so the entry is built by hand.
        var table = new DecodedTable(TableDecodeStatus.Ok, "x_tables", 0, false, Def, [[1, 2]], null);
        var entry = new OpenedEntry("db/x_tables/bad.tsv", EntryKind.Table, 10, 10, CompressionFormat.None, EntryProblem.None, null, false, table, null, null);
        var before = setup.Game.Log.Warnings.Count;

        setup.Tabs.Add(setup.Source, entry, setup.Workspace.Schema);

        Assert.IsType<TableTabViewModel>(setup.Tabs.Tabs[^1]);
        Assert.Empty(setup.Controller.EditableTabs);
        Assert.Equal(before + 1, setup.Game.Log.Warnings.Count);
        Assert.Contains("db/x_tables/bad.tsv", setup.Game.Log.Warnings[^1]);
    }

    [Fact] public async Task Clearing_the_tab_list_removes_the_steps_of_all_editable_tabs()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            Assert.True(setup.History.CanUndo);

            setup.Tabs.Tabs.Clear();

            Assert.Empty(setup.Controller.EditableTabs);
            Assert.False(setup.History.CanUndo);
            Assert.False(setup.History.CanRedo);
            Assert.False(setup.Controller.HasModifiedTabs);
        }
    }

    [Fact] public async Task A_failure_to_select_the_tab_after_a_step_is_no_failed_step()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Tabs.SelectedTab = b;
            setup.Tabs.Changed += (_, _) =>
            {
                if (ReferenceEquals(setup.Tabs.SelectedTab, a))
                {
                    throw new InvalidOperationException("selecting failed");
                }
            };
            var errors = setup.Game.Log.Errors.Count;

            var thrown = Assert.Throws<InvalidOperationException>(() => setup.Controller.UndoCommand.Execute(null));

            Assert.Equal("selecting failed", thrown.Message);
            Assert.Empty(setup.Dialogs.Messages);                       // not reported as a failed undo
            Assert.Equal(errors, setup.Game.Log.Errors.Count);
            Assert.Equal("42", a.Rows[0][Value]);                       // the step itself was undone
            Assert.True(setup.History.CanRedo);
        }
    }

    [Fact] public async Task HasModifiedTabs_and_ModifiedChanged_follow_the_tabs()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            var raised = 0;
            setup.Controller.ModifiedChanged += (_, _) => raised++;
            Assert.False(setup.Controller.HasModifiedTabs);

            a.Rows[0][Value] = "50";
            Assert.True(setup.Controller.HasModifiedTabs);
            Assert.Equal(1, raised);

            a.Rows[0][Value] = "51";                                    // A stays modified: the set did not change
            Assert.Equal(1, raised);

            b.Rows[0][Value] = "8";
            Assert.Equal(2, raised);

            setup.Controller.UndoCommand.Execute(null);                 // B is back to its saved state
            Assert.Equal(3, raised);
            Assert.True(setup.Controller.HasModifiedTabs);

            setup.Tabs.CloseWithoutAsking(a);                           // the modified tab leaves
            Assert.Equal(4, raised);
            Assert.False(setup.Controller.HasModifiedTabs);

            // A tab that left no longer reports.
            a.Document.MarkSaved();
            Assert.Equal(4, raised);
            Assert.Same(b, Assert.Single(setup.Controller.EditableTabs));
        }
    }
}

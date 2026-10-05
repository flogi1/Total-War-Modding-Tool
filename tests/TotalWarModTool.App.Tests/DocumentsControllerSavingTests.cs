using TotalWarModTool.Editing;
using TotalWarModTool.Operations;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.App.Tests;

public class DocumentsControllerSavingTests
{
    private const int Value = 0;
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);
    private const string FileA = @"db\x_tables\a.tsv";
    private const string FileB = @"db\x_tables\b.tsv";
    private const string FileC = @"db\x_tables\c.tsv";

    private static readonly TableDefinition Def = new(0, [new FieldDefinition("value", FieldType.I32, nameof(FieldType.I32), false, null)]);

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, ProjectsController Projects, TabsViewModel Tabs, ActionHistory History,
        FakeEditDialogs Dialogs, DocumentsController Controller, ProjectState Project) : IDisposable
    {
        public void Dispose() => Game.Dispose();

        public TabSource Source => TabSource.ForProject(Project.Id, Project.DisplayName);

        public string PathOf(string relativeFile) => Path.Combine(Project.Folder, "pack", relativeFile);

        public string Read(string relativeFile) => File.ReadAllText(PathOf(relativeFile));
    }

    // A loaded TestGame with a project "Alpha" that holds the given files below its pack folder, and the controller wired as the app wires it.
    // The documents controller gets its own runner when one is given; the projects keep the fake of the game.
    private static async Task<Setup> CreateAsync(IOperationRunner? runner, params (string RelativeFile, string Text)[] files)
    {
        var game = new TestGame();
        var workspace = game.NewController();
        await workspace.LoadAsync();
        var projects = game.NewProjects(workspace);
        var project = game.NewProject(projects, "Alpha", files);
        var tabs = new TabsViewModel(game.Texts);
        var history = new ActionHistory();
        var dialogs = new FakeEditDialogs();
        var controller = new DocumentsController(tabs, projects, history, runner ?? game.Runner, dialogs, game.Log, game.Texts);
        return new Setup(game, workspace, projects, tabs, history, dialogs, controller, project);
    }

    private static Task<Setup> CreateAsync(params (string RelativeFile, string Text)[] files) => CreateAsync(null, files);

    private static string TableText(string packPath, int value) => TableTsv.Write("x_tables", 0, packPath, Def, [[value]]).Text!;

    private static (string, string) Table(string relativeFile, int value) =>
        (relativeFile, TableText(relativeFile.Replace('\\', '/').Replace(".tsv", string.Empty), value));

    private static async Task<EditableRowsTabViewModel> OpenAsync(Setup setup, string relativeFile)
    {
        var opened = await setup.Projects.OpenFileAsync(setup.Project.Id, relativeFile);
        Assert.NotNull(opened);
        setup.Tabs.Add(setup.Source, opened, setup.Workspace.Schema);
        return Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.Tabs[^1]);
    }

    private static async Task<(Setup Setup, EditableRowsTabViewModel A, EditableRowsTabViewModel B)> TwoTablesAsync(IOperationRunner? runner = null)
    {
        var setup = await CreateAsync(runner, Table(FileA, 42), Table(FileB, 7));
        var a = await OpenAsync(setup, FileA);
        var b = await OpenAsync(setup, FileB);
        return (setup, a, b);
    }

    // Makes the file read-only: the writer reports it as locked, quickly and without a second program.
    private static IDisposable ReadOnly(string path)
    {
        File.SetAttributes(path, FileAttributes.ReadOnly);
        return new ReadOnlyRestorer(path);
    }

    private sealed class ReadOnlyRestorer(string path) : IDisposable
    {
        public void Dispose() => File.SetAttributes(path, FileAttributes.Normal);
    }

    // A runner whose run reports "failed" although the work ran to its end (the progress window could not be shown).
    private sealed class FailedWithValueRunner : IOperationRunner
    {
        public bool IsRunning => false;

        public Task<OperationOutcome<T>> RunAsync<T>(string title, Func<IProgress<OperationProgress>, CancellationToken, T> work) =>
            Task.FromResult(new OperationOutcome<T>(OperationStatus.Failed, work(new Progress<OperationProgress>(), CancellationToken.None),
                new InvalidOperationException("the window could not be shown")));
    }

    // ---- save and save all -----------------------------------------------------------------------------------------------------

    [Fact] public async Task Save_writes_the_file_clears_the_mark_and_keeps_the_history()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            var originalB = setup.Read(FileB);
            a.Rows[0][Value] = "50";
            setup.Tabs.SelectedTab = a;
            Assert.True(a.IsModified);
            var runs = setup.Game.Runner.Titles.Count;

            setup.Controller.SaveCommand.Execute(null);

            Assert.Equal(setup.Game.Texts.OperationSaveFiles, Assert.Single(setup.Game.Runner.Titles.Skip(runs)));
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.Contains("50", setup.Read(FileA));
            Assert.False(a.IsModified);
            Assert.False(setup.Controller.HasModifiedTabs);
            Assert.Equal(ProjectFileWriter.Fingerprint(setup.Project.Folder, FileA), setup.Controller.KnownFingerprint(a));
            Assert.NotNull(setup.Controller.KnownFingerprint(a));
            Assert.Equal(originalB, setup.Read(FileB));
            Assert.Empty(setup.Dialogs.Messages);

            // The history is kept: an undo is possible and makes the tab modified again; the file is as saved.
            Assert.True(setup.History.CanUndo);
            setup.Controller.UndoCommand.Execute(null);
            Assert.True(a.IsModified);
            Assert.Equal("42", a.Rows[0][Value]);
            Assert.Contains("50", setup.Read(FileA));
        }
    }

    [Fact] public async Task Save_all_writes_every_modified_tab_in_one_run()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7), Table(FileC, 3));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            var c = await OpenAsync(setup, FileC);
            var originalC = setup.Read(FileC);
            var knownC = setup.Controller.KnownFingerprint(c);
            Assert.NotNull(knownC);                                                 // taken when the tab was opened
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            var runs = setup.Game.Runner.Titles.Count; var reports = setup.Game.Runner.Reports.Count;

            setup.Controller.SaveAllCommand.Execute(null);

            Assert.Equal(1, setup.Game.Runner.Titles.Count - runs);
            Assert.Equal([1L, 2L], setup.Game.Runner.Reports.Skip(reports).Select(report => report.Done));
            Assert.All(setup.Game.Runner.Reports.Skip(reports), report => Assert.Equal(2L, report.Total));
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.Equal(b.Document.ToText().Text, setup.Read(FileB));
            Assert.Equal(originalC, setup.Read(FileC));                        // not modified: not written
            Assert.False(a.IsModified);
            Assert.False(b.IsModified);
            Assert.False(setup.Controller.HasModifiedTabs);
            Assert.NotNull(setup.Controller.KnownFingerprint(a));
            Assert.Equal(ProjectFileWriter.Fingerprint(setup.Project.Folder, FileB), setup.Controller.KnownFingerprint(b));
            Assert.Equal(knownC, setup.Controller.KnownFingerprint(c));        // not written: still the state at open
        }
    }

    [Fact] public async Task A_locked_file_keeps_the_tab_modified_and_names_the_file()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            var original = setup.Read(FileA);
            var knownA = setup.Controller.KnownFingerprint(a);
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";

            using (ReadOnly(setup.PathOf(FileA)))
            {
                setup.Controller.SaveAllCommand.Execute(null);
            }

            var (title, message) = Assert.Single(setup.Dialogs.Messages);
            Assert.Equal(setup.Game.Texts.SaveFailedTitle, title);
            Assert.Contains("db/x_tables/a.tsv", message);
            Assert.Contains(setup.Game.Texts.SaveProblemLocked, message);
            Assert.DoesNotContain("db/x_tables/b.tsv", message);
            Assert.True(a.IsModified);
            Assert.Equal(original, setup.Read(FileA));
            Assert.Equal(knownA, setup.Controller.KnownFingerprint(a));        // not written: still the state at open

            // The other file was written and is saved.
            Assert.False(b.IsModified);
            Assert.Equal(b.Document.ToText().Text, setup.Read(FileB));
        }
    }

    [Fact] public async Task Save_is_disabled_for_an_unmodified_tab_and_for_a_read_only_tab()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            File.WriteAllBytes(setup.PathOf(@"db\x_tables\data__"), TestGame.TableBytes);
            var binary = Assert.IsType<TableTabViewModel>(await OpenBinaryAsync(setup, @"db\x_tables\data__"));
            var raised = 0;
            setup.Controller.SaveCommand.CanExecuteChanged += (_, _) => raised++;
            setup.Controller.SaveAllCommand.CanExecuteChanged += (_, _) => raised++;

            setup.Tabs.SelectedTab = a;
            Assert.False(setup.Controller.SaveCommand.CanExecute(null));
            Assert.False(setup.Controller.SaveAllCommand.CanExecute(null));

            a.Rows[0][Value] = "50";
            Assert.True(raised > 0);                                            // the commands were asked to ask again
            Assert.True(setup.Controller.SaveCommand.CanExecute(null));
            Assert.True(setup.Controller.SaveAllCommand.CanExecute(null));

            setup.Tabs.SelectedTab = b;                                         // an unmodified tab, while another one is modified
            Assert.False(setup.Controller.SaveCommand.CanExecute(null));
            Assert.True(setup.Controller.SaveAllCommand.CanExecute(null));

            setup.Tabs.SelectedTab = binary;                                    // a read-only tab
            Assert.False(setup.Controller.SaveCommand.CanExecute(null));

            setup.Tabs.SelectedTab = a;
            setup.Game.Runner.IsRunning = true;                                 // no save while an operation runs
            Assert.False(setup.Controller.SaveCommand.CanExecute(null));
            Assert.False(setup.Controller.SaveAllCommand.CanExecute(null));
            setup.Controller.SaveCommand.Execute(null);                         // a direct call does nothing either
            Assert.True(a.IsModified);
            Assert.Empty(setup.Dialogs.Messages);

            setup.Game.Runner.IsRunning = false;
            Assert.True(setup.Controller.SaveCommand.CanExecute(null));
            setup.Tabs.SelectedTab = null;
            Assert.False(setup.Controller.SaveCommand.CanExecute(null));
        }
    }

    private static async Task<TabViewModel> OpenBinaryAsync(Setup setup, string relativeFile)
    {
        var opened = await setup.Projects.OpenFileAsync(setup.Project.Id, relativeFile);
        Assert.NotNull(opened);
        setup.Tabs.Add(setup.Source, opened, setup.Workspace.Schema);
        return setup.Tabs.Tabs[^1];
    }

    [Fact] public async Task A_save_while_an_operation_runs_is_refused_with_a_message()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Game.Runner.IsRunning = true;
            var runs = setup.Game.Runner.Titles.Count;

            Assert.False(await setup.Controller.SaveAsync([a]));

            Assert.Equal((setup.Game.Texts.SaveFailedTitle, setup.Game.Texts.ProjectBusy), Assert.Single(setup.Dialogs.Messages));
            Assert.Equal(runs, setup.Game.Runner.Titles.Count);
            Assert.True(a.IsModified);
        }
    }

    [Fact] public async Task A_cancel_between_two_files_keeps_the_written_ones_saved_and_says_how_many()
    {
        var setup = await CreateAsync(Table(FileA, 42), Table(FileB, 7), Table(FileC, 3));
        using (setup)
        {
            var a = await OpenAsync(setup, FileA);
            var b = await OpenAsync(setup, FileB);
            var c = await OpenAsync(setup, FileC);
            var originalB = setup.Read(FileB);
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            c.Rows[0][Value] = "4";
            setup.Game.Runner.OnReport = (progress, cancel) =>
            {
                if (progress.Done == 2)
                {
                    cancel();                                                   // the user cancels while the second file is next
                }
            };

            Assert.False(await setup.Controller.SaveAsync([a, b, c]));

            Assert.False(a.IsModified);
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.True(b.IsModified);
            Assert.True(c.IsModified);
            Assert.Equal(originalB, setup.Read(FileB));
            var (title, message) = Assert.Single(setup.Dialogs.Messages);
            Assert.Equal(setup.Game.Texts.SaveCancelledTitle, title);
            Assert.Equal(setup.Game.Texts.SaveCancelledMessage(1, 3), message);
        }
    }

    [Fact] public async Task A_run_that_is_reported_as_failed_but_has_its_value_is_used()
    {
        var (setup, a, _) = await TwoTablesAsync(new FailedWithValueRunner());
        using (setup)
        {
            a.Rows[0][Value] = "50";

            Assert.True(await setup.Controller.SaveAsync([a]));

            Assert.False(a.IsModified);
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task A_tab_whose_project_left_the_list_cannot_be_saved()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            var original = setup.Read(FileA);
            a.Rows[0][Value] = "50";
            Assert.True(setup.Projects.Remove(setup.Project.Id).Ok);

            Assert.False(await setup.Controller.SaveAsync([a]));

            var (_, message) = Assert.Single(setup.Dialogs.Messages);
            Assert.Contains(setup.Game.Texts.SaveProblemProjectGone, message);
            Assert.Contains("db/x_tables/a.tsv", message);
            Assert.True(a.IsModified);
            Assert.Equal(original, setup.Read(FileA));
        }
    }

    [Fact] public async Task Many_problems_are_one_message_of_at_most_ten_lines_and_a_count()
    {
        var files = Enumerable.Range(0, 12).Select(i => Table($@"db\x_tables\t{i:00}.tsv", i)).ToArray();
        var setup = await CreateAsync(files);
        using (setup)
        {
            var tabs = new List<EditableRowsTabViewModel>();
            foreach (var (file, _) in files)
            {
                var tab = await OpenAsync(setup, file);
                tab.Rows[0][Value] = "99";
                tabs.Add(tab);
            }

            Assert.True(setup.Projects.Remove(setup.Project.Id).Ok);              // every file now fails the same cheap way

            Assert.False(await setup.Controller.SaveAsync(tabs));

            var (_, message) = Assert.Single(setup.Dialogs.Messages);
            var lines = message.Split('\n');
            Assert.Equal(10, lines.Count(line => line.StartsWith("db/x_tables/t", StringComparison.Ordinal)));
            Assert.Contains(setup.Game.Texts.ReportMoreLines(2), lines);
            Assert.Contains(setup.Game.Texts.SaveFailedSummary(12), message);
            Assert.All(tabs, tab => Assert.True(tab.IsModified));
        }
    }

    // ---- closing a tab ---------------------------------------------------------------------------------------------------------

    [Fact] public async Task Closing_an_unmodified_tab_does_not_ask()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            setup.Tabs.Close(a);

            Assert.DoesNotContain(a, setup.Tabs.Tabs);
            Assert.Empty(setup.Dialogs.UnsavedAsks);
        }
    }

    [Fact] public async Task Closing_a_modified_tab_asks_and_cancel_keeps_it()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            var original = setup.Read(FileA);
            a.Rows[0][Value] = "50";

            setup.Tabs.Close(a);                                                // the default answer of the fake is Cancel

            Assert.Contains(a, setup.Tabs.Tabs);
            Assert.True(a.IsModified);
            Assert.Equal(["db/x_tables/a.tsv"], Assert.Single(setup.Dialogs.UnsavedAsks));
            Assert.Equal(original, setup.Read(FileA));

            // Asked again on the next try: the first question is over.
            setup.Tabs.Close(a);
            Assert.Equal(2, setup.Dialogs.UnsavedAsks.Count);
        }
    }

    [Fact] public async Task Closing_a_modified_tab_with_Discard_closes_it_and_leaves_the_file()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            var original = setup.Read(FileA);
            a.Rows[0][Value] = "50";
            setup.Dialogs.UnsavedAnswer = SaveChoice.Discard;

            setup.Tabs.Close(a);

            Assert.DoesNotContain(a, setup.Tabs.Tabs);
            Assert.Equal(original, setup.Read(FileA));
            Assert.False(setup.History.CanUndo);                                // the steps of the closed tab are gone
            Assert.False(setup.Controller.HasModifiedTabs);
        }
    }

    [Fact] public async Task Closing_a_modified_tab_with_Save_saves_then_closes()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            var expected = a.Document.ToText().Text;
            setup.Dialogs.UnsavedAnswer = SaveChoice.Save;

            setup.Tabs.Close(a);

            Assert.DoesNotContain(a, setup.Tabs.Tabs);
            Assert.Equal(expected, setup.Read(FileA));
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task Closing_with_Save_keeps_the_tab_when_the_save_fails()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Dialogs.UnsavedAnswer = SaveChoice.Save;

            using (ReadOnly(setup.PathOf(FileA)))
            {
                setup.Tabs.Close(a);
            }

            Assert.Contains(a, setup.Tabs.Tabs);
            Assert.True(a.IsModified);
            Assert.Equal(setup.Game.Texts.SaveFailedTitle, Assert.Single(setup.Dialogs.Messages).Title);
        }
    }

    [Fact] public async Task A_second_close_request_while_the_question_is_open_is_ignored()
    {
        var runner = new ManualOperationRunner();
        var (setup, a, _) = await TwoTablesAsync(runner);
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Dialogs.UnsavedAnswer = SaveChoice.Save;

            setup.Tabs.Close(a);                                                // asks, the answer is Save, the save is now pending
            await runner.WaitForRun().WaitAsync(Guard);
            setup.Tabs.Close(a);                                                // ignored: this tab is already being closed


            Assert.Single(setup.Dialogs.UnsavedAsks);

            runner.Complete();

            Assert.DoesNotContain(a, setup.Tabs.Tabs);
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
        }
    }

    [Fact] public async Task A_failure_of_the_question_is_logged_and_the_tab_stays_closable_later()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Dialogs.WhileAsking = () => throw new InvalidOperationException("the dialog broke");
            var errors = setup.Game.Log.Errors.Count;

            setup.Tabs.Close(a);                                                // does not throw, the tab stays

            Assert.Contains(a, setup.Tabs.Tabs);
            Assert.Equal(errors + 1, setup.Game.Log.Errors.Count);
            Assert.IsType<InvalidOperationException>(setup.Game.Log.Exceptions[^1]);

            setup.Dialogs.WhileAsking = null;
            setup.Dialogs.UnsavedAnswer = SaveChoice.Discard;
            setup.Tabs.Close(a);
            Assert.DoesNotContain(a, setup.Tabs.Tabs);
        }
    }

    [Fact] public async Task A_failure_of_the_question_also_tells_the_user()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Dialogs.WhileAsking = () => throw new InvalidOperationException("the dialog broke");

            setup.Tabs.Close(a);                                                // the close button must not do nothing silently

            var (title, message) = Assert.Single(setup.Dialogs.Messages);
            Assert.Equal(setup.Game.Texts.ActionFailedTitle, title);
            Assert.Equal(setup.Game.Texts.ActionFailedMessage("the dialog broke"), message);
            Assert.Contains(a, setup.Tabs.Tabs);
        }
    }

    [Fact] public async Task When_the_message_about_a_failure_cannot_be_shown_either_it_is_only_logged()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Dialogs.WhileAsking = () => throw new InvalidOperationException("the dialog broke");
            setup.Dialogs.ShowMessageFailure = new InvalidOperationException("no window");
            var errors = setup.Game.Log.Errors.Count;

            setup.Tabs.Close(a);                                                // does not throw

            Assert.Single(setup.Dialogs.Messages);
            Assert.Equal(errors + 2, setup.Game.Log.Errors.Count);              // the failure, then the message that could not be shown
            Assert.Equal("the dialog broke", setup.Game.Log.Exceptions[^2]!.Message);
            Assert.Equal("no window", setup.Game.Log.Exceptions[^1]!.Message);
            Assert.Contains(a, setup.Tabs.Tabs);
        }
    }

    // ---- closing the app -------------------------------------------------------------------------------------------------------

    [Fact] public async Task Closing_the_app_with_modified_tabs_asks_once()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            var closed = 0;
            Assert.True(setup.Controller.RequestAppClose(() => closed++));      // nothing modified: the window may close at once
            Assert.Empty(setup.Dialogs.UnsavedAsks);

            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";
            setup.Dialogs.UnsavedAnswer = SaveChoice.Discard;

            Assert.False(setup.Controller.RequestAppClose(() => closed++));     // asks, then closes the window again by itself

            Assert.Equal(["db/x_tables/a.tsv", "db/x_tables/b.tsv"], Assert.Single(setup.Dialogs.UnsavedAsks));
            Assert.Equal(1, closed);
            Assert.True(setup.Controller.RequestAppClose(() => closed++));      // that second Closing event: no second question
            Assert.Single(setup.Dialogs.UnsavedAsks);
            Assert.Equal(1, closed);
        }
    }

    [Fact] public async Task Closing_the_app_with_Save_saves_every_tab_and_Cancel_keeps_the_window()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            var closed = 0;
            a.Rows[0][Value] = "50";
            b.Rows[0][Value] = "8";

            Assert.False(setup.Controller.RequestAppClose(() => closed++));     // the default answer is Cancel
            Assert.Equal(0, closed);
            Assert.True(a.IsModified);

            setup.Dialogs.UnsavedAnswer = SaveChoice.Save;
            Assert.False(setup.Controller.RequestAppClose(() => closed++));

            Assert.Equal(1, closed);
            Assert.False(setup.Controller.HasModifiedTabs);
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.Equal(b.Document.ToText().Text, setup.Read(FileB));
            Assert.Equal(2, setup.Dialogs.UnsavedAsks.Count);
        }
    }

    [Fact] public async Task Closing_the_app_while_an_operation_runs_only_refuses()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            var closed = 0;
            a.Rows[0][Value] = "50";
            setup.Game.Runner.IsRunning = true;

            Assert.False(setup.Controller.RequestAppClose(() => closed++));

            Assert.Equal(0, closed);
            Assert.Empty(setup.Dialogs.UnsavedAsks);
            Assert.Empty(setup.Dialogs.Messages);
        }
    }

    [Fact] public async Task Closing_the_app_while_the_question_is_open_does_not_ask_twice()
    {
        var runner = new ManualOperationRunner();
        var (setup, a, _) = await TwoTablesAsync(runner);
        using (setup)
        {
            var closed = 0;
            a.Rows[0][Value] = "50";
            setup.Dialogs.UnsavedAnswer = SaveChoice.Save;

            Assert.False(setup.Controller.RequestAppClose(() => closed++));     // the save is pending
            await runner.WaitForRun().WaitAsync(Guard);
            Assert.False(setup.Controller.RequestAppClose(() => closed++));     // ignored

            Assert.Single(setup.Dialogs.UnsavedAsks);
            runner.Complete();
            Assert.Equal(1, closed);
        }
    }

    [Fact] public async Task A_second_app_close_request_while_the_question_is_open_is_refused_and_after_Cancel_the_next_one_asks_again()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            var closed = 0;
            bool? nested = null;
            var asksInside = -1;
            a.Rows[0][Value] = "50";
            setup.Dialogs.WhileAsking = () =>
            {
                if (nested is null)                                             // the question of the first request is open right now
                {
                    nested = setup.Controller.RequestAppClose(() => closed++);
                    asksInside = setup.Dialogs.UnsavedAsks.Count;
                }
            };

            Assert.False(setup.Controller.RequestAppClose(() => closed++));     // the fake answers Cancel

            Assert.False(nested);
            Assert.Equal(1, asksInside);                                        // the second request did not ask a second time
            Assert.Single(setup.Dialogs.UnsavedAsks);
            Assert.Equal(0, closed);

            setup.Dialogs.WhileAsking = null;
            Assert.False(setup.Controller.RequestAppClose(() => closed++));     // the question is over: this one asks again

            Assert.Equal(2, setup.Dialogs.UnsavedAsks.Count);
            Assert.Equal(0, closed);
        }
    }

    // ---- remove from the list and build ----------------------------------------------------------------------------------------

    [Fact] public async Task UnsavedReason_names_a_project_with_modified_tabs_only()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            var other = setup.Game.NewProject(setup.Projects, "Beta", Table(FileA, 1));
            Assert.Null(setup.Controller.UnsavedReason(setup.Project.Id));
            Assert.Null(setup.Controller.UnsavedReason(other.Id));

            a.Rows[0][Value] = "50";

            Assert.Equal(setup.Game.Texts.ProjectRemoveUnsaved, setup.Controller.UnsavedReason(setup.Project.Id));
            Assert.Null(setup.Controller.UnsavedReason(other.Id));
            Assert.Null(setup.Controller.UnsavedReason(Guid.NewGuid()));

            setup.Tabs.SelectedTab = a;
            setup.Controller.SaveCommand.Execute(null);
            Assert.Null(setup.Controller.UnsavedReason(setup.Project.Id));
            Assert.False(b.IsModified);
        }
    }

    [Fact] public async Task The_question_names_the_project_when_tabs_of_several_projects_are_listed()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            var other = setup.Game.NewProject(setup.Projects, "Beta", Table(FileA, 1));
            var opened = await setup.Projects.OpenFileAsync(other.Id, FileA);
            setup.Tabs.Add(TabSource.ForProject(other.Id, "Beta"), opened!, setup.Workspace.Schema);
            var beta = Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.Tabs[^1]);
            a.Rows[0][Value] = "50";
            beta.Rows[0][Value] = "2";

            Assert.False(await setup.Controller.ConfirmCloseAsync([a, beta]));

            Assert.Equal(["Alpha: db/x_tables/a.tsv", "Beta: db/x_tables/a.tsv"], Assert.Single(setup.Dialogs.UnsavedAsks));
        }
    }

    [Fact] public async Task The_names_in_the_question_are_escaped()
    {
        var (setup, _, _) = await TwoTablesAsync();
        using (setup)
        {
            var source = setup.Source;
            var file = @"db\x_tables\a" + (char)7 + ".tsv";
            var document = RowsDocument.ForLoc("text/db/a.loc", [new LocRow("k", "Text", false)]);
            var tab = new EditableRowsTabViewModel(source, file.Replace('\\', '/'), setup.Game.Texts, _ => { }, document, setup.History);
            setup.Tabs.Tabs.Add(tab);
            tab.Rows[0][0] = "other";

            Assert.False(await setup.Controller.ConfirmCloseAsync([tab]));

            var name = Assert.Single(Assert.Single(setup.Dialogs.UnsavedAsks));
            Assert.Equal("db/x_tables/a\\u0007.tsv", name);
        }
    }

    [Fact] public async Task Confirm_before_a_build_asks_only_for_the_tabs_of_that_project()
    {
        var (setup, a, b) = await TwoTablesAsync();
        using (setup)
        {
            var other = setup.Game.NewProject(setup.Projects, "Beta", Table(FileA, 1));
            var opened = await setup.Projects.OpenFileAsync(other.Id, FileA);
            setup.Tabs.Add(TabSource.ForProject(other.Id, "Beta"), opened!, setup.Workspace.Schema);
            var beta = Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.Tabs[^1]);
            var originalA = setup.Read(FileA);

            // Nothing modified in the project: no question.
            Assert.True(await setup.Controller.ConfirmSaveBeforeBuildAsync(setup.Project.Id));
            Assert.Empty(setup.Dialogs.BuildAsks);

            a.Rows[0][Value] = "50";
            beta.Rows[0][Value] = "2";

            // No: nothing is written, the build must not start.
            Assert.False(await setup.Controller.ConfirmSaveBeforeBuildAsync(setup.Project.Id));
            Assert.Equal(["db/x_tables/a.tsv"], Assert.Single(setup.Dialogs.BuildAsks));
            Assert.Equal(originalA, setup.Read(FileA));
            Assert.True(a.IsModified);

            // Yes: the tabs of this project are saved, those of the other project are not.
            setup.Dialogs.SaveAllAnswer = true;
            Assert.True(await setup.Controller.ConfirmSaveBeforeBuildAsync(setup.Project.Id));
            Assert.False(a.IsModified);
            Assert.True(beta.IsModified);
            Assert.Equal(a.Document.ToText().Text, setup.Read(FileA));
            Assert.False(b.IsModified);
        }
    }

    [Fact] public async Task Confirm_before_a_build_is_false_when_the_save_fails()
    {
        var (setup, a, _) = await TwoTablesAsync();
        using (setup)
        {
            a.Rows[0][Value] = "50";
            setup.Dialogs.SaveAllAnswer = true;

            bool proceed;
            using (ReadOnly(setup.PathOf(FileA)))
            {
                proceed = await setup.Controller.ConfirmSaveBeforeBuildAsync(setup.Project.Id);
            }

            Assert.False(proceed);
            Assert.True(a.IsModified);
            Assert.Equal(setup.Game.Texts.SaveFailedTitle, Assert.Single(setup.Dialogs.Messages).Title);
        }
    }
}

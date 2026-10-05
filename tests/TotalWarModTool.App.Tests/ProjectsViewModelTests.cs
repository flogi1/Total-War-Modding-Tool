using System.Text;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Settings;
using TotalWarModTool.Storage;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ProjectsViewModelTests
{
    // One row of the table "x_tables" of the test schema, in the text form of a project.
    private const string Tsv = "value\n#x_tables;0;db/x_tables/my_mod\n42\n";

    private const string Backslash = "\\";

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, ProjectsController Projects, FakeProjectDialogs Dialogs,
        ProjectsViewModel ViewModel) : IDisposable
    {
        public AppTexts Texts => Game.Texts;

        public void Dispose() => Game.Dispose();
    }

    // The view model over a loaded TestGame, as the app wires it; the dialogs are the fake.
    private static async Task<Setup> CreateAsync(Action<TestGame>? arrange = null)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var workspace = game.NewController();
        await workspace.LoadAsync();
        return Wire(game, workspace);
    }

    private static Setup Wire(TestGame game, WorkspaceController workspace)
    {
        var projects = game.NewProjects(workspace);
        var dialogs = new FakeProjectDialogs();
        return new Setup(game, workspace, projects, dialogs, new ProjectsViewModel(projects, dialogs, game.Texts));
    }

    private static void Put(string projectFolder, string relativeFile, byte[] bytes)
    {
        var path = Path.Combine(projectFolder, "pack", relativeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    // A project "My Mod" (pack my_mod.pack) with one valid table file, created through the controller; it becomes the active project.
    private static ProjectState NewProject(Setup setup, string folderName = "mod")
    {
        var folder = setup.Game.NewFolder(folderName);
        Assert.True(setup.Projects.Create(folder, "My Mod", "my_mod.pack").Ok);
        var state = setup.Projects.Projects[^1];
        Put(state.Folder, @"db\x_tables\my_mod.tsv", new UTF8Encoding(false).GetBytes(Tsv));
        return state;
    }

    // A pack with a table, a loc file and a script, plus an entry that leaves the project folder and one with a control character in its
    // path (both skipped).
    private static string SourcePack(TestGame game) => new PackBuilder()
        .AddEntry(@"db\x_tables\my_mod", TestGame.TableBytes)
        .AddEntry(@"text\db\my_mod.loc", LocEncoder.Encode([new LocRow("k", "Text", false)]))
        .AddEntry(@"script\a.lua", new UTF8Encoding(false).GetBytes("print(1)"))
        .AddEntry(@"..\..\escape.txt", [1])
        .AddEntry(@"script\bad" + (char)7 + ".lua", [1])
        .WriteTo(game.Root.File("source.pack"));

    // ---- new project, open project -----------------------------------------------------------------------------------------------

    [Fact] public async Task New_project_asks_validates_and_creates()
    {
        using var setup = await CreateAsync(game => game.AddVanillaPack("data.pack"));
        var folder = setup.Game.Root.File("new-mod");
        setup.Dialogs.ProjectAnswer = new ProjectInput("My Mod", folder, "my_mod.pack");
        var changed = new List<string?>(); setup.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        setup.ViewModel.NewProjectCommand.Execute(null);

        var state = Assert.Single(setup.Projects.Projects);
        Assert.Equal(("My Mod", folder), (state.DisplayName, state.Folder));
        Assert.Equal(setup.Texts.ProjectCreated("My Mod"), setup.ViewModel.StatusMessage);
        Assert.Contains(nameof(ProjectsViewModel.StatusMessage), changed);
        Assert.Empty(setup.Dialogs.Messages);

        var ask = Assert.Single(setup.Dialogs.Asks);
        var other = setup.Game.Root.File("another");          // the folder of the new project is in the list now
        Assert.Equal((setup.Texts.ProjectNewTitle, new ProjectInput("", "", "")), (ask.Title, ask.Defaults));
        Assert.Empty(ask.Info);
        Assert.NotNull(ask.Validate(new ProjectInput("", other, "x.pack")));
        Assert.NotNull(ask.Validate(new ProjectInput("  ", other, "x.pack")));
        Assert.Equal(setup.Texts.ProjectFolderNotAbsolute, ask.Validate(new ProjectInput("N", @"relative\x", "x.pack")));
        Assert.Equal(setup.Texts.ProjectFolderNotAbsolute, ask.Validate(new ProjectInput("N", "", "x.pack")));
        var vanilla = ask.Validate(new ProjectInput("N", other, "DATA.pack"));          // the name of a vanilla pack, whatever the case
        Assert.NotNull(vanilla);
        Assert.Equal(setup.Projects.CheckPackName("DATA.pack"), vanilla);
        Assert.Equal(setup.Projects.CheckPackName("data"), ask.Validate(new ProjectInput("N", other, "data")));
        Assert.Null(ask.Validate(new ProjectInput("My Mod", other, "my_mod.pack")));
    }

    [Fact] public async Task A_project_the_controller_refuses_is_shown_as_a_message_and_asked_again_with_the_input()
    {
        using var setup = await CreateAsync();
        var busy = setup.Game.NewFolder("busy");
        File.WriteAllText(Path.Combine(busy, "x.txt"), "x");
        var refused = new ProjectInput("N", busy, "n.pack");
        setup.Dialogs.ProjectAnswers.Enqueue(refused);          // then the queue is empty and the answer is a cancel

        setup.ViewModel.NewProjectCommand.Execute(null);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.StartsWith(setup.Texts.ProjectProblemText(ProjectProblem.FolderNotEmpty), message);
        Assert.Equal([new ProjectInput("", "", ""), refused], setup.Dialogs.Asks.Select(ask => ask.Defaults));
        Assert.Empty(setup.Projects.Projects);
        Assert.Null(setup.ViewModel.StatusMessage);

        // The second attempt with another folder succeeds and ends the command.
        setup.Dialogs.Messages.Clear(); setup.Dialogs.Asks.Clear();
        setup.Dialogs.ProjectAnswers.Enqueue(refused);
        setup.Dialogs.ProjectAnswers.Enqueue(refused with { Folder = setup.Game.Root.File("fresh") });

        setup.ViewModel.NewProjectCommand.Execute(null);

        Assert.Equal(2, setup.Dialogs.Asks.Count);
        Assert.Single(setup.Dialogs.Messages);
        Assert.Equal("N", Assert.Single(setup.Projects.Projects).DisplayName);
        Assert.Equal(setup.Texts.ProjectCreated("N"), setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task The_dialog_validation_refuses_what_the_controller_would_refuse()
    {
        using var setup = await CreateAsync();
        var existing = NewProject(setup);
        setup.Dialogs.ProjectAnswers.Enqueue(null);
        setup.ViewModel.NewProjectCommand.Execute(null);
        var validate = Assert.Single(setup.Dialogs.Asks).Validate;
        var other = setup.Game.Root.File("other");

        Assert.Null(validate(new ProjectInput("Mod", other, "mod.pack")));

        // A folder of the list is refused whatever the case or a trailing separator.
        var listed = setup.Texts.ProjectAlreadyInList(existing.Folder);
        Assert.Equal(listed, validate(new ProjectInput("Mod", existing.Folder, "mod.pack")));
        Assert.Equal(listed, validate(new ProjectInput("Mod", existing.Folder.ToUpperInvariant() + Path.DirectorySeparatorChar, "mod.pack")));

        // A name the controller would refuse comes back with the controller's own text.
        Assert.Equal(setup.Texts.ProjectNameInvalid, validate(new ProjectInput("bad" + (char)0xD800, other, "mod.pack")));
        Assert.Equal(setup.Texts.ProjectNameEmpty, validate(new ProjectInput(" ", other, "mod.pack")));
    }

    [Fact] public async Task A_cancelled_dialog_changes_nothing()
    {
        using var setup = await CreateAsync();
        var changes = 0; setup.Projects.Changed += (_, _) => changes++;

        setup.ViewModel.NewProjectCommand.Execute(null);          // no answer prepared: the user cancels
        setup.ViewModel.OpenProjectCommand.Execute(null);
        setup.ViewModel.ImportPackCommand.Execute(null);

        Assert.Single(setup.Dialogs.Asks);
        Assert.Equal([setup.Texts.ProjectOpenTitle], setup.Dialogs.FolderTitles);
        Assert.Equal(1, setup.Dialogs.PackFileCalls);
        Assert.Empty(setup.Projects.Projects);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Empty(setup.Game.Runner.Titles);
        Assert.Equal((0, (string?)null), (changes, setup.ViewModel.StatusMessage));

        // A cancelled import dialog after a readable pack: the pack was read, nothing was imported.
        await setup.ViewModel.ImportAsync(SourcePack(setup.Game), "source.pack");

        Assert.Equal(setup.Texts.OperationReadPack("source.pack"), Assert.Single(setup.Game.Runner.Titles));
        Assert.Empty(setup.Projects.Projects);
    }

    [Fact] public async Task The_status_message_is_cleared_when_a_command_starts()
    {
        using var setup = await CreateAsync();
        setup.Dialogs.ProjectAnswer = new ProjectInput("My Mod", setup.Game.Root.File("a"), "my_mod.pack");
        setup.ViewModel.NewProjectCommand.Execute(null);
        Assert.NotNull(setup.ViewModel.StatusMessage);
        var changed = new List<string?>(); setup.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        setup.Dialogs.ProjectAnswer = null;
        setup.ViewModel.NewProjectCommand.Execute(null);

        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.Contains(nameof(ProjectsViewModel.StatusMessage), changed);
    }

    [Fact] public async Task Open_project_shows_a_message_for_a_broken_folder()
    {
        using var setup = await CreateAsync();
        setup.Dialogs.FolderAnswers.Enqueue(setup.Game.NewFolder("empty"));          // no project file in it

        setup.ViewModel.OpenProjectCommand.Execute(null);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ProjectMessageTitle, title);
        Assert.StartsWith(setup.Texts.ProjectProblemText(ProjectProblem.NotFound), message);
        Assert.Empty(setup.Projects.Projects);
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task Open_project_adds_an_existing_project()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        setup.ViewModel.Remove(state.Id);
        Assert.Empty(setup.Projects.Projects);
        setup.Dialogs.FolderAnswers.Enqueue(state.Folder);

        setup.ViewModel.OpenProjectCommand.Execute(null);

        Assert.Equal("My Mod", Assert.Single(setup.Projects.Projects).DisplayName);
        Assert.Equal(setup.Texts.ProjectAdded("My Mod"), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);
    }

    // ---- import ----------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Import_shows_the_preview_and_reports_entries_that_stayed_binary_or_were_skipped()
    {
        using var setup = await CreateAsync(game => game.AddVanillaPack("data.pack"));
        var source = SourcePack(setup.Game);
        var skipped = (await setup.Projects.PreviewImportAsync(source))!.Preview!.Skipped;
        Assert.Equal(2, skipped.Count);
        var target = Path.Combine(setup.Game.NewFolder("parent"), "imported");
        setup.Dialogs.ProjectAnswer = new ProjectInput("Imported", target, "imported.pack");

        await setup.ViewModel.ImportAsync(source, "source.pack");

        var ask = Assert.Single(setup.Dialogs.Asks);
        var other = setup.Game.Root.File("another");          // the folder of the new project is in the list now
        Assert.Equal(setup.Texts.ImportTitle("source.pack"), ask.Title);
        Assert.Equal(new ProjectInput("source", "", "source.pack"), ask.Defaults);
        Assert.Contains(setup.Texts.ImportPreviewLineTables(1), ask.Info);
        Assert.Contains(setup.Texts.ImportPreviewLineLoc(1), ask.Info);
        Assert.Contains(setup.Texts.ImportPreviewLineOther(1), ask.Info);
        Assert.Contains(setup.Texts.ImportPreviewLineSkipped(2), ask.Info);
        Assert.Contains(ask.Info, line => line.StartsWith(setup.Texts.ImportPreviewLineSize(""), StringComparison.Ordinal));
        Assert.Equal(setup.Texts.ProjectFolderNotAbsolute, ask.Validate(new ProjectInput("Imported", "", "imported.pack")));
        Assert.Null(ask.Validate(new ProjectInput("Imported", setup.Game.Root.File("another"), "imported.pack")));

        Assert.Equal("Imported", Assert.Single(setup.Projects.Projects).DisplayName);
        Assert.Contains(setup.Texts.ImportSummary(1, 1, 1), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);

        var report = Assert.Single(setup.Dialogs.Reports);
        Assert.Equal(setup.Texts.ImportReportTitle, report.Title);
        Assert.Equal(setup.ViewModel.StatusMessage, report.Summary);
        var section = Assert.Single(report.Sections);
        Assert.Equal(setup.Texts.ImportReportSkippedHeading, section.Heading);
        Assert.Equal(2, section.Lines.Count);
        foreach (var entry in skipped)
        {
            Assert.Contains(section.Lines, line => line.StartsWith(DisplayText.Safe(entry.PackPath) + ": ", StringComparison.Ordinal));
        }

        Assert.Contains(section.Lines, line => line.StartsWith("script/bad" + Backslash + "u0007.lua: ", StringComparison.Ordinal));
        Assert.DoesNotContain(section.Lines, line => line.Any(char.IsControl));
    }

    [Fact] public async Task The_import_report_lists_tables_that_stayed_binary_and_dropped_dependencies()
    {
        using var setup = await CreateAsync();
        var source = new PackBuilder()
            .AddEntry(@"db\unknown_tables\t", [1, 2, 3])          // a table the schema does not know: kept as a binary file
            .AddDependency("   ")                                  // a blank dependency name: not taken over
            .WriteTo(setup.Game.Root.File("odd.pack"));
        setup.Dialogs.ProjectAnswer = new ProjectInput("Odd", setup.Game.Root.File("odd"), "odd.pack");

        await setup.ViewModel.ImportAsync(source, "odd.pack");

        var report = Assert.Single(setup.Dialogs.Reports);
        Assert.Equal([setup.Texts.ImportReportBinaryHeading, setup.Texts.ImportReportDroppedHeading], report.Sections.Select(section => section.Heading));
        Assert.StartsWith("db/unknown_tables/t: ", Assert.Single(report.Sections[0].Lines));          // pack paths are written with slashes
        Assert.Single(report.Sections[1].Lines);
    }

    [Fact] public async Task An_import_without_notable_entries_shows_no_report()
    {
        using var setup = await CreateAsync();
        var source = new PackBuilder().AddEntry(@"script\a.lua", new UTF8Encoding(false).GetBytes("print(1)")).WriteTo(setup.Game.Root.File("plain.pack"));
        setup.Dialogs.ProjectAnswer = new ProjectInput("Plain", setup.Game.Root.File("plain"), "plain.pack");

        await setup.ViewModel.ImportAsync(source, "plain.pack");

        Assert.Single(setup.Projects.Projects);
        Assert.Contains(setup.Texts.ImportSummary(0, 0, 1), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Reports);
    }

    [Fact] public async Task The_import_command_asks_for_a_pack_file_and_names_the_dialog_after_it()
    {
        using var setup = await CreateAsync();
        var source = SourcePack(setup.Game);
        setup.Dialogs.PackFileAnswers.Enqueue(source);
        setup.Dialogs.ProjectAnswer = new ProjectInput("Imported", setup.Game.Root.File("imported"), "imported.pack");

        setup.ViewModel.ImportPackCommand.Execute(null);

        Assert.Equal(setup.Texts.ImportTitle("source.pack"), Assert.Single(setup.Dialogs.Asks).Title);
        Assert.Single(setup.Projects.Projects);
    }

    [Fact] public async Task An_unreadable_pack_or_a_refused_import_is_shown_as_a_message()
    {
        using var setup = await CreateAsync();
        var busy = setup.Game.NewFolder("busy");
        File.WriteAllText(Path.Combine(busy, "x.txt"), "x");

        await setup.ViewModel.ImportAsync(@"relative.pack", "relative.pack");
        await setup.ViewModel.ImportAsync(setup.Game.Root.File("missing.pack"), "missing.pack");

        Assert.Empty(setup.Dialogs.Asks);
        Assert.Equal(2, setup.Dialogs.Messages.Count);
        Assert.All(setup.Dialogs.Messages, message => Assert.Equal(setup.Texts.ImportMessageTitle, message.Title));
        Assert.Equal(setup.Texts.ProjectPackPathNotAbsolute, setup.Dialogs.Messages[0].Message);

        // A refusal after the dialog: the message, then the dialog again with the input of the user; the cancel there ends the command.
        setup.Dialogs.Messages.Clear();
        var refused = new ProjectInput("N", busy, "n.pack");
        setup.Dialogs.ProjectAnswers.Enqueue(refused);
        await setup.ViewModel.ImportAsync(SourcePack(setup.Game), "source.pack");

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ImportMessageTitle, title);
        Assert.StartsWith(setup.Texts.ImportProblemText(ImportProblem.TargetNotEmpty), message);
        Assert.Equal([new ProjectInput("source", "", "source.pack"), refused], setup.Dialogs.Asks.Select(ask => ask.Defaults));
        Assert.Empty(setup.Projects.Projects);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Null(setup.ViewModel.StatusMessage);

        // The second attempt with another folder imports.
        setup.Dialogs.Messages.Clear(); setup.Dialogs.Asks.Clear();
        setup.Dialogs.ProjectAnswers.Enqueue(refused);
        setup.Dialogs.ProjectAnswers.Enqueue(refused with { Folder = setup.Game.Root.File("imported") });
        await setup.ViewModel.ImportAsync(SourcePack(setup.Game), "source.pack");

        Assert.Equal(2, setup.Dialogs.Asks.Count);
        Assert.Equal("N", Assert.Single(setup.Projects.Projects).DisplayName);
        Assert.Single(setup.Dialogs.Messages);
    }

    [Fact] public async Task A_cancelled_import_run_shows_its_message_and_does_not_ask_again()
    {
        using var setup = await CreateAsync();
        var target = setup.Game.Root.File("cancelled");
        setup.Dialogs.ProjectAnswers.Enqueue(new ProjectInput("N", target, "n.pack"));
        setup.Dialogs.WhileAsking = () => setup.Game.Runner.CancelNext = true;          // the preview has run; the import run is cancelled

        await setup.ViewModel.ImportAsync(SourcePack(setup.Game), "source.pack");

        Assert.Single(setup.Dialogs.Asks);
        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal((setup.Texts.ImportMessageTitle, setup.Texts.ImportCancelled), (title, message));
        Assert.Empty(setup.Projects.Projects);
        Assert.Empty(setup.Dialogs.Reports);
    }

    [Fact] public async Task A_report_section_shows_at_most_1000_lines_and_says_how_many_are_missing()
    {
        using var setup = await CreateAsync();
        var builder = new PackBuilder();
        for (var i = 0; i < 1005; i++)
        {
            builder.AddEntry(@"script\bad" + i + (char)7 + ".lua", [1]);          // every one is skipped: a control character in the path
        }

        var source = builder.WriteTo(setup.Game.Root.File("many.pack"));
        setup.Dialogs.ProjectAnswers.Enqueue(new ProjectInput("Many", setup.Game.Root.File("many"), "many.pack"));

        await setup.ViewModel.ImportAsync(source, "many.pack");

        var report = Assert.Single(setup.Dialogs.Reports);
        var lines = Assert.Single(report.Sections).Lines;
        Assert.Equal(1001, lines.Count);
        Assert.Equal(setup.Texts.ReportMoreLines(5), lines[^1]);
        Assert.Contains(setup.Texts.ImportSkipped(1005), report.Summary);          // the summary keeps the full number
    }

    [Fact] public void A_hostile_path_is_shown_escaped()
    {
        var hostile = "a" + (char)7 + "b" + (char)0xD800;
        var expected = string.Concat("a", Backslash, "u0007b", Backslash, "uD800");

        Assert.Equal(expected, DisplayText.Safe(hostile));
        Assert.Equal(string.Concat("x", Backslash, "u000A", Backslash, "u007Fy", Backslash, "uDC00"), DisplayText.Safe("x\n" + (char)0x7F + "y" + (char)0xDC00));

        // Well-formed text stays as it is, including a surrogate pair and a path.
        var smile = char.ConvertFromUtf32(0x1F600);
        Assert.Equal(@"db\tables\" + smile + ".tsv", DisplayText.Safe(@"db\tables\" + smile + ".tsv"));
        Assert.Equal(string.Empty, DisplayText.Safe(string.Empty));

        // Long text is cut after maxChars characters (counted before the escaping) and marked.
        Assert.Equal(new string('a', 300) + " (shortened)", DisplayText.Safe(new string('a', 1000)));
        Assert.Equal(new string('a', 300), DisplayText.Safe(new string('a', 300)));
        Assert.Equal("abc (shortened)", DisplayText.Safe("abcdef", 3));
        Assert.Equal(string.Concat(Backslash, "u0007", Backslash, "u0007 (shortened)"), DisplayText.Safe("\a\a\a\a", 2));

        // A pair is never split by the cut.
        Assert.Equal("a (shortened)", DisplayText.Safe("a" + smile, 2));
        Assert.Equal("a" + smile + " (shortened)", DisplayText.Safe("a" + smile + "b", 3));

        // The marker is a parameter, so that the UI can give the text of its language.
        Assert.Equal("abc [gekuerzt]", DisplayText.Safe("abcdef", 3, " [gekuerzt]"));
    }

    [Fact] public void Characters_that_disguise_or_break_a_line_are_escaped_and_other_format_characters_stay()
    {
        // Right-to-left override (U+202E) would show "gpj.exe" for "exe.jpg"; U+2028 and U+2029 break the line.
        var disguised = "exe" + (char)0x202E + "gpj.txt";
        Assert.Equal(string.Concat("exe", Backslash, "u202Egpj.txt"), DisplayText.Safe(disguised));
        Assert.Equal(string.Concat("a", Backslash, "u2028b", Backslash, "u2029c"), DisplayText.Safe("a" + (char)0x2028 + "b" + (char)0x2029 + "c"));

        foreach (var code in new[] { 0x061C, 0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x2066, 0x2067, 0x2068, 0x2069, 0x2028, 0x2029 })
        {
            Assert.Equal(string.Concat("x", Backslash, "u", code.ToString("X4"), "y"), DisplayText.Safe("x" + (char)code + "y"));
        }

        // Zero width joiner and non-joiner are needed by real scripts and stay; so do the neighbours of the escaped ranges.
        var kept = "a" + (char)0x200D + "b" + (char)0x200C + "c" + (char)0x2065 + (char)0x206A + (char)0x2027 + (char)0x202F;
        Assert.Equal(kept, DisplayText.Safe(kept));

        // The escape rule of the older helper is the same one.
        Assert.Equal(string.Concat("exe", Backslash, "u202Egpj"), MessageText.EscapeControlCharacters("exe" + (char)0x202E + "gpj"));
    }

    [Fact] public async Task Foreign_text_is_cut_with_the_marker_of_the_current_language()
    {
        using var setup = await CreateAsync();
        var ui = new TotalWarModTool.Localization.UiText(Path.Combine(RepoRoot.Find(), "locales"));
        ui.Load("de");
        var german = new AppTexts(ui);
        var viewModel = new ProjectsViewModel(setup.Projects, setup.Dialogs, german);
        var source = new PackBuilder().AddEntry(@"script\a" + (char)7 + new string('b', 400) + ".lua", [1]).WriteTo(setup.Game.Root.File("long.pack"));
        setup.Dialogs.ProjectAnswers.Enqueue(new ProjectInput("Long", setup.Game.Root.File("long"), "long.pack"));

        await viewModel.ImportAsync(source, "long.pack");

        var line = Assert.Single(Assert.Single(Assert.Single(setup.Dialogs.Reports).Sections).Lines);
        Assert.NotEqual("shortened", german.TextShortened);
        Assert.StartsWith("script/a" + Backslash + "u0007bbb", line);
        Assert.Contains(" (" + german.TextShortened + "): ", line);          // the cut is marked, then the reason follows
    }

    // ---- build -----------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Build_problems_open_a_report_with_file_and_line()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        Put(state.Folder, @"db\x_tables\my_mod.tsv", Encoding.UTF8.GetBytes(Tsv.Replace("42", "many")));
        Put(state.Folder, @"db\x_tables\.~lock.my_mod.tsv#", [1]);
        var problem = (await setup.Projects.BuildAsync(state.Id, install: false))!.Problems[0];

        await setup.ViewModel.BuildAsync(state.Id, install: false);

        var report = Assert.Single(setup.Dialogs.Reports);
        Assert.Equal(setup.Texts.BuildReportTitle, report.Title);
        Assert.Equal(setup.Texts.BuildProblemsSummary(1), report.Summary);
        Assert.Equal([setup.Texts.BuildProblemsHeading, setup.Texts.BuildIgnoredHeading], report.Sections.Select(section => section.Heading));
        var line = Assert.Single(report.Sections[0].Lines);
        Assert.Equal(3, problem.Line);
        Assert.Equal(@"db\x_tables\my_mod.tsv(3): " + DisplayText.Safe(problem.Detail), line);
        Assert.Equal([@"db\x_tables\.~lock.my_mod.tsv#"], report.Sections[1].Lines);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.False(Directory.Exists(Path.Combine(state.Folder, "build")));
    }

    [Fact] public async Task A_successful_build_only_sets_the_status_message()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);

        await setup.ViewModel.BuildAsync(state.Id, install: false);

        Assert.Equal(setup.Texts.BuildDone("my_mod.pack", 1), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Empty(setup.Dialogs.Messages);

        await setup.ViewModel.BuildAsync(state.Id, install: true);

        Assert.Equal(setup.Texts.BuildInstalled("my_mod.pack"), setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.True(File.Exists(Path.Combine(setup.Game.DataPath, "my_mod.pack")));
    }

    [Fact] public async Task A_build_that_could_not_be_done_is_shown_as_a_message()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        Directory.Delete(state.Folder, recursive: true);
        setup.Projects.Reload();

        await setup.ViewModel.BuildAsync(state.Id, install: false);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.BuildMessageTitle, title);
        Assert.StartsWith(setup.Texts.ProjectProblemText(ProjectProblem.NotFound), message);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task An_install_that_was_refused_is_shown_as_a_message_too()
    {
        using var setup = await CreateAsync(game => game.AddPack("my_mod.pack", ("a.txt", [9, 9])));          // a foreign file with the name of the pack
        var state = NewProject(setup);

        await setup.ViewModel.BuildAsync(state.Id, install: true);

        var message = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.BuildMessageTitle, message.Title);
        Assert.StartsWith(setup.Texts.InstallProblemText(InstallProblem.ForeignFile), message.Message);
        Assert.Equal(message.Message, setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task Nothing_is_shown_when_no_operation_could_start()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        await setup.ViewModel.BuildAsync(state.Id, install: true);          // so that there is something to uninstall
        var runs = setup.Game.Runner.Titles.Count;
        setup.Game.Runner.IsRunning = true;

        await setup.ViewModel.BuildAsync(state.Id, install: false);
        await setup.ViewModel.UninstallAsync(state.Id);
        await setup.ViewModel.ImportAsync(SourcePack(setup.Game), "source.pack");

        Assert.Null(setup.ViewModel.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.Reports);
        Assert.Empty(setup.Dialogs.Asks);
        Assert.Equal(runs, setup.Game.Runner.Titles.Count);
    }

    // ---- uninstall, set active, remove -------------------------------------------------------------------------------------------

    [Fact] public async Task Uninstall_removes_the_pack_and_says_so()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);

        await setup.ViewModel.UninstallAsync(state.Id);

        Assert.Equal(setup.Texts.UninstallNothing, Assert.Single(setup.Dialogs.Messages).Message);
        Assert.Equal(setup.Texts.UninstallMessageTitle, setup.Dialogs.Messages[0].Title);

        await setup.ViewModel.BuildAsync(state.Id, install: true);
        Assert.NotNull(setup.Projects.Projects[0].Installed);
        await setup.ViewModel.UninstallAsync(state.Id);

        Assert.Null(setup.Projects.Projects[0].Installed);
        Assert.False(File.Exists(Path.Combine(setup.Game.DataPath, "my_mod.pack")));
        Assert.Equal(setup.Texts.UninstallDone("My Mod"), setup.ViewModel.StatusMessage);
        Assert.Single(setup.Dialogs.Messages);
    }

    [Fact] public async Task Set_active_and_remove_change_the_list_without_asking()
    {
        using var setup = await CreateAsync();
        var first = NewProject(setup, "one");
        var second = NewProject(setup, "two");
        Assert.Equal(first.Id, setup.Projects.ActiveId);

        setup.ViewModel.SetActive(second.Id);

        Assert.Equal(second.Id, setup.Projects.ActiveId);

        setup.ViewModel.Remove(second.Id);

        Assert.Equal([first.Id], setup.Projects.Projects.Select(state => state.Id));
        Assert.True(Directory.Exists(second.Folder));          // the folder stays
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.Asks);

        setup.ViewModel.Remove(Guid.NewGuid());
        setup.ViewModel.SetActive(Guid.NewGuid());

        Assert.Equal(2, setup.Dialogs.Messages.Count);
        Assert.All(setup.Dialogs.Messages, message => Assert.Equal((setup.Texts.ProjectMessageTitle, setup.Texts.ProjectUnknown), message));
    }

    // ---- disabled commands -------------------------------------------------------------------------------------------------------

    [Fact] public async Task Commands_for_the_active_project_are_disabled_with_a_reason()
    {
        using var setup = await CreateAsync();
        var vm = setup.ViewModel;
        var raised = 0; ((RelayCommand)vm.BuildActiveCommand).CanExecuteChanged += (_, _) => raised++;

        Assert.Equal(setup.Texts.ProjectNoneActive, vm.BuildDisabledReason(null));
        Assert.Equal(setup.Texts.ProjectNoneActive, vm.InstallDisabledReason(null));
        Assert.False(vm.BuildActiveCommand.CanExecute(null));
        Assert.False(vm.BuildAndInstallActiveCommand.CanExecute(null));
        Assert.Equal(setup.Texts.ProjectUnknown, vm.BuildDisabledReason(Guid.NewGuid()));
        Assert.Equal(setup.Texts.ProjectUnknown, vm.UninstallDisabledReason(Guid.NewGuid()));

        var state = NewProject(setup);

        Assert.True(raised > 0);              // the list changed: the commands are asked again
        Assert.Null(vm.BuildDisabledReason(state.Id));
        Assert.Null(vm.InstallDisabledReason(state.Id));
        Assert.Equal(setup.Texts.UninstallNothing, vm.UninstallDisabledReason(state.Id));
        Assert.True(vm.BuildActiveCommand.CanExecute(null));
        Assert.True(vm.BuildAndInstallActiveCommand.CanExecute(null));

        // A running operation disables the commands without giving a reason: the progress window already shows what runs.
        setup.Game.Runner.IsRunning = true;
        Assert.Null(vm.BuildDisabledReason(state.Id));
        Assert.False(vm.BuildActiveCommand.CanExecute(null));
        Assert.False(vm.BuildAndInstallActiveCommand.CanExecute(null));
        setup.Game.Runner.IsRunning = false;

        // After an install there is something to uninstall.
        await vm.BuildAsync(state.Id, install: true);
        Assert.Null(vm.UninstallDisabledReason(state.Id));

        // A project that cannot be used says why.
        Directory.Delete(state.Folder, recursive: true);
        setup.Projects.Reload();
        var problem = setup.Texts.ProjectProblemText(ProjectProblem.NotFound);
        Assert.Equal(problem, vm.BuildDisabledReason(state.Id));
        Assert.Equal(problem, vm.InstallDisabledReason(state.Id));
        Assert.False(vm.BuildActiveCommand.CanExecute(null));
        Assert.False(vm.BuildAndInstallActiveCommand.CanExecute(null));

        // The installed pack can still be removed: it is removed by the record of the list, not by the project file.
        Assert.Null(vm.UninstallDisabledReason(state.Id));
    }

    [Fact] public async Task Installing_is_disabled_without_a_game_folder()
    {
        using var game = new TestGame();
        var workspace = new WorkspaceController(TestGame.Profile, new GameSettings(), new GlobalSettings(), game.Paths, new NoSteam(), game.Runner,
            game.Log, game.Texts);
        await workspace.LoadAsync();
        var setup = Wire(game, workspace);
        var state = NewProject(setup);

        Assert.Null(setup.ViewModel.BuildDisabledReason(state.Id));
        Assert.Equal(game.Texts.InstallNoGameFolder, setup.ViewModel.InstallDisabledReason(state.Id));
        Assert.Equal(game.Texts.InstallNoGameFolder, setup.ViewModel.UninstallDisabledReason(state.Id));
        Assert.True(setup.ViewModel.BuildActiveCommand.CanExecute(null));
        Assert.False(setup.ViewModel.BuildAndInstallActiveCommand.CanExecute(null));
    }

    [Fact] public async Task The_active_build_commands_build_the_active_project()
    {
        using var setup = await CreateAsync();
        var first = NewProject(setup, "one");
        var second = NewProject(setup, "two");
        setup.ViewModel.SetActive(second.Id);

        setup.ViewModel.BuildAndInstallActiveCommand.Execute(null);

        Assert.Equal(setup.Texts.OperationBuildAndInstall("My Mod"), Assert.Single(setup.Game.Runner.Titles));
        Assert.Equal(setup.Texts.BuildInstalled("my_mod.pack"), setup.ViewModel.StatusMessage);
        Assert.Null(setup.Projects.Projects.Single(state => state.Id == first.Id).Installed);
        Assert.NotNull(setup.Projects.Projects.Single(state => state.Id == second.Id).Installed);

        setup.ViewModel.BuildActiveCommand.Execute(null);

        Assert.Equal(setup.Texts.OperationBuild("My Mod"), setup.Game.Runner.Titles[^1]);
    }

    [Fact] public async Task The_active_build_commands_do_nothing_without_an_active_project()
    {
        using var setup = await CreateAsync();

        setup.ViewModel.BuildActiveCommand.Execute(null);
        setup.ViewModel.BuildAndInstallActiveCommand.Execute(null);

        Assert.Empty(setup.Game.Runner.Titles);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Null(setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task A_command_asks_the_commands_again_when_it_ends()
    {
        using var setup = await CreateAsync();
        NewProject(setup);
        var raised = 0; ((RelayCommand)setup.ViewModel.BuildActiveCommand).CanExecuteChanged += (_, _) => raised++;
        var changes = 0; setup.Projects.Changed += (_, _) => changes++;

        setup.ViewModel.BuildActiveCommand.Execute(null);          // a plain build changes nothing in the list

        Assert.Equal(0, changes);
        Assert.True(raised > 0);
        Assert.Equal(setup.Texts.BuildDone("my_mod.pack", 1), setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task The_commands_are_asked_again_when_the_workspace_changes()
    {
        using var game = new TestGame();
        var workspace = game.NewController();          // not loaded yet: the silent start-up load comes later
        var setup = Wire(game, workspace);
        var raised = new Dictionary<string, int>();
        foreach (var (name, command) in new[]
        {
            ("build", setup.ViewModel.BuildActiveCommand), ("install", setup.ViewModel.BuildAndInstallActiveCommand),
            ("new", setup.ViewModel.NewProjectCommand), ("open", setup.ViewModel.OpenProjectCommand), ("import", setup.ViewModel.ImportPackCommand),
        })
        {
            raised[name] = 0;
            ((RelayCommand)command).CanExecuteChanged += (_, _) => raised[name]++;
        }

        await workspace.LoadAsync();

        Assert.All(raised, pair => Assert.True(pair.Value > 0, pair.Key));
    }

    [Fact] public async Task Projects_are_disabled_without_a_game_profile()
    {
        using var game = new TestGame();
        var workspace = game.NewController();
        var projects = new ProjectsController(null, new ProjectListLoadResult(ProjectList.Empty, LoadStatus.Missing, null), workspace,
            game.Runner, game.Log, game.Texts, TimeProvider.System);
        var dialogs = new FakeProjectDialogs { ProjectAnswer = new ProjectInput("N", game.Root.File("n"), "n.pack") };
        dialogs.FolderAnswers.Enqueue(game.NewFolder("e"));
        dialogs.PackFileAnswers.Enqueue(game.Root.File("x.pack"));
        var viewModel = new ProjectsViewModel(projects, dialogs, game.Texts);

        Assert.Equal(game.Texts.ProjectNoGameProfile, viewModel.ProjectsDisabledReason);
        Assert.False(viewModel.NewProjectCommand.CanExecute(null));
        Assert.False(viewModel.OpenProjectCommand.CanExecute(null));
        Assert.False(viewModel.ImportPackCommand.CanExecute(null));

        await viewModel.ImportAsync(game.Root.File("x.pack"), "x.pack");

        Assert.Equal(game.Texts.ProjectNoGameProfile, viewModel.StatusMessage);
        Assert.Empty(dialogs.Asks);
        Assert.Empty(dialogs.Messages);
        Assert.Empty(game.Runner.Titles);

        // Even when a command is invoked anyway, nothing is asked.
        viewModel.NewProjectCommand.Execute(null);
        viewModel.OpenProjectCommand.Execute(null);
        viewModel.ImportPackCommand.Execute(null);

        Assert.Empty(dialogs.Asks);
        Assert.Empty(dialogs.FolderTitles);
        Assert.Equal(0, dialogs.PackFileCalls);
    }

    [Fact] public async Task Projects_are_enabled_with_a_game_profile()
    {
        using var setup = await CreateAsync();

        Assert.Null(setup.ViewModel.ProjectsDisabledReason);
        Assert.True(setup.ViewModel.NewProjectCommand.CanExecute(null));
        Assert.True(setup.ViewModel.OpenProjectCommand.CanExecute(null));
        Assert.True(setup.ViewModel.ImportPackCommand.CanExecute(null));
    }

    [Fact] public async Task Install_and_uninstall_are_disabled_while_the_list_cannot_be_saved()
    {
        using var game = new TestGame();
        var store = new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{ "schemaVersion": 99, "projects": [] }""");          // a newer list: it is never saved
        var workspace = game.NewController();
        await workspace.LoadAsync();
        var setup = Wire(game, workspace);
        var state = NewProject(setup);

        Assert.Null(setup.ViewModel.BuildDisabledReason(state.Id));
        Assert.Equal(game.Texts.InstallListNotWritable, setup.ViewModel.InstallDisabledReason(state.Id));
        Assert.Equal(game.Texts.InstallListNotWritable, setup.ViewModel.UninstallDisabledReason(state.Id));
        Assert.True(setup.ViewModel.BuildActiveCommand.CanExecute(null));
        Assert.False(setup.ViewModel.BuildAndInstallActiveCommand.CanExecute(null));
    }

    [Fact] public async Task An_install_or_uninstall_whose_record_cannot_be_saved_opens_a_message()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        var games = Path.Combine(setup.Game.Paths.RoamingRoot, "games");
        Directory.Delete(games, recursive: true);
        File.WriteAllText(games, "x");                                          // every later save of the list fails

        await setup.ViewModel.BuildAsync(state.Id, install: true);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.BuildMessageTitle, title);
        Assert.StartsWith(setup.Texts.InstallRecordNotSaved("my_mod.pack") + "\n\n", message);
        Assert.Equal(setup.Texts.BuildInstalled("my_mod.pack") + "\n" + setup.Texts.ProjectListNotSaved, setup.ViewModel.StatusMessage);

        setup.Dialogs.Messages.Clear();
        await setup.ViewModel.UninstallAsync(state.Id);

        (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.UninstallMessageTitle, title);
        Assert.StartsWith(setup.Texts.UninstallRecordNotSaved("my_mod.pack") + "\n\n", message);
        Assert.Equal(setup.Texts.UninstallDone("My Mod") + "\n" + setup.Texts.ProjectListNotSaved, setup.ViewModel.StatusMessage);
    }

    [Fact] public async Task Remove_is_disabled_while_the_project_has_an_installed_pack()
    {
        using var setup = await CreateAsync();
        var vm = setup.ViewModel;
        var state = NewProject(setup);
        Assert.Null(vm.RemoveDisabledReason(state.Id));
        Assert.Equal(setup.Texts.ProjectUnknown, vm.RemoveDisabledReason(Guid.NewGuid()));

        await vm.BuildAsync(state.Id, install: true);
        Assert.Equal(setup.Texts.ProjectRemoveInstalled, vm.RemoveDisabledReason(state.Id));

        vm.Remove(state.Id);
        Assert.Equal((setup.Texts.ProjectMessageTitle, setup.Texts.ProjectRemoveInstalled), Assert.Single(setup.Dialogs.Messages));
        Assert.Single(setup.Projects.Projects);

        await vm.UninstallAsync(state.Id);
        Assert.Null(vm.RemoveDisabledReason(state.Id));
    }

    [Fact] public async Task Remove_is_disabled_with_a_reason_while_the_project_has_unsaved_changes()
    {
        using var setup = await CreateAsync();
        var vm = setup.ViewModel;
        var state = NewProject(setup);
        string? reason = "Save first.";
        vm.UnsavedReason = id => id == state.Id ? reason : null;

        Assert.Equal("Save first.", vm.RemoveDisabledReason(state.Id));
        Assert.Equal(setup.Texts.ProjectUnknown, vm.RemoveDisabledReason(Guid.NewGuid()));       // an unknown project keeps its own reason

        vm.Remove(state.Id);                                                                      // refused with the reason, nothing changes
        Assert.Equal((setup.Texts.ProjectMessageTitle, "Save first."), Assert.Single(setup.Dialogs.Messages));
        Assert.Single(setup.Projects.Projects);

        reason = null;
        Assert.Null(vm.RemoveDisabledReason(state.Id));
        vm.Remove(state.Id);
        Assert.Empty(setup.Projects.Projects);
        Assert.Single(setup.Dialogs.Messages);
    }

    [Fact] public async Task Build_with_unsaved_changes_asks_to_save_all_first()
    {
        using var setup = await CreateAsync();
        var vm = setup.ViewModel;
        var state = NewProject(setup);
        var asked = new List<Guid>(); var proceed = false;
        vm.BeforeBuild = id =>
        {
            asked.Add(id);
            return Task.FromResult(proceed);
        };
        var runs = setup.Game.Runner.Titles.Count;

        // No: nothing is built, and the view model says nothing of its own (the question already told the user).
        await vm.BuildAsync(state.Id, install: false);
        Assert.Equal([state.Id], asked);
        Assert.Equal(runs, setup.Game.Runner.Titles.Count);
        Assert.Null(vm.StatusMessage);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Empty(setup.Dialogs.Reports);

        // Yes: the build runs, also from the command that builds and installs.
        proceed = true;
        await vm.BuildAsync(state.Id, install: false);
        Assert.Equal(2, asked.Count);
        Assert.Equal(setup.Texts.OperationBuild("My Mod"), Assert.Single(setup.Game.Runner.Titles.Skip(runs)));
        Assert.NotNull(vm.StatusMessage);

        vm.BuildAndInstallActiveCommand.Execute(null);
        Assert.Equal(3, asked.Count);
        Assert.Equal(setup.Texts.OperationBuildAndInstall("My Mod"), setup.Game.Runner.Titles[^1]);

        // Without the hook the build starts at once.
        vm.BeforeBuild = null;
        var before = setup.Game.Runner.Titles.Count;
        await vm.BuildAsync(state.Id, install: false);
        Assert.Equal(before + 1, setup.Game.Runner.Titles.Count);
        Assert.Equal(3, asked.Count);
    }

    [Fact] public async Task The_import_command_waits_for_the_start_up_load_and_for_a_running_operation()
    {
        using var game = new TestGame();
        var workspace = game.NewController();
        var setup = Wire(game, workspace);

        var load = workspace.LoadAsync();                                       // returns at its first await: the workspace is busy now
        Assert.True(workspace.IsBusy);
        Assert.False(setup.ViewModel.ImportPackCommand.CanExecute(null));

        await load;
        Assert.True(setup.ViewModel.ImportPackCommand.CanExecute(null));
        game.Runner.IsRunning = true;
        Assert.False(setup.ViewModel.ImportPackCommand.CanExecute(null));
    }

    [Fact] public async Task Building_is_disabled_without_a_schema()
    {
        using var game = new TestGame(withSchema: false);
        var workspace = game.NewController();
        await workspace.LoadAsync();
        var setup = Wire(game, workspace);
        var state = NewProject(setup);

        Assert.Equal(game.Texts.BuildNoSchema, setup.ViewModel.BuildDisabledReason(state.Id));
        Assert.Equal(game.Texts.BuildNoSchema, setup.ViewModel.InstallDisabledReason(state.Id));
        Assert.False(setup.ViewModel.BuildActiveCommand.CanExecute(null));
        Assert.False(setup.ViewModel.BuildAndInstallActiveCommand.CanExecute(null));
    }

    [Fact] public async Task Open_project_for_a_folder_that_is_in_the_list_shows_the_message()
    {
        using var setup = await CreateAsync();
        var state = NewProject(setup);
        setup.Dialogs.FolderAnswers.Enqueue(state.Folder);

        setup.ViewModel.OpenProjectCommand.Execute(null);

        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal((setup.Texts.ProjectMessageTitle, setup.Texts.ProjectAlreadyInList(state.Folder)), (title, message));
        Assert.Single(setup.Projects.Projects);
        Assert.Null(setup.ViewModel.StatusMessage);
    }
}

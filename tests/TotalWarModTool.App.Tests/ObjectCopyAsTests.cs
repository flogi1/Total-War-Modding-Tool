using TotalWarModTool.Editing;
using TotalWarModTool.Objects;
using static TotalWarModTool.App.Tests.ObjectApplySetup;

namespace TotalWarModTool.App.Tests;

public class ObjectCopyAsTests
{
    // A copy goes into new files named after the project: there is no project file of either table in these tests.
    private const string XFile = @"db\x_tables\Flo.tsv";
    private const string YFile = @"db\y_tables\Flo.tsv";

    // Runs "Copy as" the way the button does (through the handler the controller put on the tab) and proves that it could run.
    private static async Task CopyAsync(ObjectTabViewModel tab)
    {
        Assert.Null(tab.CopyDisabledReason);
        Assert.True(tab.CopyCommand.CanExecute(null));
        await tab.CopyHandler!(tab);
    }

    private static ParsedObject XObject(string key) =>
        new(new ObjectId("x_tables", [key]), [new ParsedField("key", key, 0), new ParsedField("label", "Copied", 0)], [], [], []);

    private static void AssertNothingCopied(Setup setup)
    {
        Assert.False(setup.History.CanUndo);
        Assert.False(Directory.Exists(Path.Combine(setup.Project.Folder, "pack", "db")));
        Assert.DoesNotContain(setup.Texts.ObjectsApplyWriting, setup.Runner.Titles);
    }

    [Fact] public async Task Copy_as_writes_the_root_and_its_rows_and_opens_the_copy_in_the_same_tab()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);
        setup.Dialogs.NewKeyAnswer = _ => "k9";
        var recorded = 0;
        setup.History.Changed += () => recorded++;
        var changedFor = new List<Guid>();
        setup.Apply.FilesChanged += (_, id) => changedFor.Add(id);

        await CopyAsync(tab);

        Assert.Equal([("k1", "k1_copy")], setup.Dialogs.NewKeyAsks);
        Assert.Contains(setup.Texts.ObjectsCopyAsChecking, setup.Runner.Titles);
        var x = Assert.Single(setup.RowsOf(XFile));
        Assert.Equal(("k9", "First"), ((string)x[0]!, (string)x[1]!));
        Assert.Equal([("k9", 1), ("k9", 2)], setup.RowsOf(YFile).Select(row => ((string)row[0]!, (int)row[1]!)));
        Assert.Equal(["k9"], tab.Current.KeyValues);
        Assert.Equal(2, tab.History.Count);
        Assert.Same(tab, setup.Tabs.SelectedTab);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(SourceKind.Project, tab.Result!.View.Root!.Provenance.Source.Kind);
        var step = Assert.IsType<FileContentsStep>(setup.History.NextUndo);
        Assert.Equal(1, recorded);
        Assert.Equal([XFile, YFile], step.Files.Order(StringComparer.Ordinal));
        Assert.NotNull(tab.ResultLine);
        Assert.Contains("2 files created", tab.ResultLine);
        Assert.Empty(setup.Dialogs.Messages);
        Assert.Equal([setup.Project.Id], changedFor);
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains("k9", StringComparison.Ordinal) && entry.Contains("Copied", StringComparison.Ordinal));
    }

    [Fact] public async Task The_new_key_dialog_refuses_an_empty_a_spaced_and_an_existing_key()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);
        Func<string, string?>? validate = null;
        setup.Dialogs.NewKeyAnswer = given =>
        {
            validate = given;
            return null;
        };

        await CopyAsync(tab);

        Assert.Equal([("k1", "k1_copy")], setup.Dialogs.NewKeyAsks);
        Assert.NotNull(validate);
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyEmpty, validate(string.Empty));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyWhitespace, validate("a b"));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyWhitespace, validate("a\tb"));
        Assert.Equal(setup.Texts.ObjectsCopyAsKeyExists("data.pack"), validate("k2"));
        Assert.Null(validate("k9"));
        Assert.Null(validate("k1_copy"));
    }

    [Fact] public async Task Copy_as_with_unapplied_text_asks_first_and_cancel_copies_nothing()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);
        Edit(tab, "  label = First", "  label = Changed");
        setup.Dialogs.NewKeyAnswer = _ => "k9";

        await CopyAsync(tab);

        Assert.Equal(["k1"], setup.Dialogs.UnappliedAsks);
        Assert.Empty(setup.Dialogs.NewKeyAsks);
        Assert.True(tab.HasUnappliedText);
        Assert.Equal("k1", tab.Current.JoinedKey);
        Assert.DoesNotContain(setup.Texts.ObjectsCopyAsChecking, setup.Runner.Titles);
        AssertNothingCopied(setup);

        // Discard lets the copy go on: the key is asked and the copy is written from the built object.
        setup.Dialogs.UnappliedAnswer = SaveChoice.Discard;
        await CopyAsync(tab);

        Assert.Single(setup.Dialogs.NewKeyAsks);
        Assert.Equal("First", Assert.Single(setup.RowsOf(XFile))[1]);
        Assert.Equal("k9", tab.Current.JoinedKey);
    }

    [Fact] public async Task Copy_as_after_applying_the_unapplied_text_copies_the_applied_state()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);
        Edit(tab, "  label = First", "  label = Changed");
        setup.Dialogs.UnappliedAnswer = SaveChoice.Save;
        setup.Dialogs.NewKeyAnswer = _ => "k9";
        var recorded = 0;
        setup.History.Changed += () => recorded++;

        await CopyAsync(tab);

        Assert.Equal(["k1"], setup.Dialogs.UnappliedAsks);
        Assert.Single(setup.Dialogs.NewKeyAsks);
        Assert.Equal([("k1", "Changed"), ("k9", "Changed")], setup.RowsOf(XFile).Select(row => ((string)row[0]!, (string)row[1]!)));
        Assert.Equal("k9", tab.Current.JoinedKey);
        Assert.Equal(2, recorded);
        Assert.Empty(setup.Dialogs.Messages);
    }

    [Fact] public async Task Cancelling_the_key_dialog_copies_nothing()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);

        await CopyAsync(tab);

        Assert.Single(setup.Dialogs.NewKeyAsks);
        Assert.Equal("k1", tab.Current.JoinedKey);
        Assert.Single(tab.History);
        Assert.Null(tab.ResultLine);
        Assert.DoesNotContain(setup.Texts.ObjectsCopyAsChecking, setup.Runner.Titles);
        AssertNothingCopied(setup);
    }

    [Fact] public async Task A_taken_derived_key_is_reported_and_nothing_is_written()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);

        // The root k9 is free, the second object of the copy would take k2 of the game.
        var done = await setup.Apply.WriteCopyAsync((_, _, _) => new CopyResult([XObject("k9"), XObject("k2")], []), tab.Result!.Order, setup.Project,
            setup.Objects.Stores!, "Copy: test", "k1", () => true);

        Assert.Null(done);
        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ObjectsCopyAsTakenTitle, title);
        Assert.StartsWith(setup.Texts.ObjectsCopyAsTakenMessage(1), message);
        Assert.Contains("x_tables: k2", message);
        Assert.DoesNotContain("k9", message);
        Assert.Contains(setup.Texts.ObjectsCopyAsChecking, setup.Runner.Titles);
        AssertNothingCopied(setup);
    }

    [Fact] public async Task An_error_of_a_copy_is_shown_without_a_line_number_and_nothing_is_written()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);

        // The copied y row has a value that its I32 field does not accept; a copy has no text, so its diagnostics have no line.
        var row = new ParsedRow([new ParsedField("value", "abc", 0)], [], 0);
        var copy = XObject("k9") with { Sections = [new ParsedSection("y_tables", "x", [row], 0)] };
        var done = await setup.Apply.WriteCopyAsync((_, _, _) => new CopyResult([copy], []), tab.Result!.Order, setup.Project,
            setup.Objects.Stores!, "Copy: test", "k1", () => true);

        Assert.Null(done);
        var (title, message) = Assert.Single(setup.Dialogs.Messages);
        Assert.Equal(setup.Texts.ObjectsApplyErrorsTitle, title);
        Assert.StartsWith(setup.Texts.ObjectsApplyErrorsMessage(1), message);
        Assert.Contains(setup.Texts.DiagnosticText(DiagnosticKind.InvalidValue), message);
        Assert.DoesNotContain(setup.Texts.ObjectsApplyErrorLine(0, string.Empty).Trim(), message);
        AssertNothingCopied(setup);
    }

    [Fact] public async Task A_copy_without_changes_says_so_and_writes_nothing()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);

        var done = await setup.Apply.WriteCopyAsync((_, _, _) => new CopyResult([], []), tab.Result!.Order, setup.Project,
            setup.Objects.Stores!, "Copy: test", "k1", () => true);

        Assert.Null(done);
        Assert.Equal((setup.Texts.ObjectsCopyAs, setup.Texts.ObjectsApplyResultNothing), Assert.Single(setup.Dialogs.Messages));
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains("no change to write", StringComparison.Ordinal));
        AssertNothingCopied(setup);
    }

    [Fact] public async Task The_result_line_of_a_copy_names_the_rows_left_out()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);
        CopyNote[] notes = [new CopyNote(CopyNoteKind.ForeignKeyRows, "z_tables", 3), new CopyNote(CopyNoteKind.HiddenSection, "y_tables", 1)];

        var done = await setup.Apply.WriteCopyAsync((_, _, _) => new CopyResult([XObject("k9")], notes), tab.Result!.Order, setup.Project,
            setup.Objects.Stores!, "Copy: test", "k1", () => true);

        Assert.NotNull(done);
        Assert.Equal(notes, done.Notes);
        Assert.Same(done.Step, setup.History.NextUndo);
        tab.SetApplyResult(done.Plan.Summary, [XFile], 0, done.Notes);
        Assert.Contains(setup.Texts.ObjectsCopyAsNoteForeign(3, "z_tables"), tab.ResultLine);
        Assert.DoesNotContain("y_tables", tab.ResultLine);
    }

    [Fact] public async Task Undo_of_a_copy_removes_every_row_and_rebuilds_the_tab()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);
        setup.Dialogs.NewKeyAnswer = _ => "k9";
        await CopyAsync(tab);
        var step = Assert.IsType<FileContentsStep>(setup.History.NextUndo);
        Assert.Equal(1, setup.Builds("k9"));
        await setup.Main.Main.OpenProjectFileAsync(setup.Project.Id, XFile);
        Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.SelectedTab);

        setup.Main.Documents!.UndoCommand.Execute(null);

        Assert.Equal(2, step.InTrash.Count);
        Assert.False(File.Exists(setup.ProjectFile(XFile)));
        Assert.False(File.Exists(setup.ProjectFile(YFile)));
        Assert.False(setup.History.CanUndo);
        Assert.Same(tab, setup.Tabs.SelectedTab);
        Assert.Equal(2, setup.Builds("k9"));
        Assert.Null(tab.Result!.View.Root);
    }

    [Fact] public async Task Copy_as_is_disabled_without_a_project_on_a_stale_tab_and_for_a_missing_object()
    {
        using var setup = await CreateAsync();
        var tab = await ShowK1Async(setup);
        setup.Dialogs.NewKeyAnswer = _ => "k9";
        Assert.Null(tab.CopyDisabledReason);

        tab.MarkStale();
        Assert.Equal(setup.Texts.ObjectsApplyStale, tab.CopyDisabledReason);
        Assert.False(tab.CopyCommand.CanExecute(null));
        await setup.Apply.CopyAsync(tab);
        Assert.Empty(setup.Dialogs.NewKeyAsks);
        Assert.Contains(setup.Game.Log.Infos, entry => entry.Contains(setup.Texts.ObjectsApplyStale, StringComparison.Ordinal));

        tab.NavigateTo(new ObjectId("x_tables", ["k7"]), newTab: false);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(setup.Texts.ObjectsCopyAsNoRoot, tab.CopyDisabledReason);
        await setup.Apply.CopyAsync(tab);
        Assert.Empty(setup.Dialogs.NewKeyAsks);

        Assert.True(setup.Main.Projects.Remove(setup.Project.Id).Ok);
        Assert.Equal(setup.Texts.ObjectsApplyNoProject, tab.CopyDisabledReason);
        Assert.Equal(setup.Texts.ObjectsApplyNoProject, tab.CopyToolTip);
        await setup.Apply.CopyAsync(tab);
        Assert.Empty(setup.Dialogs.NewKeyAsks);
        Assert.False(setup.History.CanUndo);
    }
}

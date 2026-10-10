using TotalWarModTool.Editing;
using TotalWarModTool.Localization;
using TotalWarModTool.Objects;
using TotalWarModTool.Schema;

namespace TotalWarModTool.App.Tests;

public class ObjectTabViewModelTests
{
    private const string Table = "building_levels_tables";
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly TableSource Vanilla = new(SourceKind.Vanilla, "db.pack", @"D:\game\db.pack");
    private static readonly TableSource Mod1 = new(SourceKind.Mod, "m1.pack", @"D:\mods\m1.pack");
    private static readonly TableSource Mod2 = new(SourceKind.Mod, "m2.pack", @"D:\mods\m2.pack");
    private static readonly TableSource Project = new(SourceKind.Project, "My project", @"D:\projects\p");
    private static readonly ObjectWarning SchemaWarning = new(ObjectWarningKind.TableNotInSchema, "x_tables", "not in the schema");

    private static string Translate(string key) => key;

    private static ObjectRef Ref(string key, string? rootPack = null) => new(Table, [key], rootPack);

    private static ObjectId Id(string key) => new(Table, [key]);

    private static LoadOrder Order(params LoadOrderWarning[] warnings) => new([Vanilla, Mod1, Mod2, Project], null, warnings);

    private static FieldValue Plain(string name, string text) =>
        new(name, FieldType.StringU8, text, null, ReferenceResolution.None, null, null);

    private static ObjectRow Row(string value) => new([Plain("effect", value)], new RowProvenance(Vanilla, [], false, false), null, null);

    // Sections sorted by table name: index 0 has two rows, index 1 has 25 (collapsed until it is expanded).
    private static ObjectView View(string key = "a", LoadOrder? order = null, params ObjectWarning[] warnings) => new(
        Id(key), Row("v"), [],
        [
            new ObjectSection("a_tables", "building", false, [Row("1"), Row("2")]),
            new ObjectSection("b_tables", "building", false, Enumerable.Range(0, 25).Select(i => Row("r" + i)).ToArray()),
        ],
        warnings, order ?? Order(), [Table]);

    private static LocTable Loc() => new(new Dictionary<string, LocText> { ["effects_onscreen_name_e1"] = new("Fire", Vanilla) }, []);

    private static ObjectBuildResult Result(ObjectView? view = null) =>
        new(view ?? View(warnings: [SchemaWarning]), view?.Order ?? Order(), null, new Dictionary<string, string> { [Table] = "sig" }, "loc", Loc());

    private static ObjectTabViewModel Tab(AppTexts? texts = null, int position = 0, params ObjectRef[] history) =>
        new(texts ?? Texts, _ => { }, history.Length == 0 ? [Ref("a")] : history, position, Translate);

    private static ObjectTabViewModel ReadyTab()
    {
        var tab = Tab();
        tab.ApplyResult(Result());
        return tab;
    }

    private static string Joined(params string[] lines) => string.Join(Environment.NewLine, lines);

    [Fact] public void A_composite_key_title_separates_the_values_and_the_entry_path_stays()
    {
        var tab = Tab(history: [new ObjectRef("pairs_tables", ["x", "y\n"], null)]);
        Assert.Equal(@"x | y\u000A", tab.Title);
        Assert.Equal(@"pairs: x | y\u000A", tab.ToolTip);
        Assert.Equal("pairs_tables:x|y\n", tab.EntryPath);
        Assert.Equal("xy\n", tab.Current.JoinedKey);
    }

    [Fact] public void A_new_tab_is_idle_with_the_idle_notice_and_the_key_as_title()
    {
        var tab = Tab(history: [Ref("a\nb"), Ref("c")]);
        Assert.Equal(ObjectTabState.Idle, tab.State);
        Assert.Equal(Texts.ObjectsStateIdle, tab.Notice);
        Assert.Equal(@"a\u000Ab", tab.Title);
        Assert.Equal(@"building_levels: a\u000Ab", tab.ToolTip);
        Assert.Equal(("object:" + Table, Table + ":a\nb"), (tab.PackPath, tab.EntryPath));
        Assert.Equal((Ref("a\nb"), 2, 0), (tab.Current, tab.History.Count, tab.Position));
        Assert.Equal(string.Empty, tab.Text);
        Assert.Equal(string.Empty, tab.Status);
        Assert.Null(tab.WarningsText);
        Assert.Null(tab.Result);
        Assert.False(tab.IsTableEntry);
        Assert.True(tab.RefreshCommand.CanExecute(null));
        Assert.False(tab.BackCommand.CanExecute(null));
        Assert.True(tab.ForwardCommand.CanExecute(null));
    }

    [Fact] public void ApplyResult_renders_lines_status_and_warnings()
    {
        var tab = Tab();
        var changed = new List<string?>();
        tab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        tab.ApplyResult(Result());
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal("Vanilla + 2 mods + project, 1 warnings", tab.Status);
        Assert.Equal("TableNotInSchema: x_tables: not in the schema", tab.WarningsText);
        Assert.NotEmpty(tab.Text);
        Assert.Null(tab.Notice);
        Assert.NotNull(tab.Result);
        Assert.All(new[] { "State", "Text", "Status", "WarningsText", "Notice", "Result" }, name => Assert.Contains(name, changed));
    }

    [Fact] public void NavigateTo_cuts_the_forward_part_appends_and_builds()
    {
        var tab = Tab(history: [Ref("a", @"D:\mods\root.pack"), Ref("b"), Ref("c")]);
        var builds = 0;
        tab.BuildHandler = _ => builds++;
        tab.NavigateTo(Id("d"), newTab: false);
        Assert.Equal(["a", "d"], tab.History.Select(r => r.JoinedKey));
        Assert.Equal(1, tab.Position);
        Assert.Equal(1, builds);
        Assert.Equal(new ObjectRef(Table, ["d"], @"D:\mods\root.pack"), tab.Current);
        Assert.Equal("d", tab.Title);
        Assert.Equal("building_levels: d", tab.ToolTip);
    }

    [Fact] public void The_history_keeps_the_last_fifty_entries()
    {
        var tab = Tab();
        for (var i = 1; i <= 60; i++)
        {
            tab.NavigateTo(Id("n" + i), newTab: false);
        }

        Assert.Equal((50, 49), (tab.History.Count, tab.Position));
        Assert.Equal("n11", tab.History[0].JoinedKey);
        Assert.Equal("n60", tab.Current.JoinedKey);
        tab.BackCommand.Execute(null);
        Assert.Equal(48, tab.Position);
        Assert.Equal("n59", tab.Current.JoinedKey);
    }

    [Fact] public void Back_and_forward_move_the_position_and_build()
    {
        var tab = Tab(position: 1, history: [Ref("a"), Ref("b"), Ref("c")]);
        var builds = 0;
        tab.BuildHandler = _ => builds++;
        Assert.True(tab.BackCommand.CanExecute(null));
        tab.BackCommand.Execute(null);
        Assert.Equal(("a", 0, 1), (tab.Title, tab.Position, builds));
        Assert.False(tab.BackCommand.CanExecute(null));
        tab.ForwardCommand.Execute(null);
        tab.ForwardCommand.Execute(null);
        Assert.Equal(("c", 2, 3), (tab.Title, tab.Position, builds));
        Assert.False(tab.ForwardCommand.CanExecute(null));
        tab.RefreshCommand.Execute(null);
        Assert.Equal((2, 4), (tab.Position, builds));
        Assert.Equal(3, tab.History.Count);
    }

    [Fact] public void Moving_to_another_object_drops_what_the_old_one_showed()
    {
        var tab = Tab(position: 1, history: [Ref("a"), Ref("b")]);
        tab.ApplyResult(Result(View("b")));
        Assert.NotEmpty(tab.Text);
        tab.BackCommand.Execute(null);
        Assert.Equal((ObjectTabState.Idle, 0, string.Empty), (tab.State, tab.Text.Length, tab.Status));
        Assert.Null(tab.Result);
        Assert.Equal(Texts.ObjectsStateIdle, tab.Notice);
    }

    [Fact] public void Moving_to_another_object_drops_the_stale_mark()
    {
        var tab = ReadyTab();
        tab.MarkStale();
        Assert.True(tab.IsStale);
        tab.NavigateTo(Id("b"), newTab: false);
        Assert.False(tab.IsStale);
        Assert.Equal(string.Empty, tab.StaleText);
    }

    [Fact] public void A_new_result_drops_the_stale_marks_but_not_an_outside_change()
    {
        var tab = ReadyTab();
        tab.MarkStale();
        tab.MarkSchemaStale();
        tab.ApplyResult(Result());
        Assert.False(tab.IsStale);
        tab.SetOutsideChange(OutsideChange.Changed);
        tab.ApplyResult(Result());
        Assert.True(tab.IsStaleOutside);
    }

    [Fact] public void The_commands_wait_while_a_build_runs()
    {
        var tab = Tab(position: 1, history: [Ref("a"), Ref("b")]);
        tab.BeginBuild();
        Assert.Equal(ObjectTabState.Building, tab.State);
        Assert.Equal(Texts.ObjectsStateBuilding, tab.Notice);
        Assert.False(tab.RefreshCommand.CanExecute(null));
        Assert.False(tab.BackCommand.CanExecute(null));
        tab.ApplyResult(Result(View("b")));
        Assert.True(tab.RefreshCommand.CanExecute(null));
        Assert.True(tab.BackCommand.CanExecute(null));
    }

    [Fact] public void A_shift_click_opens_a_new_tab_instead()
    {
        var tab = Tab(history: [Ref("a", @"D:\mods\root.pack"), Ref("b")]);
        var builds = 0;
        var opened = new List<(ObjectTabViewModel From, ObjectRef Target, bool NewTab)>();
        tab.BuildHandler = _ => builds++;
        tab.OpenHandler = (from, target, newTab) => opened.Add((from, target, newTab));
        tab.NavigateTo(Id("d"), newTab: true);
        Assert.Equal([(tab, new ObjectRef(Table, ["d"], @"D:\mods\root.pack"), true)], opened);
        Assert.Equal(["a", "b"], tab.History.Select(r => r.JoinedKey));
        Assert.Equal((0, 0), (tab.Position, builds));
        Assert.Equal("a", tab.Title);
    }

    [Fact] public void A_cancelled_build_keeps_the_old_text_and_says_so()
    {
        var tab = ReadyTab();
        var text = tab.Text;
        tab.BeginBuild();
        Assert.Equal(text, tab.Text);
        tab.ApplyCancelled();
        Assert.Equal(ObjectTabState.Cancelled, tab.State);
        Assert.Equal(text, tab.Text);
        Assert.Contains(Texts.ObjectsStateCancelled, tab.Notice);
        Assert.True(tab.RefreshCommand.CanExecute(null));
    }

    [Fact] public void A_failed_build_shows_its_reason_or_the_default_text()
    {
        var tab = Tab();
        tab.ApplyFailed();
        Assert.Equal((ObjectTabState.Failed, Texts.ObjectsStateFailed), (tab.State, tab.Notice));
        tab.ApplyFailed("No game is set.");
        Assert.Equal("No game is set.", tab.Notice);
        tab.BeginBuild();
        tab.ApplyFailed();
        Assert.Equal(Texts.ObjectsStateFailed, tab.Notice);
    }

    [Fact] public void A_root_not_found_result_shows_the_not_found_notice()
    {
        var tab = Tab();
        tab.ApplyResult(Result(new ObjectView(Id("a"), null, [], [], [new ObjectWarning(ObjectWarningKind.RootNotFound, null, "no row")], Order(), [Table])));
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(Texts.ObjectsNotFound, tab.Notice);
        Assert.False(tab.JumpToRowCommand.CanExecute(null));
    }

    [Fact] public void Notices_list_unsaved_source_root_pack_added_and_missing_used_mods()
    {
        var tab = Tab();
        tab.SetUnsavedSource(true);
        tab.SetCategoryProblem(true);
        tab.ApplyResult(Result(View(order: Order(
            new LoadOrderWarning(LoadOrderProblem.RootPackAdded, "r.pack", "added"),
            new LoadOrderWarning(LoadOrderProblem.UsedModsUnreadable, null, "unreadable")))));
        Assert.Equal(Joined(Texts.ObjectsNoticeUnsavedSource, Texts.ObjectsNoticeRootPackAdded, Texts.ObjectsNoticeNoUsedMods,
            Texts.ObjectsNoticeCategoryUnreadable), tab.Notice);

        tab.SetJumpNotice(JumpOutcome.SourceGone);
        Assert.EndsWith(Texts.ObjectsNoticeRowSourceGone, tab.Notice);
        tab.SetJumpNotice(JumpOutcome.RowNotFound);
        Assert.EndsWith(Texts.ObjectsNoticeRowNotFound, tab.Notice);
        tab.ApplyResult(Result());                                        // the next result clears the jump notice
        Assert.DoesNotContain(Texts.ObjectsNoticeRowNotFound, tab.Notice);
        tab.SetUnsavedSource(false);
        tab.SetCategoryProblem(false);
        Assert.Null(tab.Notice);
    }

    [Fact] public void Moving_to_another_object_forgets_the_flags_of_the_source_tab()
    {
        var tab = Tab();
        tab.SetUnsavedSource(true);
        tab.SetCategoryProblem(true);
        Assert.Contains(Texts.ObjectsNoticeUnsavedSource, tab.Notice);
        tab.NavigateTo(Id("b"), newTab: false);
        Assert.Equal(Texts.ObjectsStateIdle, tab.Notice);
        tab.SetUnsavedSource(true);
        tab.RefreshCommand.Execute(null);                                 // a refresh keeps the flags
        Assert.Contains(Texts.ObjectsNoticeUnsavedSource, tab.Notice);
    }

    [Fact] public void The_status_counts_missing_mods()
    {
        var tab = Tab();
        tab.ApplyResult(Result(View(order: Order(new LoadOrderWarning(LoadOrderProblem.ModPackMissing, "m3.pack", "missing")))));
        Assert.Equal("Vanilla + 2 mods + project, 2 of 3 mods loaded", tab.Status);
    }

    [Fact] public void The_status_without_a_project_and_without_warnings_is_short()
    {
        var tab = Tab();
        tab.ApplyResult(Result(View(order: new LoadOrder([Vanilla, Mod1], null, []))));
        Assert.Equal("Vanilla + 1 mods", tab.Status);
        Assert.Null(tab.WarningsText);
    }

    [Fact] public void JumpToRow_needs_a_ready_root_and_a_handler()
    {
        var tab = Tab();
        var jumps = 0;
        Assert.False(tab.JumpToRowCommand.CanExecute(null));
        tab.JumpHandler = _ => jumps++;
        Assert.False(tab.JumpToRowCommand.CanExecute(null));
        tab.ApplyResult(Result());
        Assert.True(tab.JumpToRowCommand.CanExecute(null));
        tab.JumpToRowCommand.Execute(null);
        Assert.Equal(1, jumps);
    }

    [Fact] public void A_language_change_renders_titles_and_notices_again()
    {
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var texts = new AppTexts(ui);
        var tab = Tab(texts);
        tab.SetUnsavedSource(true);
        tab.ApplyResult(Result());
        var changed = new List<string?>();
        tab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        ui.Load("de");

        Assert.Contains(nameof(ObjectTabViewModel.Notice), changed);
        Assert.Contains(nameof(ObjectTabViewModel.Status), changed);
        Assert.Equal(texts.ObjectsNoticeUnsavedSource, tab.Notice);
        Assert.Equal("Vanilla + 2 Mods + Projekt, 1 Warnungen", tab.Status);
    }

    [Fact] public void The_stale_text_of_an_object_tab_names_its_sources()
    {
        var pack = Tab();
        pack.MarkStale();
        Assert.True(pack.IsStale);
        Assert.Equal(Texts.ObjectsStale, pack.StaleText);

        var schema = Tab();
        schema.MarkSchemaStale();
        Assert.Equal(Texts.TabStaleSchema, schema.StaleText);
    }

    [Fact] public void A_result_sets_the_baseline_text_and_the_text()
    {
        var tab = Tab();
        var changed = new List<string?>();
        tab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var result = Result();
        tab.ApplyResult(result);
        var expected = ObjectText.RenderBlock(result.View, result.Category).Text;
        Assert.Equal(expected, tab.BaselineText);
        Assert.Equal(expected, tab.Text);
        Assert.False(tab.HasUnappliedText);
        Assert.False(tab.IsModified);
        Assert.Equal(expected.Split('\n')[0].Length, tab.HeadLineLength);
        Assert.True(tab.HeadLineLength > 0);
        Assert.NotNull(tab.Block);
        Assert.Contains("Text", changed);
        Assert.Contains("BaselineText", changed);
    }

    [Fact] public void A_changed_text_is_unapplied_and_marks_the_tab_modified()
    {
        var tab = ReadyTab();
        var changed = new List<string?>();
        tab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        tab.Text = tab.BaselineText + "\n  x = 1";
        Assert.True(tab.HasUnappliedText);
        Assert.True(tab.IsModified);
        Assert.Contains("HasUnappliedText", changed);
        Assert.Contains("IsModified", changed);
        changed.Clear();
        tab.Text = tab.BaselineText;
        Assert.False(tab.HasUnappliedText);
        Assert.False(tab.IsModified);
        Assert.Contains("HasUnappliedText", changed);
        Assert.Contains("IsModified", changed);
    }

    [Fact] public void Discard_sets_the_text_back_to_the_baseline()
    {
        var tab = ReadyTab();
        tab.Text = tab.BaselineText + "\n  x = 1";
        tab.DiscardText();
        Assert.Equal(tab.BaselineText, tab.Text);
        Assert.False(tab.HasUnappliedText);
    }

    [Fact] public void Moving_to_another_object_clears_the_text()
    {
        var tab = ReadyTab();
        tab.Text = tab.BaselineText + "\n  x = 1";
        tab.NavigateTo(Id("b"), false);
        Assert.Equal(string.Empty, tab.Text);
        Assert.Equal(string.Empty, tab.BaselineText);
        Assert.Null(tab.Block);
        Assert.Equal(0, tab.HeadLineLength);
        Assert.False(tab.HasUnappliedText);
    }

    [Fact] public void Apply_is_disabled_without_a_handler_and_names_the_first_reason()
    {
        var tab = Tab();
        Assert.Equal(Texts.ObjectsApplyNotReady, tab.ApplyDisabledReason);
        Assert.False(tab.ApplyCommand.CanExecute(null));
        tab.ApplyResult(Result());
        Assert.Equal(Texts.ObjectsApplyNoChanges, tab.ApplyDisabledReason);
        tab.Text = tab.BaselineText + "\n  x = 1";
        Assert.Null(tab.ApplyDisabledReason);
        Assert.False(tab.ApplyCommand.CanExecute(null));                    // no handler
    }

    [Fact] public void The_environment_reason_comes_first_then_root_pack_then_state_then_stale()
    {
        var tab = Tab();
        tab.ApplyHandler = _ => Task.CompletedTask;
        tab.ApplyResult(Result());
        tab.Text = tab.BaselineText + "\n  x = 1";
        tab.ApplyEnvironmentReason = () => "busy";
        Assert.Equal("busy", tab.ApplyDisabledReason);
        Assert.False(tab.ApplyCommand.CanExecute(null));
        tab.ApplyEnvironmentReason = () => null;
        Assert.Null(tab.ApplyDisabledReason);
        Assert.True(tab.ApplyCommand.CanExecute(null));

        var rooted = Tab();
        rooted.ApplyHandler = _ => Task.CompletedTask;
        rooted.ApplyResult(Result(View(order: Order(new LoadOrderWarning(LoadOrderProblem.RootPackAdded, "r.pack", "added")))));
        rooted.Text = rooted.BaselineText + "\n  x = 1";
        Assert.Equal(Texts.ObjectsApplyRootPackNotInOrder, rooted.ApplyDisabledReason);

        tab.MarkStale();
        Assert.Equal(Texts.ObjectsApplyStale, tab.ApplyDisabledReason);
        tab.ClearStale();
        Assert.Null(tab.ApplyDisabledReason);

        tab.BeginBuild();
        Assert.Equal(Texts.ObjectsApplyNotReady, tab.ApplyDisabledReason);
    }

    [Fact] public void Apply_runs_the_handler_once_with_the_tab()
    {
        var tab = ReadyTab();
        var calls = new List<ObjectTabViewModel>();
        tab.ApplyHandler = t =>
        {
            calls.Add(t);
            return Task.CompletedTask;
        };

        tab.Text = tab.BaselineText + "\n  x = 1";
        tab.ApplyCommand.Execute(null);

        Assert.Same(tab, Assert.Single(calls));
    }

    [Fact] public void The_result_line_lists_rows_files_texts_created_files_and_warnings()
    {
        var tab = ReadyTab();
        Assert.Null(tab.ResultLine);
        tab.SetApplyResult(new WriteSummary(1, 2, 0, 1, 0, 2, 1, 0), [@"db\x_tables\flo.tsv"], 1);
        Assert.Equal(@"3 rows in 2 files changed, 1 texts, 1 files created: db\x_tables\flo.tsv, 1 warnings", tab.ResultLine);
        tab.NavigateTo(Id("b"), false);
        Assert.Null(tab.ResultLine);
    }

    [Fact] public void Back_with_unapplied_text_asks_and_discard_resets_the_text()
    {
        var asks = 0;
        var answer = false;
        Func<ObjectTabViewModel, Task<bool>> leave = t =>
        {
            asks++;
            if (answer)
            {
                t.DiscardText();
            }

            return Task.FromResult(answer);
        };

        var tab = Tab(position: 1, history: [Ref("a"), Ref("b")]);
        tab.ApplyResult(Result(View("b")));
        tab.LeaveHandler = leave;
        tab.BackCommand.Execute(null);
        Assert.Equal((0, 0), (asks, tab.Position));                         // no change, no question

        var changed = Tab(position: 1, history: [Ref("a"), Ref("b")]);
        changed.ApplyResult(Result(View("b")));
        changed.LeaveHandler = leave;
        changed.Text = changed.BaselineText + "\n  x = 1";
        changed.BackCommand.Execute(null);
        Assert.Equal((1, 1), (asks, changed.Position));
        Assert.True(changed.HasUnappliedText);

        answer = true;
        changed.BackCommand.Execute(null);
        Assert.Equal((2, 0), (asks, changed.Position));
        Assert.False(changed.HasUnappliedText);
    }

    [Fact] public void A_new_result_replaces_the_text_and_clears_the_unapplied_mark()
    {
        var tab = ReadyTab();
        tab.Text = tab.BaselineText + "\n  x = 1";
        Assert.True(tab.HasUnappliedText);
        var result = Result(View("a"));
        tab.ApplyResult(result);
        Assert.Equal(ObjectText.RenderBlock(result.View, result.Category).Text, tab.Text);
        Assert.Equal(tab.BaselineText, tab.Text);
        Assert.False(tab.HasUnappliedText);
        Assert.False(tab.IsModified);
    }

    [Fact] public void The_apply_tool_tip_is_the_reason_or_the_name_with_the_key()
    {
        var tab = ReadyTab();
        Assert.Equal(Texts.ObjectsApplyNoChanges, tab.ApplyToolTip);
        tab.ApplyHandler = _ => Task.CompletedTask;
        tab.Text = tab.BaselineText + "\n  x = 1";
        Assert.Equal(Texts.ObjectsApply + " (" + Texts.ObjectsApplyGesture + ")", tab.ApplyToolTip);
    }

    private static BlockDiagnostic Error(int line, DiagnosticKind kind) => new(line, DiagnosticSeverity.Error, kind, "detail");

    private static ObjectTabViewModel CheckingTab(Func<ObjectChanges?> check)
    {
        var tab = ReadyTab();
        tab.ApplyHandler = _ => Task.CompletedTask;
        tab.CheckHandler = _ => check();
        return tab;
    }

    [Fact] public void Errors_of_the_check_disable_apply_and_name_their_count()
    {
        var tab = CheckingTab(() => new ObjectChanges([], [], [Error(2, DiagnosticKind.UnknownField), Error(3, DiagnosticKind.InvalidValue)]));
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tab.Text = tab.BaselineText + "\n  x = 1";
        Assert.Null(tab.ApplyDisabledReason);

        tab.Check();

        Assert.Equal(2, tab.ErrorCount);
        Assert.Equal(2, tab.Diagnostics.Count);
        Assert.Equal(Texts.ObjectsApplyErrors(2), tab.ApplyDisabledReason);
        Assert.False(tab.ApplyCommand.CanExecute(null));
        Assert.Contains(nameof(ObjectTabViewModel.Diagnostics), raised);
        Assert.Contains(nameof(ObjectTabViewModel.ErrorCount), raised);
    }

    [Fact] public void A_whitespace_only_change_disables_apply_as_no_change()
    {
        var tab = CheckingTab(() => new ObjectChanges([], [], []));
        tab.Text = tab.BaselineText + " ";
        Assert.True(tab.HasUnappliedText);
        Assert.Null(tab.ApplyDisabledReason);

        tab.Check();

        Assert.True(tab.IsDiffEmpty);
        Assert.Equal(Texts.ObjectsApplyNoChanges, tab.ApplyDisabledReason);
        Assert.False(tab.ApplyCommand.CanExecute(null));
    }

    [Fact] public void A_check_result_belongs_to_the_text_it_was_computed_for()
    {
        var tab = CheckingTab(() => new ObjectChanges([], [], [Error(2, DiagnosticKind.UnknownField)]));
        tab.Text = tab.BaselineText + "\n  x = 1";
        tab.Check();
        Assert.Equal(1, tab.ErrorCount);

        tab.Text += "2";

        Assert.Empty(tab.Diagnostics);
        Assert.Equal(0, tab.ErrorCount);
        Assert.False(tab.IsDiffEmpty);
        Assert.Null(tab.ApplyDisabledReason);
    }

    [Fact] public void A_new_result_clears_the_diagnostics()
    {
        var tab = CheckingTab(() => new ObjectChanges([], [], [Error(2, DiagnosticKind.UnknownField)]));
        tab.Text = tab.BaselineText + "\n  x = 1";
        tab.Check();

        tab.ApplyResult(Result());

        Assert.Empty(tab.Diagnostics);
        Assert.False(tab.IsDiffEmpty);
    }

    [Fact] public void Check_without_a_handler_or_a_result_does_nothing()
    {
        var calls = 0;
        var noResult = Tab();
        noResult.CheckHandler = _ =>
        {
            calls++;
            return new ObjectChanges([], [], []);
        };
        noResult.Check();
        var noHandler = ReadyTab();
        noHandler.Check();

        Assert.Equal(0, calls);
        Assert.Empty(noResult.Diagnostics);
        Assert.Empty(noHandler.Diagnostics);
        Assert.False(noHandler.IsDiffEmpty);
    }

    [Fact] public void DisplayNameOf_reads_the_loc_of_the_result_and_is_null_without_one()
    {
        var tab = Tab();
        var fire = new ObjectId("effects_tables", ["e1"]);
        Assert.Null(tab.DisplayNameOf(fire));

        tab.ApplyResult(Result());
        Assert.Equal("Fire", tab.DisplayNameOf(fire));
        Assert.Null(tab.DisplayNameOf(new ObjectId("effects_tables", ["unknown"])));

        tab.Text += "x";
        Assert.Equal("Fire", tab.DisplayNameOf(fire));
    }

    [Fact] public void Expanded_sections_survive_a_new_result_and_not_a_move()
    {
        var tab = ReadyTab();
        Assert.False(tab.IsSectionExpanded(1));

        tab.SetSectionExpanded(1, true);
        tab.SetSectionExpanded(1, true);
        Assert.True(tab.IsSectionExpanded(1));
        Assert.False(tab.IsSectionExpanded(0));

        tab.ApplyResult(Result());
        Assert.True(tab.IsSectionExpanded(1));

        tab.SetSectionExpanded(1, false);
        Assert.False(tab.IsSectionExpanded(1));

        tab.SetSectionExpanded(0, true);
        tab.NavigateTo(Id("b"), false);
        Assert.False(tab.IsSectionExpanded(0));
    }

    [Fact] public void An_expanded_section_stays_expanded_when_an_earlier_section_disappears()
    {
        static ObjectSection Section(string table) => new(table, "building", false, [Row("1"), Row("2")]);
        static ObjectBuildResult With(params ObjectSection[] sections) => Result(new ObjectView(Id("a"), Row("v"), [], sections, [], Order(), [Table]));

        var tab = Tab();
        tab.ApplyResult(With(Section("a_tables"), Section("b_tables"), Section("c_tables")));
        tab.SetSectionExpanded(2, true);

        tab.ApplyResult(With(Section("a_tables"), Section("c_tables")));

        Assert.True(tab.IsSectionExpanded(1));
        Assert.False(tab.IsSectionExpanded(0));
        Assert.False(tab.IsSectionExpanded(2));
    }
}

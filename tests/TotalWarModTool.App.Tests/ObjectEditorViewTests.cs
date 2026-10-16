using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using TotalWarModTool.Localization;
using TotalWarModTool.Objects;
using TotalWarModTool.Schema;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class ObjectEditorViewTests
{
    private const string Table = "building_levels_tables";
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly TableSource Vanilla = new(SourceKind.Vanilla, "db.pack", @"D:\game\db.pack");

    private static ObjectId Id(string key) => new(Table, [key]);

    private static FieldValue Plain(string name, string text) => new(name, FieldType.StringU8, text, null, ReferenceResolution.None, null, null);

    private static ObjectRow Row(string value) => new([Plain("effect", value)], new RowProvenance(Vanilla, [], false, false), null, null);

    private static ObjectView View(string key, string value) => new(
        Id(key), Row(value), [],
        [new ObjectSection("a_tables", "building", false, [Row("1"), Row("2")])],
        [], new LoadOrder([Vanilla], null, []), [Table]);

    private static ObjectBuildResult Result(string key = "a", string value = "v") => new(
        View(key, value), new LoadOrder([Vanilla], null, []), null, new Dictionary<string, string> { [Table] = "sig" }, "loc",
        new LocTable(new Dictionary<string, LocText>(), []));

    private static readonly ObjectId Chain = new("building_chains_tables", ["chain1"]);

    // The root has a reference field that resolves, so its line carries a Link span.
    private static ObjectBuildResult LinkResult()
    {
        var link = new FieldValue("chain", FieldType.StringU8, "chain1", new TableReference("building_chains_tables", "key"),
            ReferenceResolution.Found, Chain, null);
        var root = new ObjectRow([Plain("effect", "v"), link], new RowProvenance(Vanilla, [], false, false), null, null);
        var view = new ObjectView(Id("a"), root, [], [], [], new LoadOrder([Vanilla], null, []), [Table]);
        return new ObjectBuildResult(view, new LoadOrder([Vanilla], null, []), null, new Dictionary<string, string> { [Table] = "sig" }, "loc",
            new LocTable(new Dictionary<string, LocText>(), []));
    }

    private sealed class FakeSystemTheme : ISystemThemeSource { public bool IsDark() => false; }

    private static ObjectTabViewModel Tab(ObjectBuildResult? result = null)
    {
        var tab = new ObjectTabViewModel(Texts, _ => { }, [new ObjectRef(Table, ["a"], null)], 0, key => key);
        tab.ApplyResult(result ?? Result());
        return tab;
    }

    // The view inside a shown window with the tab as DataContext. The test host has no Application, so the styles of Controls.xaml go into the view's resources.
    private static (ObjectEditorView View, Window Window) Create(ObjectTabViewModel tab)
    {
        var view = new ObjectEditorView { DataContext = tab };
        var theme = new ResourceDictionary();
        new ThemeManager(theme, new FakeSystemTheme()).Apply(ThemeChoice.Light);
        view.Resources.MergedDictionaries.Add(theme);
        view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(
            Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();
        Pump(window);
        return (view, window);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    [Fact] public void The_editor_shows_the_text_of_the_tab_and_follows_a_new_result() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            Assert.Equal(tab.BaselineText, view.Editor.Text);
            tab.ApplyResult(Result("a", "changed value"));
            Pump(window);
            Assert.Equal(tab.BaselineText, view.Editor.Text);
            Assert.Contains("changed value", view.Editor.Text);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void Typing_updates_the_text_of_the_tab() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "\n  x = 1");
            Assert.EndsWith("  x = 1", tab.Text);
            Assert.True(tab.HasUnappliedText);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_head_line_is_read_only_and_the_rest_is_not() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            var provider = view.Editor.TextArea.ReadOnlySectionProvider;
            Assert.False(provider.CanInsert(1));
            Assert.True(provider.CanInsert(tab.HeadLineLength));
            Assert.True(provider.CanInsert(view.Editor.Document.TextLength));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_editor_has_no_undo_stack_and_no_undo_command_binding() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "\n  x = 1");
            Assert.False(view.Editor.CanUndo);
            Assert.DoesNotContain(view.Editor.TextArea.CommandBindings.Cast<CommandBinding>(),
                binding => binding.Command == ApplicationCommands.Undo || binding.Command == ApplicationCommands.Redo);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void Setting_the_text_from_the_tab_does_not_echo_back() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "\n  x = 1");
            var textChanges = 0;
            tab.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ObjectTabViewModel.Text))
                {
                    textChanges++;
                }
            };

            tab.DiscardText();
            Assert.Equal(tab.BaselineText, view.Editor.Text);
            Assert.Equal(1, textChanges);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void Ctrl_Enter_is_bound_to_the_apply_command_of_the_tab() => Sta.Run(() =>
    {
        var tab = Tab();
        var calls = 0;
        tab.ApplyHandler = _ =>
        {
            calls++;
            return Task.CompletedTask;
        };
        var (view, window) = Create(tab);
        try
        {
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "\n  x = 1");
            var binding = view.Editor.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.Return && b.Modifiers == ModifierKeys.Control);
            Assert.Same(tab.ApplyCommand, binding.Command);
            Assert.True(binding.Command.CanExecute(null));
            binding.Command.Execute(null);
            Assert.Equal(1, calls);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_undo_commands_of_the_editor_reach_the_window() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            // The window binds Undo and Redo to the global history; the editor must neither handle them nor stand in their way.
            var ran = new List<RoutedCommand>();
            window.CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, e) => { ran.Add(ApplicationCommands.Undo); e.Handled = true; }, (_, e) => e.CanExecute = true));
            window.CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, e) => { ran.Add(ApplicationCommands.Redo); e.Handled = true; }, (_, e) => e.CanExecute = true));
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "\n  x = 1");
            var text = view.Editor.Text;

            Assert.True(ApplicationCommands.Undo.CanExecute(null, view.Editor.TextArea));
            ApplicationCommands.Undo.Execute(null, view.Editor.TextArea);
            ApplicationCommands.Redo.Execute(null, view.Editor.TextArea);

            Assert.Equal([ApplicationCommands.Undo, ApplicationCommands.Redo], ran);
            Assert.Equal(text, view.Editor.Text);
            Assert.DoesNotContain(view.Editor.TextArea.InputBindings.OfType<KeyBinding>(), b => b.Command == ApplicationCommands.Undo || b.Command == ApplicationCommands.Redo);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_editor_takes_the_focus_when_the_view_is_loaded() => Sta.Run(() =>
    {
        var (view, window) = Create(Tab());
        try
        {
            Assert.True(view.Editor.TextArea.IsKeyboardFocusWithin || ReferenceEquals(FocusManager.GetFocusedElement(window), view.Editor.TextArea));
        }
        finally
        {
            window.Close();
        }
    });

    // The colour of every text run of a document line, as the editor draws it.
    private static List<(string Text, Brush? Brush)> Runs(ObjectEditorView view, int number)
    {
        var document = view.Editor.Document;
        var line = document.GetLineByNumber(number);
        var visual = view.Editor.TextArea.TextView.GetOrConstructVisualLine(line);
        return visual.Elements.OfType<VisualLineText>()
            .Select(e => (Text: document.GetText(line.Offset + e.RelativeTextOffset, e.DocumentLength), Brush: (Brush?)e.TextRunProperties.ForegroundBrush))
            .ToList();
    }

    private static int LineOf(ObjectEditorView view, string text)
    {
        var document = view.Editor.Document;
        return document.Lines.First(l => document.GetText(l).Contains(text, StringComparison.Ordinal)).LineNumber;
    }

    [Fact] public void Intact_lines_are_coloured_from_the_baseline_and_changed_lines_from_their_syntax() => Sta.Run(() =>
    {
        var tab = Tab(LinkResult());
        var (view, window) = Create(tab);
        try
        {
            var accent = (Brush)view.FindResource("Theme.Accent");
            var muted = (Brush)view.FindResource("Theme.TextMuted");
            var number = LineOf(view, "chain = chain1");

            var intact = Runs(view, number);
            Assert.Contains(intact, run => run.Text == "chain1" && ReferenceEquals(run.Brush, accent));
            Assert.Contains(intact, run => run.Text == "chain" && ReferenceEquals(run.Brush, muted));

            var line = view.Editor.Document.GetLineByNumber(number);
            view.Editor.Document.Replace(line.EndOffset - 1, 1, "2");
            Pump(window);

            var changed = Runs(view, number);
            Assert.DoesNotContain(changed, run => ReferenceEquals(run.Brush, accent));
            Assert.Contains(changed, run => run.Text == "chain" && ReferenceEquals(run.Brush, muted));
        }
        finally
        {
            window.Close();
        }
    });

    private static List<TrailingElement> TrailingsOf(ObjectEditorView view, int number)
    {
        var line = view.Editor.Document.GetLineByNumber(number);
        return view.Editor.TextArea.TextView.GetOrConstructVisualLine(line).Elements.OfType<TrailingElement>().ToList();
    }

    [Fact] public void Intact_lines_show_their_trailing_and_changed_lines_do_not() => Sta.Run(() =>
    {
        var tab = Tab(LinkResult());
        var (view, window) = Create(tab);
        try
        {
            var head = Assert.Single(TrailingsOf(view, 1));
            Assert.Contains(head.Pieces, piece => piece.Text.Contains("[db.pack]", StringComparison.Ordinal));
            Assert.Equal(0, head.DocumentLength);
            Assert.Equal(tab.BaselineText, view.Editor.Text);

            var number = LineOf(view, "chain = chain1");
            var line = view.Editor.Document.GetLineByNumber(number);
            view.Editor.Document.Replace(line.EndOffset - 1, 1, "2");
            Pump(window);

            Assert.Empty(TrailingsOf(view, number));
            Assert.Single(TrailingsOf(view, 1));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_caret_cannot_enter_a_trailing() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            view.Editor.TextArea.Caret.Offset = tab.HeadLineLength;
            System.Windows.Documents.EditingCommands.MoveRightByCharacter.Execute(null, view.Editor.TextArea);

            Assert.Equal(tab.HeadLineLength + 1, view.Editor.TextArea.Caret.Offset);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void End_puts_the_caret_in_front_of_the_trailing() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            var caret = view.Editor.TextArea.Caret;
            caret.Offset = 0;
            System.Windows.Documents.EditingCommands.MoveToLineEnd.Execute(null, view.Editor.TextArea);
            Pump(window);

            Assert.Equal(tab.HeadLineLength, caret.Offset);
            Assert.Equal(tab.HeadLineLength, caret.VisualColumn);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void Shift_End_does_not_select_the_trailing() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            view.Editor.TextArea.Caret.Offset = 0;
            System.Windows.Documents.EditingCommands.SelectToLineEnd.Execute(null, view.Editor.TextArea);
            Pump(window);

            var selection = view.Editor.TextArea.Selection;
            Assert.All(selection.Segments, segment => Assert.True(segment.EndVisualColumn <= tab.HeadLineLength));
            Assert.Equal(tab.Text[..tab.HeadLineLength], selection.GetText());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void A_caret_position_behind_the_trailing_is_moved_in_front_of_it() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            var caret = view.Editor.TextArea.Caret;
            var behind = tab.HeadLineLength + 1;
            caret.Position = new TextViewPosition(1, behind, behind);
            Pump(window);

            Assert.Equal(tab.HeadLineLength, caret.VisualColumn);
            Assert.Equal(tab.HeadLineLength, caret.Offset);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_last_shown_line_of_a_folding_keeps_its_trailing() => Sta.Run(() =>
    {
        var tab = Tab(LongResult());
        var (view, window) = Create(tab);
        try
        {
            var folding = Assert.Single(view.Foldings.AllFoldings);
            Assert.True(folding.IsFolded);
            var line = view.Editor.Document.GetLineByOffset(folding.StartOffset);
            var elements = view.Editor.TextArea.TextView.GetOrConstructVisualLine(line).Elements;

            var trailing = elements.ToList().FindIndex(e => e is TrailingElement);
            var folded = elements.ToList().FindIndex(e => e.GetType().Name.Contains("Folding", StringComparison.Ordinal));
            Assert.True(trailing >= 0, "no trailing on the line the folding starts at");
            Assert.True(folded > trailing, "the folding must come after the trailing");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void Typing_schedules_a_check_after_the_delay() => Sta.Run(() =>
    {
        var tab = Tab();
        var checks = 0;
        tab.CheckHandler = _ =>
        {
            checks++;
            return new ObjectChanges([], [], []);
        };
        var (view, window) = Create(tab);
        try
        {
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "\n  x = 1");
            Assert.Equal(0, checks);

            PumpFor(window, ObjectEditorView.CheckDelay / 2);
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "2");
            PumpFor(window, ObjectEditorView.CheckDelay / 2);
            Assert.Equal(0, checks);

            PumpFor(window, ObjectEditorView.CheckDelay + TimeSpan.FromMilliseconds(300));
            Assert.Equal(1, checks);
            Assert.True(tab.IsDiffEmpty);

            view.Editor.Document.Insert(view.Editor.Document.TextLength, "3");
            PumpFor(window, ObjectEditorView.CheckDelay + TimeSpan.FromMilliseconds(300));
            Assert.Equal(2, checks);
        }
        finally
        {
            window.Close();
        }
    });

    // Lets the dispatcher run for the given time, so that timers fire.
    private static void PumpFor(Window window, TimeSpan time)
    {
        var end = DateTime.UtcNow + time;
        while (DateTime.UtcNow < end)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }

    private static BlockDiagnostic Diagnostic(int line, DiagnosticSeverity severity) => new(line, severity, DiagnosticKind.InvalidValue, "detail");

    [Fact] public void A_diagnostic_line_is_underlined_in_the_error_colour() => Sta.Run(() =>
    {
        var tab = Tab();
        var diagnostics = new List<BlockDiagnostic>();
        tab.CheckHandler = _ => new ObjectChanges([], [], diagnostics);
        var (view, window) = Create(tab);
        try
        {
            var error = (Brush)view.FindResource("Theme.ObjectError");
            var warning = (Brush)view.FindResource("Theme.Warning");
            Assert.Empty(view.Diagnostics.Underlines());

            diagnostics.Add(Diagnostic(2, DiagnosticSeverity.Warning));
            diagnostics.Add(Diagnostic(3, DiagnosticSeverity.Warning));
            diagnostics.Add(Diagnostic(3, DiagnosticSeverity.Error));
            diagnostics.Add(Diagnostic(500, DiagnosticSeverity.Error));
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "\n");
            tab.Check();
            Pump(window);

            var underlines = view.Diagnostics.Underlines();
            Assert.Equal([2, 3, 500], underlines.Select(u => u.Line));
            Assert.Same(warning, underlines[0].Brush);
            Assert.Same(error, underlines[1].Brush);

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                view.Diagnostics.Draw(view.Editor.TextArea.TextView, context);
            }

            // Line 500 is not in the document: Draw skips it.
            Assert.NotNull(visual.Drawing);
            Assert.True(visual.Drawing.Children.Count > 0);

            tab.Text = tab.BaselineText;
            Assert.Empty(view.Diagnostics.Underlines());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_tool_tip_of_a_point_comes_from_the_hover_rules() => Sta.Run(() =>
    {
        var tab = Tab(LinkResult());
        var diagnostics = new[] { new BlockDiagnostic(2, DiagnosticSeverity.Error, DiagnosticKind.InvalidValue, "expected an integer") };
        tab.CheckHandler = _ => new ObjectChanges([], [], diagnostics);
        var (view, window) = Create(tab);
        try
        {
            view.Editor.Document.Insert(view.Editor.Document.TextLength, "\n");
            tab.Check();
            Pump(window);
            var textView = view.Editor.TextArea.TextView;
            var number = LineOf(view, "chain = chain1");
            var linkPoint = textView.GetVisualPosition(new TextViewPosition(number, 13), VisualYPosition.LineMiddle) - textView.ScrollOffset;
            var errorPoint = textView.GetVisualPosition(new TextViewPosition(2, 2), VisualYPosition.LineMiddle) - textView.ScrollOffset;

            Assert.Equal(tab.DisplayNameOf(Chain) ?? "building_chains", view.ToolTipAt(new Point(linkPoint.X, linkPoint.Y)));
            Assert.Contains("expected an integer", view.ToolTipAt(new Point(errorPoint.X, errorPoint.Y)));
        }
        finally
        {
            window.Close();
        }
    });

    // 25 rows in one section: it folds after the fifth row.
    private static ObjectBuildResult LongResult()
    {
        var order = new LoadOrder([Vanilla], null, []);
        var rows = Enumerable.Range(0, 25).Select(i => Row("r" + i)).ToArray();
        var view = new ObjectView(Id("a"), Row("v"), [], [new ObjectSection("a_tables", "building", false, rows)], [], order, [Table]);
        return new ObjectBuildResult(view, order, null, new Dictionary<string, string> { [Table] = "sig" }, "loc",
            new LocTable(new Dictionary<string, LocText>(), []));
    }

    [Fact] public void Long_sections_are_folded_with_a_more_rows_title_and_unfolding_is_remembered() => Sta.Run(() =>
    {
        var tab = Tab(LongResult());
        var (view, window) = Create(tab);
        try
        {
            var folding = Assert.Single(view.Foldings.AllFoldings);
            Assert.True(folding.IsFolded);
            Assert.Equal(Texts.ObjectsMoreRows(20), folding.Title);
            Assert.False(tab.IsSectionExpanded(0));

            folding.IsFolded = false;
            Pump(window);
            Assert.True(tab.IsSectionExpanded(0));

            tab.ApplyResult(LongResult());
            Pump(window);
            var rebuilt = Assert.Single(view.Foldings.AllFoldings);
            Assert.False(rebuilt.IsFolded);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void A_folding_follows_an_insert_above_it() => Sta.Run(() =>
    {
        var tab = Tab(LongResult());
        var (view, window) = Create(tab);
        try
        {
            var before = Assert.Single(view.Foldings.AllFoldings).StartOffset;

            view.Editor.Document.Insert(view.Editor.Document.GetLineByNumber(2).Offset, "  new = 1\n");
            Pump(window);

            var folding = Assert.Single(view.Foldings.AllFoldings);
            Assert.Equal(before + "  new = 1\n".Length, folding.StartOffset);
            Assert.True(folding.IsFolded);
        }
        finally
        {
            window.Close();
        }
    });

    private static Point PointOf(ObjectEditorView view, int line, int column)
    {
        var textView = view.Editor.TextArea.TextView;
        var point = textView.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.LineMiddle) - textView.ScrollOffset;
        return new Point(point.X + 1, point.Y);
    }

    [Fact] public void Ctrl_click_on_a_link_navigates_and_a_plain_click_does_not() => Sta.Run(() =>
    {
        var tab = Tab(LinkResult());
        var builds = 0;
        tab.BuildHandler = _ => builds++;
        var (view, window) = Create(tab);
        try
        {
            var number = LineOf(view, "chain = chain1");
            var onName = PointOf(view, number, 4);
            var onLink = PointOf(view, number, 13);

            Assert.False(view.TryOpenLinkAt(onName, false));
            Assert.Equal(0, tab.Position);

            Assert.True(view.TryOpenLinkAt(onLink, false));
            Assert.Equal((1, 1), (tab.Position, builds));
            Assert.Equal(Chain, tab.Current.ToObjectId());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void A_changed_line_is_not_a_link() => Sta.Run(() =>
    {
        var tab = Tab(LinkResult());
        var (view, window) = Create(tab);
        try
        {
            var number = LineOf(view, "chain = chain1");
            var line = view.Editor.Document.GetLineByNumber(number);
            view.Editor.Document.Replace(line.EndOffset - 1, 1, "2");
            Pump(window);

            Assert.False(view.TryOpenLinkAt(PointOf(view, number, 13), false));
            Assert.Equal(0, tab.Position);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void Shift_or_middle_opens_a_new_tab() => Sta.Run(() =>
    {
        var tab = Tab(LinkResult());
        var opened = new List<ObjectRef>();
        tab.OpenHandler = (_, target, newTab) =>
        {
            Assert.True(newTab);
            opened.Add(target);
        };
        var (view, window) = Create(tab);
        try
        {
            var number = LineOf(view, "chain = chain1");

            Assert.True(view.TryOpenLinkAt(PointOf(view, number, 13), true));

            Assert.Equal(Chain, Assert.Single(opened).ToObjectId());
            Assert.Equal(0, tab.Position);
            Assert.Single(tab.History);
        }
        finally
        {
            window.Close();
        }
    });

    // A build with a warning: the block has an empty annotation line under the head that holds only a trailing.
    private static ObjectBuildResult WarningResult()
    {
        var order = new LoadOrder([Vanilla], null, []);
        var view = new ObjectView(Id("a"), Row("v"), [], [], [new ObjectWarning(ObjectWarningKind.SourceProblem, null, "a source problem")], order, [Table]);
        return new ObjectBuildResult(view, order, null, new Dictionary<string, string> { [Table] = "sig" }, "loc",
            new LocTable(new Dictionary<string, LocText>(), []));
    }

    [Fact] public void The_caret_can_enter_and_leave_a_line_that_holds_only_a_trailing() => Sta.Run(() =>
    {
        var tab = Tab(WarningResult());
        var (view, window) = Create(tab);
        try
        {
            var area = view.Editor.TextArea;
            var document = view.Editor.Document;
            Assert.Equal(0, document.GetLineByNumber(2).Length);
            Assert.Single(TrailingsOf(view, 2));

            area.Caret.Offset = 0;
            System.Windows.Documents.EditingCommands.MoveDownByLine.Execute(null, area);
            Pump(window);
            Assert.Equal(2, area.Caret.Line);

            area.Caret.Position = new TextViewPosition(2, 1);
            System.Windows.Documents.EditingCommands.MoveRightByCharacter.Execute(null, area);
            Pump(window);
            Assert.Equal(3, area.Caret.Line);

            System.Windows.Documents.EditingCommands.MoveLeftByCharacter.Execute(null, area);
            Pump(window);
            Assert.Equal(2, area.Caret.Line);
            System.Windows.Documents.EditingCommands.MoveUpByLine.Execute(null, area);
            Pump(window);
            Assert.Equal(1, area.Caret.Line);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void A_tab_with_unapplied_text_shows_colours_trailings_and_foldings_when_its_view_is_made_again() => Sta.Run(() =>
    {
        var tab = Tab(LongResult());
        var number = 2;
        var (first, firstWindow) = Create(tab);
        try
        {
            var line = first.Editor.Document.GetLineByNumber(number);
            first.Editor.Document.Replace(line.Offset, line.Length, "  effect = changed");
        }
        finally
        {
            firstWindow.Close();
        }

        Assert.True(tab.HasUnappliedText);
        var (view, window) = Create(tab);
        try
        {
            Assert.Equal(tab.Text, view.Editor.Text);
            Assert.Single(TrailingsOf(view, 1));
            Assert.Empty(TrailingsOf(view, number));
            var folding = Assert.Single(view.Foldings.AllFoldings);
            Assert.True(folding.IsFolded);
            Assert.Single(TrailingsOf(view, 4));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_context_menu_of_the_editor_offers_copy_as() => Sta.Run(() =>
    {
        var tab = Tab();
        var (view, window) = Create(tab);
        try
        {
            var menu = Assert.IsType<System.Windows.Controls.ContextMenu>(view.Editor.ContextMenu);
            menu.PlacementTarget = view.Editor;
            menu.IsOpen = true;
            Pump(window);
            var items = menu.Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            Assert.Equal([ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste, tab.CopyCommand], items.Select(item => item.Command));
            Assert.Equal([Texts.ObjectsEditorCut, Texts.ObjectsEditorCopy, Texts.ObjectsEditorPaste, Texts.ObjectsCopyAs], items.Select(item => item.Header));
            menu.IsOpen = false;
        }
        finally
        {
            window.Close();
        }
    });
}

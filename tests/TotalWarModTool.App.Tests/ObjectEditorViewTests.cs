using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
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
}

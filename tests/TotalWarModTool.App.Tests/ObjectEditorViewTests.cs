using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using TotalWarModTool.Localization;
using TotalWarModTool.Objects;
using TotalWarModTool.Schema;

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
}

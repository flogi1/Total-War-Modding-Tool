using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using TotalWarModTool.Localization;
using TotalWarModTool.Objects;
using TotalWarModTool.Schema;

namespace TotalWarModTool.App.Tests;

public class ObjectBlockViewTests
{
    private const string Table = "building_levels_tables";
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly TableSource Vanilla = new(SourceKind.Vanilla, "db.pack", @"D:\game\db.pack");

    private static ObjectId Id(string key) => new(Table, [key]);

    private static FieldValue Plain(string name, string text) => new(name, FieldType.StringU8, text, null, ReferenceResolution.None, null, null);

    private static ObjectRow Row(string value) => new([Plain("effect", value)], new RowProvenance(Vanilla, [], false, false), null, null);

    // Sections sorted by table name, no category (the table names are the titles): index 0 has two rows, index 1 has 25 (collapsed until expanded).
    private static ObjectView View() => new(
        Id("a"), Row("v"), [],
        [
            new ObjectSection("a_tables", "building", false, [Row("1"), Row("2")]),
            new ObjectSection("b_tables", "building", false, Enumerable.Range(0, 25).Select(i => Row("r" + i)).ToArray()),
        ],
        [], new LoadOrder([Vanilla], null, []), [Table]);

    private static ObjectBuildResult Result(params (string Key, string Text)[] loc) => new(
        View(), new LoadOrder([Vanilla], null, []), null, new Dictionary<string, string> { [Table] = "sig" }, "loc",
        new LocTable(loc.ToDictionary(entry => entry.Key, entry => new LocText(entry.Text, Vanilla)), []));

    private static ObjectTabViewModel Tab() => new(Texts, _ => { }, [new ObjectRef(Table, ["a"], null)], 0, key => key);

    private static ObjectLine Line(int indent, params ObjectSpan[] spans) => new(spans, indent);

    // The view inside a shown window with the tab as DataContext. The test host has no Application, so the styles of Controls.xaml go into the view's resources.
    private static (ObjectBlockView View, Window Window) Create(ObjectTabViewModel tab)
    {
        var view = new ObjectBlockView { DataContext = tab };
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

    private static List<Paragraph> Paragraphs(ObjectBlockView view) => view.Document!.Blocks.Cast<Paragraph>().ToList();

    private static IEnumerable<Hyperlink> Links(ObjectBlockView view) => Paragraphs(view).SelectMany(p => p.Inlines.OfType<Hyperlink>());

    private static string TextOf(Hyperlink link) => string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text));

    private static Hyperlink LinkWithText(ObjectBlockView view, string text) => Links(view).Single(link => TextOf(link) == text);

    private static void Click(Hyperlink link) => link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, link));

    [Fact] public void Every_span_kind_maps_to_a_theme_token()
    {
        var tokens = ThemePalette.All.Select(token => token.Key).ToHashSet();
        foreach (var kind in Enum.GetValues<SpanKind>())
        {
            var key = ObjectBlockView.BrushKeyOf(kind);
            Assert.StartsWith("Theme.", key);
            Assert.Contains(key["Theme.".Length..], tokens);
        }

        Assert.Equal(
            ["Theme.ObjectVanilla", "Theme.ObjectMod", "Theme.ObjectProject", "Theme.ObjectLocText", "Theme.Warning", "Theme.TextMuted", "Theme.TextMuted",
             "Theme.TextMuted", "Theme.TextMuted", "Theme.Accent", "Theme.Accent", "Theme.Text", "Theme.Text", "Theme.Text", "Theme.Text", "Theme.Text"],
            new[]
            {
                SpanKind.ProvenanceVanilla, SpanKind.ProvenanceMod, SpanKind.ProvenanceProject, SpanKind.LocText, SpanKind.Warning, SpanKind.FieldName,
                SpanKind.Overridden, SpanKind.Duplicate, SpanKind.OlderLayout, SpanKind.Link, SpanKind.Collapsed, SpanKind.Normal, SpanKind.TableName,
                SpanKind.Key, SpanKind.Value, SpanKind.Title,
            }.Select(ObjectBlockView.BrushKeyOf));
    }

    [Fact] public void Lines_become_paragraphs_with_runs_and_hyperlinks() => Sta.Run(() =>
    {
        var (view, window) = Create(Tab());
        try
        {
            view.Lines =
            [
                Line(0, new ObjectSpan("building_levels", SpanKind.TableName), new ObjectSpan(" ", SpanKind.Normal), new ObjectSpan("a", SpanKind.Key)),
                Line(1, new ObjectSpan("effect", SpanKind.FieldName), new ObjectSpan(" ", SpanKind.Normal), new ObjectSpan("b", SpanKind.Link, Target: Id("b"))),
                Line(2, new ObjectSpan("old", SpanKind.Overridden), new ObjectSpan("broken", SpanKind.Warning, ToolTip: "target unknown")),
            ];
            Pump(window);

            var paragraphs = Paragraphs(view);
            Assert.Equal(3, paragraphs.Count);
            Assert.Equal([0d, 24d, 48d], paragraphs.Select(p => p.Margin.Left));
            Assert.Equal("Consolas", view.Document!.FontFamily.Source);

            var head = paragraphs[0].Inlines.Cast<Run>().ToList();
            Assert.Equal(["building_levels", " ", "a"], head.Select(run => run.Text));
            Assert.Equal([FontWeights.Bold, FontWeights.Normal, FontWeights.Bold], head.Select(run => run.FontWeight));

            var link = Assert.Single(Links(view));
            Assert.Equal("b", TextOf(link));
            Assert.Same(view.FindResource("ThemeObjectLink"), link.Style);

            var last = paragraphs[2].Inlines.Cast<Run>().ToList();
            Assert.Equal(TextDecorations.Strikethrough, last[0].TextDecorations);
            Assert.Equal("target unknown", last[1].ToolTip);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void A_click_on_a_link_navigates_and_a_shift_click_opens_a_new_tab() => Sta.Run(() =>
    {
        var tab = Tab();
        var builds = 0;
        var opened = new List<(ObjectRef Target, bool NewTab)>();
        tab.BuildHandler = _ => builds++;
        tab.OpenHandler = (_, target, newTab) => opened.Add((target, newTab));
        var (view, window) = Create(tab);
        try
        {
            view.Lines = [Line(0, new ObjectSpan("b", SpanKind.Link, Target: Id("b")))];
            Pump(window);
            var link = Assert.Single(Links(view));

            Click(link);
            Assert.Equal((1, "b"), (builds, tab.Current.JoinedKey));
            Assert.Empty(opened);

            // Shift held cannot be simulated through the input manager: the click handler hands the modifier to this method.
            view.OnLinkClicked(Id("c"), newTab: true);
            Assert.Equal([(new ObjectRef(Table, ["c"], null), true)], opened);

            // The middle button on a link opens a new tab too.
            link.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = UIElement.MouseDownEvent, Source = link });
            Assert.Equal(2, opened.Count);
            Assert.Equal((new ObjectRef(Table, ["b"], null), true), opened[1]);
            Assert.Equal(1, builds);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void A_click_on_the_collapsed_line_expands_the_section() => Sta.Run(() =>
    {
        var tab = Tab();
        tab.ApplyResult(Result());
        var builds = 0;
        tab.BuildHandler = _ => builds++;
        var (view, window) = Create(tab);
        try
        {
            view.SetBinding(ObjectBlockView.LinesProperty, new Binding(nameof(ObjectTabViewModel.Lines)) { Mode = BindingMode.OneWay });
            window.Height = 150;
            Pump(window);
            var collapsedText = tab.Lines.SelectMany(line => line.Spans).Single(span => span.Kind == SpanKind.Collapsed).Text;
            var before = tab.Lines;
            var scroller = (ScrollViewer)view.Viewer.Template.FindName("PART_ContentHost", view.Viewer);
            scroller.ScrollToVerticalOffset(30);
            Pump(window);
            Assert.Equal(30, scroller.VerticalOffset);

            Click(LinkWithText(view, collapsedText));
            Pump(window);

            Assert.Equal(30, scroller.VerticalOffset);                              // expanding keeps the scroll position
            Assert.NotSame(before, tab.Lines);
            Assert.Contains(1, tab.ExpandedSections);
            Assert.DoesNotContain(Links(view), link => TextOf(link) == collapsedText);
            Assert.Equal(tab.Lines.Count, Paragraphs(view).Count);

            // The title of the section (the table name, as there is no category) collapses it again, and a second click expands it.
            Click(LinkWithText(view, "b"));
            Pump(window);
            Assert.Empty(tab.ExpandedSections);
            Assert.Single(Links(view), link => TextOf(link) == collapsedText);
            view.OnSectionTitleClicked(1);
            Assert.Contains(1, tab.ExpandedSections);
            Assert.Equal(0, builds);

            // A new result is another document: it starts at the top.
            tab.ApplyResult(Result());
            Pump(window);
            Assert.Equal(0, scroller.VerticalOffset);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact] public void The_tool_tip_of_a_link_shows_the_display_name() => Sta.Run(() =>
    {
        var tab = Tab();
        tab.ApplyResult(Result(("effects_onscreen_name_e1", "One")));
        var (view, window) = Create(tab);
        try
        {
            view.Lines =
            [
                Line(0, new ObjectSpan("e1", SpanKind.Link, Target: new ObjectId("effects_tables", ["e1"]))),
                Line(0, new ObjectSpan("u1", SpanKind.Link, Target: new ObjectId("units_tables", ["u1"]))),
            ];
            Pump(window);
            var named = LinkWithText(view, "e1");
            var unnamed = LinkWithText(view, "u1");
            Assert.NotNull(named.ToolTip);                                          // a placeholder, so that ToolTipOpening fires

            // ToolTipEventArgs cannot be created outside WPF: the ToolTipOpening handler hands the link to this method.
            view.FillToolTip(named);
            view.FillToolTip(unnamed);

            Assert.Equal("One", named.ToolTip);
            Assert.Equal("units", unnamed.ToolTip);                                  // no display name: the short table name
        }
        finally
        {
            window.Close();
        }
    });
}

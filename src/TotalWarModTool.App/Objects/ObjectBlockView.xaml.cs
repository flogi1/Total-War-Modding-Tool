using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// Shows the lines of an object tab as a read-only FlowDocument. Pure view wiring: a click on a link asks the tab to navigate, a click on a
/// collapsed line asks it to expand.
/// </summary>
public sealed partial class ObjectBlockView : UserControl
{
    /// <summary>The lines of the tab; a change rebuilds the document.</summary>
    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(IReadOnlyList<ObjectLine>), typeof(ObjectBlockView), new PropertyMetadata(null, OnLinesChanged));

    private const double IndentWidth = 24;

    // The key of the style in Controls.xaml that gives the links the colour of the theme and the underline on hover.
    private const string LinkStyleKey = "ThemeObjectLink";

    // The scroll position to keep while a section is expanded or collapsed; every other rebuild (another object, another tab, a new result)
    // starts at the top. The rebuild runs synchronously inside the toggle, through the change notification of Lines.
    private double? _keptOffset;

    public ObjectBlockView()
    {
        InitializeComponent();
    }

    public IReadOnlyList<ObjectLine>? Lines
    {
        get => (IReadOnlyList<ObjectLine>?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    private ObjectTabViewModel? Tab => DataContext as ObjectTabViewModel;

    /// <summary>
    /// The brush key of a span kind, for the test and the builder: Theme.ObjectVanilla, Theme.ObjectMod, Theme.ObjectProject, Theme.ObjectLocText,
    /// Theme.Warning, Theme.TextMuted (FieldName, Overridden, Duplicate, OlderLayout), Theme.Accent (Link, Collapsed), Theme.Text (the rest).
    /// </summary>
    internal static string BrushKeyOf(SpanKind kind) => kind switch
    {
        SpanKind.ProvenanceVanilla => "Theme.ObjectVanilla",
        SpanKind.ProvenanceMod => "Theme.ObjectMod",
        SpanKind.ProvenanceProject => "Theme.ObjectProject",
        SpanKind.LocText => "Theme.ObjectLocText",
        SpanKind.Warning => "Theme.Warning",
        SpanKind.FieldName or SpanKind.Overridden or SpanKind.Duplicate or SpanKind.OlderLayout => "Theme.TextMuted",
        SpanKind.Link or SpanKind.Collapsed => "Theme.Accent",
        _ => "Theme.Text",
    };

    /// <summary>A link was clicked (Shift held or the middle button: <paramref name="newTab"/>).</summary>
    internal void OnLinkClicked(ObjectId target, bool newTab) => Tab?.NavigateTo(target, newTab);

    /// <summary>The title of a section was clicked: an expanded section collapses, any other expands.</summary>
    internal void OnSectionTitleClicked(int section)
    {
        if (Tab is not { } tab)
        {
            return;
        }

        if (tab.ExpandedSections.Contains(section))
        {
            KeepingScrollPosition(() => tab.CollapseSection(section));
        }
        else
        {
            KeepingScrollPosition(() => tab.ExpandSection(section));
        }
    }

    /// <summary>The tool tip of a link is about to open: the display name of its target, else the short name of its table.</summary>
    internal void FillToolTip(Hyperlink link)
    {
        if (link.Tag is ObjectId target && Tab is { } tab)
        {
            link.ToolTip = tab.DisplayNameOf(target) ?? ObjectText.ShortTableName(target.Table);
        }
    }

    private static void OnLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ObjectBlockView)d).Rebuild();

    private void Rebuild()
    {
        // A clicked link has the keyboard focus and is removed with the old document: the focus goes back to the viewer, so that the keys of
        // the tab (Alt+Left, Alt+Right) keep working.
        var hadFocus = Viewer.IsKeyboardFocusWithin;
        Document.Blocks.Clear();
        foreach (var line in Lines ?? [])
        {
            Document.Blocks.Add(ParagraphOf(line));
        }

        if (hadFocus)
        {
            Viewer.Focus();
        }

        if (ContentScroller() is not { } scroller)
        {
            return;
        }

        // Set after the new document has been laid out, else the offset is clamped to the old extent.
        var offset = _keptOffset ?? 0;
        Dispatcher.BeginInvoke(() => scroller.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
    }

    private void KeepingScrollPosition(Action toggle)
    {
        _keptOffset = ContentScroller()?.VerticalOffset;
        try
        {
            toggle();
        }
        finally
        {
            _keptOffset = null;
        }
    }

    // The scroll viewer inside the template of the document viewer; null while the viewer has no template yet.
    private ScrollViewer? ContentScroller()
    {
        Viewer.ApplyTemplate();
        return Viewer.Template?.FindName("PART_ContentHost", Viewer) as ScrollViewer;
    }

    private Paragraph ParagraphOf(ObjectLine line)
    {
        var paragraph = new Paragraph { Margin = new Thickness(line.Indent * IndentWidth, 0, 0, 0) };
        foreach (var span in line.Spans)
        {
            paragraph.Inlines.Add(InlineOf(span));
        }

        return paragraph;
    }

    private Inline InlineOf(ObjectSpan span)
    {
        var run = new Run(span.Text);
        run.SetResourceReference(TextElement.ForegroundProperty, BrushKeyOf(span.Kind));
        if (span.Kind is SpanKind.TableName or SpanKind.Key or SpanKind.Title)
        {
            run.FontWeight = FontWeights.Bold;
        }

        if (span.Kind == SpanKind.Overridden)
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }

        if (span.ToolTip is not null)
        {
            run.ToolTip = span.ToolTip;
        }

        // A table name without a section is the head line, not a title.
        return span switch
        {
            { Kind: SpanKind.Link, Target: { } target } => TargetLink(run, target),
            { Kind: SpanKind.Collapsed, Section: { } section } => Link(run, () => KeepingScrollPosition(() => Tab?.ExpandSection(section))),
            { Kind: SpanKind.Title or SpanKind.TableName, Section: { } section } => Link(run, () => OnSectionTitleClicked(section)),
            _ => run,
        };
    }

    private Hyperlink TargetLink(Run run, ObjectId target)
    {
        var link = Link(run, () => OnLinkClicked(target, (Keyboard.Modifiers & ModifierKeys.Shift) != 0));
        link.Tag = target;

        // A placeholder, so that ToolTipOpening fires; the display name is looked up only when the tool tip opens.
        link.ToolTip = ObjectText.ShortTableName(target.Table);
        link.ToolTipOpening += (_, _) => FillToolTip(link);
        link.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                OnLinkClicked(target, newTab: true);
                e.Handled = true;
            }
        };
        return link;
    }

    private Hyperlink Link(Run run, Action click)
    {
        var link = new Hyperlink(run);
        link.SetResourceReference(FrameworkContentElement.StyleProperty, LinkStyleKey);
        link.Click += (_, _) => click();
        return link;
    }
}

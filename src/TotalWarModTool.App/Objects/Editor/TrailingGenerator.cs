using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// The trailing of a line as one visual element behind the text of the line. It has no document text (DocumentLength 0), so a selection never
/// holds it, and the caret has no stop inside it.
/// </summary>
internal sealed class TrailingElement : FormattedTextElement
{
    public TrailingElement(FormattedText text, IReadOnlyList<TrailingPiece> pieces)
        : base(text, 0)
    {
        Pieces = pieces;
    }

    public IReadOnlyList<TrailingPiece> Pieces { get; }

    // The base class builds a TextLine from its text field, which a FormattedText element does not have.
    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new FormattedTextRun(this, TextRunProperties);

    // Without this the visual line adds an implicit caret stop behind the last element, which would be a stop inside the trailing.
    public override bool HandlesLineBorders => true;

    // The only stop is the place in front of the trailing, so a line that holds nothing else still has a caret position; none lies inside it.
    public override int GetNextCaretPosition(int visualColumn, LogicalDirection direction, CaretPositioningMode mode) =>
        direction == LogicalDirection.Forward
            ? visualColumn < VisualColumn ? VisualColumn : -1
            : visualColumn > VisualColumn ? VisualColumn : -1;
}

/// <summary>
/// Draws the trailing of every intact line behind its text: one element, not editable, not selectable, muted (Theme.ObjectTrailing) except
/// provenance and warning pieces, which keep their span colours. View wiring only; the texts come from TrailingText.
/// </summary>
internal sealed class TrailingGenerator : VisualLineElementGenerator
{
    private readonly Func<BlockLineMap?> _map;
    private readonly Func<ObjectTabViewModel?> _tab;
    private readonly Func<string, Brush?> _brushOf;

    public TrailingGenerator(Func<BlockLineMap?> map, Func<ObjectTabViewModel?> tab, Func<string, Brush?> brushOf)
    {
        _map = map;
        _tab = tab;
        _brushOf = brushOf;
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        return _map()?.IntactLineAt(line.LineNumber) is { Trailing.Count: > 0 } && line.EndOffset >= startOffset ? line.EndOffset : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var tab = _tab();
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (tab is null || _map()?.IntactLineAt(line.LineNumber) is not { } baseline)
        {
            return null;
        }

        var pieces = TrailingText.Of(baseline, tab.Texts, tab.TranslateTitle);
        if (pieces.Count == 0)
        {
            return null;
        }

        var properties = CurrentContext.GlobalTextRunProperties;
        var muted = _brushOf(TrailingText.BrushKeyOf(SpanKind.Normal)) ?? properties.ForegroundBrush;
        var text = new FormattedText(string.Concat(pieces.Select(p => p.Text)), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            properties.Typeface, properties.FontRenderingEmSize, muted, VisualTreeHelper.GetDpi(CurrentContext.TextView).PixelsPerDip);

        var start = 0;
        foreach (var piece in pieces)
        {
            if (_brushOf(TrailingText.BrushKeyOf(piece.Kind)) is { } brush && piece.Text.Length > 0)
            {
                text.SetForegroundBrush(brush, start, piece.Text.Length);
            }

            if (piece.Kind == SpanKind.Overridden && piece.Text.Length > 0)
            {
                text.SetTextDecorations(TextDecorations.Strikethrough, start, piece.Text.Length);
            }

            start += piece.Text.Length;
        }

        return new TrailingElement(text, pieces);
    }
}

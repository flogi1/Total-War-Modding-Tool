using ICSharpCode.AvalonEdit.Document;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>One resolved document line: the baseline line it is (intact) or null, and the spans to colour it with.</summary>
public sealed record ResolvedLine(BlockLine? Baseline, IReadOnlyList<TextSpan> Spans, bool IsIntact);

/// <summary>
/// Maps the lines of a TextDocument to the lines of the BlockText the text came from. One anchor per baseline line; a baseline line is intact
/// when the document line at its anchor has exactly the baseline text (indent included). Intact lines carry the spans and trailing of the
/// baseline; every other line gets LineSyntax spans and no trailing. Created for one (document, block) pair; the document's text must be
/// block.Text at creation. Lives on the thread of the document; Dispose unsubscribes it from the document.
/// </summary>
public sealed class BlockLineMap : IDisposable
{
    private readonly TextDocument _document;
    private readonly BlockText _block;
    private readonly TextAnchor[] _anchors;
    private readonly string?[] _expected;
    private readonly Dictionary<int, int> _indexOfLine = [];
    private readonly Dictionary<int, int> _lineOfIndex = [];
    private bool _dirty = true;

    public BlockLineMap(TextDocument document, BlockText block)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        _document = document;
        _block = block;
        _anchors = new TextAnchor[block.Lines.Count];
        _expected = new string?[block.Lines.Count];
        for (var i = 0; i < _anchors.Length; i++)
        {
            var offset = document.GetLineByNumber(Math.Min(i + 1, document.LineCount)).Offset;
            _anchors[i] = document.CreateAnchor(offset);
            _anchors[i].MovementType = AnchorMovementType.AfterInsertion;
            _anchors[i].SurviveDeletion = false;
        }

        document.Changed += OnDocumentChanged;
    }

    /// <summary>The resolution of a 1-based document line, from the current state of the document.</summary>
    public ResolvedLine Resolve(int documentLineNumber)
    {
        if (documentLineNumber < 1 || documentLineNumber > _document.LineCount)
        {
            return new ResolvedLine(null, [], false);
        }

        if (IntactLineAt(documentLineNumber) is { } baseline)
        {
            if (baseline.Spans.Count == 0)
            {
                return new ResolvedLine(baseline, [], true);
            }

            var spans = new List<TextSpan>(baseline.Spans.Count + 1);
            if (baseline.Indent > 0)
            {
                spans.Add(new TextSpan(new string(' ', baseline.Indent * 2), SpanKind.Normal));
            }

            spans.AddRange(baseline.Spans);
            return new ResolvedLine(baseline, spans, true);
        }

        var line = _document.GetLineByNumber(documentLineNumber);
        return new ResolvedLine(null, LineSyntax.Classify(_document.GetText(line.Offset, line.Length), documentLineNumber == 1), false);
    }

    /// <summary>The baseline line that is intact at this document line, or null. For the trailing generator and the link lookup.</summary>
    public BlockLine? IntactLineAt(int documentLineNumber)
    {
        Refresh();
        return _indexOfLine.TryGetValue(documentLineNumber, out var index) ? _block.Lines[index] : null;
    }

    /// <summary>The 1-based document line of a baseline line index while it is intact, or null (deleted or changed).</summary>
    public int? DocumentLineOf(int baselineIndex)
    {
        Refresh();
        return _lineOfIndex.TryGetValue(baselineIndex, out var line) ? line : null;
    }

    public void Dispose() => _document.Changed -= OnDocumentChanged;

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e) => _dirty = true;

    private void Refresh()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _indexOfLine.Clear();
        _lineOfIndex.Clear();
        for (var i = 0; i < _anchors.Length; i++)
        {
            var anchor = _anchors[i];
            if (anchor.IsDeleted)
            {
                continue;
            }

            var number = anchor.Line;
            if (_indexOfLine.ContainsKey(number) || !MatchesBaseline(i, number))
            {
                continue;
            }

            _indexOfLine[number] = i;
            _lineOfIndex[i] = number;
        }
    }

    private bool MatchesBaseline(int index, int documentLineNumber)
    {
        var expected = _expected[index] ??= ExpectedText(_block.Lines[index]);
        var line = _document.GetLineByNumber(documentLineNumber);
        return line.Length == expected.Length && string.Equals(_document.GetText(line.Offset, line.Length), expected, StringComparison.Ordinal);
    }

    private static string ExpectedText(BlockLine line) =>
        line.Spans.Count == 0 ? string.Empty : new string(' ', line.Indent * 2) + string.Concat(line.Spans.Select(span => span.Text));
}

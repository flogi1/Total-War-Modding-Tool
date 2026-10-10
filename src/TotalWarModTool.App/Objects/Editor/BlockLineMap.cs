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
    private const long MaxAlignedPairs = 4_000_000;

    private readonly TextAnchor?[] _anchors;
    private readonly string?[] _expected;
    private readonly Dictionary<int, int> _indexOfLine = [];
    private readonly Dictionary<int, int> _lineOfIndex = [];
    private bool _dirty = true;

    /// <summary>The document holds exactly the text of the block.</summary>
    public BlockLineMap(TextDocument document, BlockText block)
        : this(document, block, Enumerable.Range(1, block.Lines.Count).Select(line => (int?)Math.Min(line, document.LineCount)).ToArray())
    {
    }

    private BlockLineMap(TextDocument document, BlockText block, int?[] lineOfIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        _document = document;
        _block = block;
        _anchors = new TextAnchor?[block.Lines.Count];
        _expected = new string?[block.Lines.Count];
        for (var i = 0; i < _anchors.Length; i++)
        {
            if (lineOfIndex[i] is not { } line)
            {
                continue;
            }

            var anchor = document.CreateAnchor(document.GetLineByNumber(line).Offset);
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            anchor.SurviveDeletion = false;
            _anchors[i] = anchor;
        }

        document.Changed += OnDocumentChanged;
    }

    /// <summary>
    /// The map for a document whose text may differ from the block (a tab with unapplied text that is shown again): the lines of the block are
    /// matched to the lines of the document in order by equal text, the others have no anchor and are never intact. Beyond about four million line
    /// pairs only the common beginning and end of the two texts are matched.
    /// </summary>
    public static BlockLineMap Create(TextDocument document, BlockText block)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        if (string.Equals(document.Text, block.Text, StringComparison.Ordinal))
        {
            return new BlockLineMap(document, block);
        }

        var expected = block.Lines.Select(ExpectedText).ToArray();
        var actual = document.Lines.Select(line => document.GetText(line.Offset, line.Length)).ToArray();
        return new BlockLineMap(document, block, Align(expected, actual));
    }

    // For every expected line the 1-based index of the actual line it matches, or null: an order-preserving matching of equal lines (longest common subsequence).
    private static int?[] Align(string[] expected, string[] actual)
    {
        var result = new int?[expected.Length];
        var head = 0;
        while (head < expected.Length && head < actual.Length && expected[head] == actual[head])
        {
            result[head] = head + 1;
            head++;
        }

        var tail = 0;
        while (tail < expected.Length - head && tail < actual.Length - head && expected[^(tail + 1)] == actual[^(tail + 1)])
        {
            result[expected.Length - 1 - tail] = actual.Length - tail;
            tail++;
        }

        var n = expected.Length - head - tail;
        var m = actual.Length - head - tail;
        if (n == 0 || m == 0 || (long)n * m > MaxAlignedPairs)
        {
            return result;
        }

        // lengths[i, j]: the common subsequence of the last n-i expected and m-j actual lines of the middle part.
        var lengths = new ushort[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lengths[i, j] = expected[head + i] == actual[head + j]
                    ? (ushort)(lengths[i + 1, j + 1] + 1)
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (expected[head + i] == actual[head + j])
            {
                result[head + i] = head + j + 1;
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return result;
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
            if (anchor is null || anchor.IsDeleted)
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

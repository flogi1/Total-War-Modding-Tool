using ICSharpCode.AvalonEdit.Document;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App.Tests;

public class BlockLineMapTests
{
    private static readonly ObjectId Chain = new("building_chains_tables", ["chain1"]);

    private static BlockLine Line(BlockLineKind kind, int indent, TextSpan[] spans, TextSpan[]? trailing = null) =>
        new(kind, spans, trailing ?? [], indent);

    // 1 head, 2 effect = v, 3 chain = chain1 (link), 4 blank, 5 section head, 6 row head.
    private static BlockText Block() => new(
    [
        Line(BlockLineKind.Head, 0, [new("building_levels", SpanKind.TableName), new(" ", SpanKind.Normal), new("b1", SpanKind.Key)],
            [new("[db.pack]", SpanKind.ProvenanceVanilla)]),
        Line(BlockLineKind.RootField, 1, [new("effect", SpanKind.FieldName), new(" = ", SpanKind.Normal), new("v", SpanKind.Value)]),
        Line(BlockLineKind.RootField, 1, [new("chain", SpanKind.FieldName), new(" = ", SpanKind.Normal), new("chain1", SpanKind.Link, Chain)]),
        Line(BlockLineKind.Blank, 0, []),
        Line(BlockLineKind.SectionHead, 0, [new("effects", SpanKind.TableName)], [new("(1)", SpanKind.Normal)]),
        Line(BlockLineKind.RowHead, 1, [new("u1", SpanKind.Value)]),
    ], []);

    private static (TextDocument Document, BlockText Block, BlockLineMap Map) Create()
    {
        var block = Block();
        var document = new TextDocument(block.Text);
        return (document, block, new BlockLineMap(document, block));
    }

    private static void ReplaceLineText(TextDocument document, int number, string text)
    {
        var line = document.GetLineByNumber(number);
        document.Replace(line.Offset, line.Length, text);
    }

    [Fact]
    public void Every_line_is_intact_right_after_creation()
    {
        var (document, block, map) = Create();

        for (var i = 1; i <= block.Lines.Count; i++)
        {
            var resolved = map.Resolve(i);
            Assert.True(resolved.IsIntact, $"line {i}");
            Assert.Same(block.Lines[i - 1], resolved.Baseline);
            Assert.Same(block.Lines[i - 1], map.IntactLineAt(i));
            Assert.Equal(i, map.DocumentLineOf(i - 1));
            Assert.Equal(document.GetText(document.GetLineByNumber(i)), string.Concat(resolved.Spans.Select(s => s.Text)));
        }
    }

    [Fact]
    public void An_edited_line_is_not_intact_and_gets_syntax_spans()
    {
        var (document, _, map) = Create();

        ReplaceLineText(document, 2, "  effect = w");

        var resolved = map.Resolve(2);
        Assert.False(resolved.IsIntact);
        Assert.Null(resolved.Baseline);
        Assert.Null(map.IntactLineAt(2));
        Assert.Equal(LineSyntax.Classify("  effect = w", false).Select(s => (s.Text, s.Kind)), resolved.Spans.Select(s => (s.Text, s.Kind)));
        Assert.Null(map.DocumentLineOf(1));
        Assert.True(map.Resolve(1).IsIntact);
        Assert.True(map.Resolve(3).IsIntact);
        Assert.True(map.Resolve(6).IsIntact);
    }

    [Fact]
    public void A_line_edited_back_to_its_text_is_intact_again()
    {
        var (document, block, map) = Create();

        ReplaceLineText(document, 2, "  effect = w");
        Assert.False(map.Resolve(2).IsIntact);
        ReplaceLineText(document, 2, "  effect = v");

        var resolved = map.Resolve(2);
        Assert.True(resolved.IsIntact);
        Assert.Same(block.Lines[1], resolved.Baseline);
        Assert.Equal(["  ", "effect", " = ", "v"], resolved.Spans.Select(s => s.Text));
    }

    [Fact]
    public void A_character_typed_and_deleted_again_leaves_the_line_intact()
    {
        var (document, _, map) = Create();
        var line = document.GetLineByNumber(2);

        document.Insert(line.Offset + 2, "x");
        Assert.False(map.Resolve(2).IsIntact);
        document.Remove(line.Offset + 2, 1);

        Assert.True(map.Resolve(2).IsIntact);
    }

    [Fact]
    public void Lines_after_an_insert_stay_intact()
    {
        var (document, block, map) = Create();

        document.Insert(document.GetLineByNumber(2).Offset, "  x = 1\n");

        Assert.False(map.Resolve(2).IsIntact);
        var moved = map.Resolve(3);
        Assert.True(moved.IsIntact);
        Assert.Same(block.Lines[1], moved.Baseline);
        Assert.Equal(3, map.DocumentLineOf(1));
        Assert.Equal(4, map.DocumentLineOf(2));
        Assert.Equal(7, map.DocumentLineOf(5));
    }

    [Fact]
    public void A_deleted_and_retyped_line_is_not_intact()
    {
        var (document, _, map) = Create();
        var line = document.GetLineByNumber(2);

        document.Remove(line.Offset, line.TotalLength);
        document.Insert(document.GetLineByNumber(2).Offset, "  effect = v\n");

        Assert.False(map.Resolve(2).IsIntact);
        Assert.Null(map.DocumentLineOf(1));
        Assert.True(map.Resolve(3).IsIntact);
    }

    [Fact]
    public void A_deleted_line_lets_the_next_one_keep_its_own_identity()
    {
        var (document, block, map) = Create();
        var line = document.GetLineByNumber(2);

        document.Remove(line.Offset, line.TotalLength);

        var resolved = map.Resolve(2);
        Assert.True(resolved.IsIntact);
        Assert.Same(block.Lines[2], resolved.Baseline);
        Assert.Null(map.DocumentLineOf(1));
        Assert.Equal(2, map.DocumentLineOf(2));
    }

    [Fact]
    public void The_head_line_is_classified_as_head_when_changed()
    {
        var (document, _, map) = Create();

        ReplaceLineText(document, 1, "building_levels b2");

        var resolved = map.Resolve(1);
        Assert.False(resolved.IsIntact);
        Assert.Equal([SpanKind.TableName, SpanKind.Normal, SpanKind.Key], resolved.Spans.Select(s => s.Kind));
    }

    [Fact]
    public void A_blank_line_is_intact_while_it_is_empty()
    {
        var (document, _, map) = Create();
        Assert.True(map.Resolve(4).IsIntact);

        ReplaceLineText(document, 4, "x");
        Assert.False(map.Resolve(4).IsIntact);
    }

    [Fact]
    public void A_line_outside_the_document_resolves_to_nothing()
    {
        var (_, _, map) = Create();

        var resolved = map.Resolve(99);
        Assert.False(resolved.IsIntact);
        Assert.Empty(resolved.Spans);
        Assert.Null(map.IntactLineAt(99));
    }
}

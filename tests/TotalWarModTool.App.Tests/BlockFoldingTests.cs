using TotalWarModTool.Objects;

namespace TotalWarModTool.App.Tests;

public class BlockFoldingTests
{
    // head, blank, section head, then one section whose rows are blocks of `linesPerRow` lines (head line plus fields).
    private static BlockText Block(int rows, int linesPerRow, int sectionIndex = 3)
    {
        var lines = new List<BlockLine>
        {
            new(BlockLineKind.Head, [new("building_levels", SpanKind.TableName), new(" ", SpanKind.Normal), new("a", SpanKind.Key)], [], 0),
            new(BlockLineKind.Blank, [], [], 0),
            new(BlockLineKind.SectionHead, [new("effects", SpanKind.TableName)], [], 0, sectionIndex),
        };
        var ranges = new List<BlockRowRange>();
        for (var row = 0; row < rows; row++)
        {
            var first = lines.Count;
            lines.Add(new BlockLine(BlockLineKind.RowHead, [new("r" + row, SpanKind.Key)], [], 1, sectionIndex, row));
            for (var field = 1; field < linesPerRow; field++)
            {
                lines.Add(new BlockLine(BlockLineKind.RowField, [new("f" + field, SpanKind.FieldName), new(" = ", SpanKind.Normal), new("v", SpanKind.Value)], [], 2,
                    sectionIndex, row));
            }

            ranges.Add(new BlockRowRange(sectionIndex, row, first, lines.Count - 1));
        }

        return new BlockText(lines, [new BlockSectionRange(sectionIndex, 2, 3, lines.Count - 1, ranges)]);
    }

    // The start offset of every line and the length of the text as the last entry.
    private static int[] StartOffsets(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        starts.Add(text.Length);
        return [.. starts];
    }

    private static int EndOfLine(string text, int[] starts, int line) => line + 1 < starts.Length - 1 ? starts[line + 1] - 1 : text.Length;

    [Fact]
    public void A_section_above_the_threshold_folds_after_the_preview_rows()
    {
        var block = Block(25, 1);
        var starts = StartOffsets(block.Text);

        var folding = Assert.Single(BlockFolding.Compute(block, starts));

        var range = block.Sections[0];
        Assert.Equal(EndOfLine(block.Text, starts, range.Rows[4].LastLine), folding.StartOffset);
        Assert.Equal(EndOfLine(block.Text, starts, range.LastLine), folding.EndOffset);
        Assert.Equal(20, folding.HiddenRows);
        Assert.Equal(3, folding.Section);
        Assert.Equal(block.Text.Length, folding.EndOffset);
    }

    [Fact]
    public void A_section_at_the_threshold_has_no_folding()
    {
        Assert.Empty(BlockFolding.Compute(Block(20, 1), StartOffsets(Block(20, 1).Text)));
        Assert.Single(BlockFolding.Compute(Block(21, 1), StartOffsets(Block(21, 1).Text)));
    }

    [Fact]
    public void Rows_with_field_lines_fold_after_the_fifth_row_block()
    {
        var block = Block(22, 3);
        var starts = StartOffsets(block.Text);

        var folding = Assert.Single(BlockFolding.Compute(block, starts));

        var fifth = block.Sections[0].Rows[4];
        Assert.Equal(3, fifth.LastLine - fifth.FirstLine + 1);
        Assert.Equal(EndOfLine(block.Text, starts, fifth.LastLine), folding.StartOffset);
        Assert.Equal(17, folding.HiddenRows);
        var kept = block.Text[..folding.StartOffset];
        Assert.EndsWith("f2 = v", kept);
        Assert.Equal(5, kept.Split('\n').Count(l => l.StartsWith("  r", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_custom_threshold_and_preview_are_used()
    {
        var block = Block(6, 1);

        var folding = Assert.Single(BlockFolding.Compute(block, StartOffsets(block.Text), threshold: 4, preview: 2));

        Assert.Equal(4, folding.HiddenRows);
    }

    [Fact]
    public void Sections_do_not_fold_when_the_preview_would_hide_nothing()
    {
        var block = Block(3, 1);

        Assert.Empty(BlockFolding.Compute(block, StartOffsets(block.Text), threshold: 1, preview: 3));
    }

    [Fact]
    public void Foldings_from_line_ends_skip_a_section_whose_lines_are_gone()
    {
        var block = Block(25, 1);
        var starts = StartOffsets(block.Text);
        int? EndOf(int line) => EndOfLine(block.Text, starts, line);

        var folding = Assert.Single(BlockFolding.Compute(block, EndOf));
        Assert.Equal(Assert.Single(BlockFolding.Compute(block, starts)), folding);

        var last = block.Sections[0].LastLine;
        Assert.Empty(BlockFolding.Compute(block, line => line == last ? null : EndOf(line)));
        var fifth = block.Sections[0].Rows[4].LastLine;
        Assert.Empty(BlockFolding.Compute(block, line => line == fifth ? null : EndOf(line)));
    }
}

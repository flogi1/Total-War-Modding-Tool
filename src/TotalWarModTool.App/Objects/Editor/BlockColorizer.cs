using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace TotalWarModTool.App;

/// <summary>Colours every visual line from the map: resource brushes of the editor by SpanBrushes, bold and strikethrough. View wiring only.</summary>
internal sealed class BlockColorizer : DocumentColorizingTransformer
{
    private readonly Func<BlockLineMap?> _map;
    private readonly Func<string, Brush?> _brushOf;

    public BlockColorizer(Func<BlockLineMap?> map, Func<string, Brush?> brushOf)
    {
        _map = map;
        _brushOf = brushOf;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var map = _map();
        if (map is null || line.Length == 0)
        {
            return;
        }

        var offset = line.Offset;
        foreach (var span in map.Resolve(line.LineNumber).Spans)
        {
            var start = offset;
            offset += span.Text.Length;
            if (span.Text.Length == 0 || offset > line.EndOffset)
            {
                continue;
            }

            var brush = _brushOf(SpanBrushes.ResourceKeyOf(span.Kind));
            var bold = SpanBrushes.IsBold(span.Kind);
            var strike = SpanBrushes.IsStrikethrough(span.Kind);
            ChangeLinePart(start, offset, element =>
            {
                if (brush is not null)
                {
                    element.TextRunProperties.SetForegroundBrush(brush);
                }

                if (bold)
                {
                    var face = element.TextRunProperties.Typeface;
                    element.TextRunProperties.SetTypeface(new Typeface(face.FontFamily, face.Style, FontWeights.Bold, face.Stretch));
                }

                if (strike)
                {
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
                }
            });
        }
    }
}

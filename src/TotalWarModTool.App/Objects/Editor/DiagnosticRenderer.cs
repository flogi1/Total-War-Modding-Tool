using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// Underlines every line that has a diagnostic: Theme.ObjectError for an error, Theme.Warning for a warning (an error wins). Wavy line under the
/// text of the line. View wiring only; the decision which line gets which colour is <see cref="Underlines"/>.
/// </summary>
internal sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private const double WaveStep = 3;
    private const double WaveHeight = 2;
    private const double EmptyLineWidth = 6;

    private readonly Func<IReadOnlyList<BlockDiagnostic>> _diagnostics;
    private readonly Func<string, Brush?> _brushOf;

    public DiagnosticRenderer(Func<IReadOnlyList<BlockDiagnostic>> diagnostics, Func<string, Brush?> brushOf)
    {
        _diagnostics = diagnostics;
        _brushOf = brushOf;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>The lines with a diagnostic in line order, each with the brush of its worst diagnostic. A line without a brush resource is left out.</summary>
    public IReadOnlyList<(int Line, Brush Brush)> Underlines()
    {
        var result = new List<(int, Brush)>();
        foreach (var group in _diagnostics().GroupBy(d => d.Line).OrderBy(g => g.Key))
        {
            var key = ThemePalette.ResourcePrefix + (group.Any(d => d.Severity == DiagnosticSeverity.Error) ? "ObjectError" : "Warning");
            if (_brushOf(key) is { } brush)
            {
                result.Add((group.Key, brush));
            }
        }

        return result;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is not { } document || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (var (number, brush) in Underlines())
        {
            if (number < 1 || number > document.LineCount)
            {
                continue;
            }

            var line = document.GetLineByNumber(number);
            var segment = new TextSegment { StartOffset = line.Offset, EndOffset = line.EndOffset };
            var pen = new Pen(brush, 1);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                var width = Math.Max(rect.Width, EmptyLineWidth);
                drawingContext.DrawGeometry(null, pen, Wave(rect.Left, rect.Bottom - WaveHeight, width));
            }
        }
    }

    private static StreamGeometry Wave(double left, double baseline, double width)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(left, baseline), false, false);
            var up = false;
            for (var x = left + WaveStep; x < left + width + WaveStep; x += WaveStep)
            {
                context.LineTo(new Point(Math.Min(x, left + width), up ? baseline : baseline + WaveHeight), true, false);
                up = !up;
            }
        }

        geometry.Freeze();
        return geometry;
    }
}

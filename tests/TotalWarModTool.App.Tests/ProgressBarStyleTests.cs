using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace TotalWarModTool.App.Tests;

/// <summary>
/// The ProgressBar style of Themes/Controls.xaml. WPF stretches the indicator over the whole track as soon as IsIndeterminate is set, so the
/// template itself has to show a state that cannot be mistaken for a finished bar.
/// </summary>
public class ProgressBarStyleTests
{
    private const double TrackWidth = 400;

    // The test host cannot resolve the pack URI of the compiled dictionary (no Application), so the source file is parsed instead.
    private static ResourceDictionary LoadControls() =>
        (ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml")));

    private static ProgressBar CreateBar(Action<ProgressBar> configure)
    {
        var bar = new ProgressBar { Style = (Style)LoadControls()[typeof(ProgressBar)], Width = TrackWidth };
        configure(bar);
        Layout(bar);
        return bar;
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(TrackWidth, 20));
        element.Arrange(new Rect(0, 0, TrackWidth, 20));
        element.UpdateLayout();
    }

    private static FrameworkElement Part(ProgressBar bar, string name) =>
        (FrameworkElement)bar.Template.FindName(name, bar) ?? throw new InvalidOperationException($"The template has no element named {name}.");

    // IsVisible is false for everything outside a window, so visibility is checked on the element and its ancestors up to the bar.
    private static bool IsShown(FrameworkElement element, FrameworkElement root)
    {
        for (DependencyObject? current = element; current is not null && current != root; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    // Width the element is painted with, including the scaling of a Viewbox around it.
    private static double RenderedWidth(FrameworkElement element, FrameworkElement root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize)).Width;

    [Fact] public void Determinate_indicator_fills_the_given_share_of_the_track() => Sta.Run(() =>
    {
        var bar = CreateBar(b => { b.Maximum = 100; b.Value = 50; });
        var track = Part(bar, "PART_Track");
        var indicator = Part(bar, "PART_Indicator");
        Assert.Equal(TrackWidth, track.ActualWidth);
        Assert.InRange(indicator.ActualWidth / track.ActualWidth, 0.45, 0.55);
    });

    [Fact] public void Indeterminate_indicator_does_not_fill_the_track() => Sta.Run(() =>
    {
        var bar = CreateBar(b => b.IsIndeterminate = true);
        var indicator = Part(bar, "PART_Indicator");
        Assert.True(!IsShown(indicator, bar) || indicator.ActualWidth < 0.6 * TrackWidth,
            $"The determinate indicator is shown at {indicator.ActualWidth} of {TrackWidth}, so an unknown amount looks 100 percent done.");
    });

    [Fact] public void Indeterminate_state_shows_a_segment_narrower_than_the_track() => Sta.Run(() =>
    {
        var bar = CreateBar(b => b.IsIndeterminate = true);
        var segment = Part(bar, "PART_IndeterminateSegment");
        Assert.True(IsShown(segment, bar));
        Assert.InRange(RenderedWidth(segment, bar), 0.1 * TrackWidth, 0.6 * TrackWidth);
    });

    [Fact] public void Segment_is_hidden_while_the_amount_is_known() => Sta.Run(() =>
    {
        var bar = CreateBar(b => { b.Maximum = 100; b.Value = 50; });
        Assert.False(IsShown(Part(bar, "PART_IndeterminateSegment"), bar));
        bar.IsIndeterminate = true;
        Layout(bar);
        Assert.True(IsShown(Part(bar, "PART_IndeterminateSegment"), bar));
        bar.IsIndeterminate = false;
        Layout(bar);
        Assert.False(IsShown(Part(bar, "PART_IndeterminateSegment"), bar));
        Assert.True(IsShown(Part(bar, "PART_Indicator"), bar));
    });

    // The animation needs a rendering clock, so the bar sits in a shown window. Waits for the effect, no fixed sleep.
    [Fact] public void Segment_moves_while_indeterminate() => Sta.RunAsync(async () =>
    {
        var bar = new ProgressBar { Style = (Style)LoadControls()[typeof(ProgressBar)], IsIndeterminate = true };
        var window = new Window { Content = bar, Width = 300, Height = 100 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segment = Part(bar, "PART_IndeterminateSegment");
            var seen = new HashSet<double> { Canvas.GetLeft(segment) };
            var deadline = Environment.TickCount64 + 5000;
            while (seen.Count < 3 && Environment.TickCount64 < deadline)
            {
                await Task.Delay(20);
                seen.Add(Canvas.GetLeft(segment));
            }

            Assert.True(seen.Count >= 3, $"The segment stayed at {string.Join(", ", seen)}.");
        }
        finally { window.Close(); }
    });
}

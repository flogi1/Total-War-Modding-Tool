using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace TotalWarModTool.App.Shell;

/// <summary>Content of a small-toolbar button (icon plus label) and its separators.</summary>
public static class ToolbarItemView
{
    private const string LabelName = "ToolbarLabel";

    /// <summary>
    /// Icon (16 px, in the foreground colour of the button, so it follows the disabled and checked states like the label) with the label
    /// beside it; <paramref name="showLabel"/> false hides the label (vertical toolbar, setting).
    /// </summary>
    public static FrameworkElement Content(string id, string label, bool showLabel)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(ToolIcons.CreateInForeground(id, 16));
        panel.Children.Add(new TextBlock
        {
            Name = LabelName,
            Text = label,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed,
        });
        return panel;
    }

    /// <summary>Separator: a vertical line in the horizontal toolbar, a horizontal line in the vertical one.</summary>
    public static FrameworkElement Separator(bool vertical)
    {
        var line = vertical
            ? new Rectangle { Width = 16, Height = 1, Margin = new Thickness(2, 4, 2, 4) }
            : new Rectangle { Width = 1, Height = 16, Margin = new Thickness(4, 2, 4, 2) };
        line.SetResourceReference(Shape.FillProperty, "Theme.Separator");
        return line;
    }

    /// <summary>The label of content built with <see cref="Content"/>, otherwise <see langword="null"/>.</summary>
    public static TextBlock? LabelOf(FrameworkElement content) =>
        content is StackPanel panel ? panel.Children.OfType<TextBlock>().FirstOrDefault(block => block.Name == LabelName) : null;

    /// <summary>
    /// Changes only the label text of content built with <see cref="Content"/> (icon and visibility stay);
    /// <see langword="false"/> if the content has no such label.
    /// </summary>
    public static bool SetLabel(FrameworkElement content, string label)
    {
        if (LabelOf(content) is not { } block)
        {
            return false;
        }

        block.Text = label;
        return true;
    }
}

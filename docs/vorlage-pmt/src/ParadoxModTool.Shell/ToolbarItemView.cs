using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace ParadoxModTool.Shell;

/// <summary>Inhalt eines Knopfs der kleinen Werkzeugleiste (Icon plus Beschriftung) und ihre Trennlinien.</summary>
public static class ToolbarItemView
{
    private const string LabelName = "ToolbarLabel";

    /// <summary>
    /// Icon (16 px, in der Vordergrundfarbe des Knopfs - normal <c>Theme.Text</c>, deaktiviert und eingerastet wie die Beschriftung) und
    /// daneben die Beschriftung; <paramref name="showLabel"/> aus blendet sie aus (senkrechte Leiste, Einstellung).
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

    /// <summary>Trennlinie: senkrechter Strich in der waagerechten Leiste, waagerechter Strich in der senkrechten.</summary>
    public static FrameworkElement Separator(bool vertical)
    {
        var line = vertical
            ? new Rectangle { Width = 16, Height = 1, Margin = new Thickness(2, 4, 2, 4) }
            : new Rectangle { Width = 1, Height = 16, Margin = new Thickness(4, 2, 4, 2) };
        line.SetResourceReference(Shape.FillProperty, "Theme.Separator");
        return line;
    }

    /// <summary>Die Beschriftung eines mit <see cref="Content"/> gebauten Inhalts, sonst <see langword="null"/>.</summary>
    public static TextBlock? LabelOf(FrameworkElement content) =>
        content is StackPanel panel ? panel.Children.OfType<TextBlock>().FirstOrDefault(block => block.Name == LabelName) : null;

    /// <summary>
    /// Ändert nur den Text der Beschriftung eines mit <see cref="Content"/> gebauten Inhalts (Icon und Ein-/Ausblenden bleiben);
    /// <see langword="false"/>, wenn der Inhalt keine solche Beschriftung hat.
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

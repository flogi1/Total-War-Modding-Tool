using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Shell;

/// <summary>
/// Grip at the start of the small toolbar: shows that the toolbar can be moved and opens a menu with the four positions on click (the quick
/// variant of the layout setting). The choice is reported by <see cref="PositionChosen"/>. Labels and tooltip come from the caller.
/// </summary>
public sealed class ToolbarPositionGrip : Button
{
    private static readonly ToolbarPosition[] Positions = [ToolbarPosition.Top, ToolbarPosition.Left, ToolbarPosition.Right, ToolbarPosition.Bottom];

    private readonly Func<ToolbarPosition> _current;
    private readonly Func<ToolbarPosition, string> _label;

    public ToolbarPositionGrip(Func<ToolbarPosition> current, Func<ToolbarPosition, string> label, string toolTip)
    {
        _current = current;
        _label = label;
        Content = "⋮";
        Padding = new Thickness(4, 0, 4, 0);
        Focusable = false;
        Cursor = Cursors.Hand;
        ToolTip = toolTip;
        SetResourceReference(ForegroundProperty, "Theme.TextMuted");
        Click += (_, _) =>
        {
            var menu = BuildMenu();
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
    }

    public event EventHandler<ToolbarPosition>? PositionChosen;

    /// <summary>Menu with Top, Left, Right, Bottom; the current position carries a check mark.</summary>
    public ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var current = _current();
        foreach (var position in Positions)
        {
            var item = new MenuItem
            {
                Header = _label(position),
                Tag = position,
                IsCheckable = true,
                IsChecked = position == current,
            };
            var chosen = position;
            item.Click += (_, _) => PositionChosen?.Invoke(this, chosen);
            menu.Items.Add(item);
        }

        return menu;
    }
}

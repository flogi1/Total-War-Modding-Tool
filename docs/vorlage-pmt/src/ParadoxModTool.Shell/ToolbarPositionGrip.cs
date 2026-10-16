using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ParadoxModTool.Settings;

namespace ParadoxModTool.Shell;

/// <summary>
/// Griff ⋮ am Anfang der kleinen Werkzeugleiste: zeigt, dass sie beweglich ist, und öffnet beim Klick ein Menü mit den vier Positionen
/// (schnelle Variante zur Einstellung unter Layout). Die Wahl meldet <see cref="PositionChosen"/> mit <c>Top|Left|Right|Bottom</c>.
/// </summary>
public sealed class ToolbarPositionGrip : Button
{
    private static readonly string[] Positions = { "Top", "Left", "Right", "Bottom" };

    private readonly Func<string> _current;

    public ToolbarPositionGrip(Func<string> current)
    {
        _current = current;
        Content = "⋮";
        Padding = new Thickness(4, 0, 4, 0);
        Focusable = false;
        Cursor = Cursors.Hand;
        ToolTip = SettingsTexts.ToolbarPositionHeading;
        SetResourceReference(ForegroundProperty, "Theme.TextMuted");
        Click += (_, _) =>
        {
            var menu = BuildMenu();
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
    }

    public event EventHandler<string>? PositionChosen;

    /// <summary>Menü mit Oben, Links, Rechts, Unten; die aktuelle Position hat ein Häkchen.</summary>
    public ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var current = _current();
        foreach (var position in Positions)
        {
            var item = new MenuItem
            {
                Header = Label(position),
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

    private static string Label(string position) => position switch
    {
        "Left" => SettingsTexts.ToolbarPositionLeft,
        "Right" => SettingsTexts.ToolbarPositionRight,
        "Bottom" => SettingsTexts.ToolbarPositionBottom,
        _ => SettingsTexts.ToolbarPositionTop,
    };
}

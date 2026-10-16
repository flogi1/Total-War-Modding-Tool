using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Shell;

/// <summary>
/// What the tile bar needs from the main window: label and tooltip per id, whether a tile is offered at all, whether it is usable at the
/// moment together with the tooltip for the disabled state, and the menu of a submenu tile. All texts come from the caller.
/// </summary>
public sealed record TileSource(
    Func<string, string> Label,
    Func<string, string> Tip,
    Func<string, bool> IsOffered,
    Func<string, bool> IsEnabled,
    Func<string, string?> DisabledTip,
    Func<string, ContextMenu?> Menu);

/// <summary>
/// The tile bar: a horizontally scrolling strip with one tile per visible id of <see cref="GlobalSettings.TileOrder"/>. Size, label and
/// "icon only" come from the settings; the bar rebuilds whenever one of these values changes. Tiles can be reordered by drag and drop
/// (<see cref="ReorderDragHost"/>), badges show counts, a warning sign shows problems, and submenu tiles open the menu from
/// <see cref="TileSource.Menu"/> on click. <see cref="RefreshState"/> re-reads the enabled state and tooltip without a rebuild.
/// </summary>
public sealed class TileBar : UserControl
{
    public const double LargeWidth = 104;
    public const double LargeHeight = 78;
    public const double MediumWidth = 84;
    public const double MediumHeight = 62;
    public const double CompactSize = 44;

    /// <summary>Bar id in the drag data; buttons of the small toolbar (<see cref="ToolbarBuilder.ListKey"/>) are never accepted here.</summary>
    public const string ListKey = "tiles";

    /// <summary>Red of the badge; the only fixed colour (readable in both themes, white text).</summary>
    public static readonly Brush BadgeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)));

    private static readonly string[] RebuildTriggers =
    [
        nameof(GlobalSettings.TileOrder), nameof(GlobalSettings.HiddenTiles), nameof(GlobalSettings.TileSize), nameof(GlobalSettings.IconOnlyTiles),
        nameof(GlobalSettings.TileLabelsVisible),
    ];

    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Button> _tiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _badges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _warnings = new(StringComparer.Ordinal);
    private List<string> _visible = [];
    private GlobalSettings? _settings;
    private TileSource? _source;

    public TileBar()
    {
        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(4, 3, 4, 3),
                Content = _strip,
            },
        };
        border.SetResourceReference(Border.BorderBrushProperty, "Theme.Separator");
        border.SetResourceReference(Border.BackgroundProperty, "Theme.WindowBackground");
        Content = border;
    }

    public event EventHandler<string>? TileClicked;

    /// <summary>Ids of the shown tiles in bar order.</summary>
    public IReadOnlyList<string> VisibleIds => _visible;

    /// <summary>Connects bar, settings and source and builds at once; later changes of the tile settings rebuild the bar.</summary>
    public void Bind(GlobalSettings settings, TileSource source)
    {
        _settings = settings;
        _source = source;
        settings.Changed += (_, name) =>
        {
            if (RebuildTriggers.Contains(name, StringComparer.Ordinal))
            {
                Rebuild();
            }
        };
        Rebuild();
    }

    public bool IsVisibleTile(string id) => _tiles.ContainsKey(id);

    /// <summary>The button of tile <paramref name="id"/>; <see cref="KeyNotFoundException"/> if it is not shown.</summary>
    public Button TileButton(string id) => _tiles[id];

    public int BadgeOf(string id) => _badges.GetValueOrDefault(id);

    /// <summary>Number of the badge; 0 hides it. Acts at once on a shown tile and survives rebuilds.</summary>
    public void SetBadge(string id, int count)
    {
        _badges[id] = count;
        if (_tiles.TryGetValue(id, out var tile) && tile.Content is Grid grid)
        {
            ApplyBadge(grid, count);
        }
    }

    /// <summary>Warning text of a tile (<see cref="SetWarning"/>), <see langword="null"/> without a warning.</summary>
    public string? WarningOf(string id) => _warnings.GetValueOrDefault(id);

    /// <summary>
    /// Warning sign on the tile; <paramref name="tip"/> becomes a further line of the tooltip, <see langword="null"/> removes both. Acts at
    /// once on a shown tile and survives rebuilds.
    /// </summary>
    public void SetWarning(string id, string? tip)
    {
        if (tip is null)
        {
            _warnings.Remove(id);
        }
        else
        {
            _warnings[id] = tip;
        }

        if (_tiles.TryGetValue(id, out var tile) && tile.Content is Grid grid && _source is { } source)
        {
            ApplyWarning(grid, tip);
            tile.ToolTip = TipFor(id, tile.IsEnabled, source);
        }
    }

    /// <summary>Moves <paramref name="dragged"/> next to <paramref name="target"/> (rule of <see cref="ReorderList.MoveNextTo"/>) in <see cref="GlobalSettings.TileOrder"/>.</summary>
    public bool MoveTile(string dragged, string target)
    {
        if (_settings is null)
        {
            return false;
        }

        var copy = _settings.TileOrder.ToList();
        if (!ReorderList.MoveNextTo(copy, dragged, target))
        {
            return false;
        }

        _settings.TileOrder = copy;
        return true;
    }

    /// <summary>Rebuilds all tiles from settings and source.</summary>
    public void Rebuild()
    {
        if (_settings is null || _source is null)
        {
            return;
        }

        var settings = _settings;
        var source = _source;
        _strip.Children.Clear();
        _tiles.Clear();
        _visible = settings.TileOrder.Where(id => TileCatalog.IsKnown(id) && settings.IsTileVisible(id) && source.IsOffered(id)).ToList();
        foreach (var id in _visible)
        {
            var tile = BuildTile(id, settings, source);
            _tiles[id] = tile;
            _strip.Children.Add(new ReorderDragHost(ListKey, id, tile, _visible.Contains, (from, to) => MoveTile(from, to)));
        }
    }

    /// <summary>Sets enabled state and tooltip of every shown tile from the source again, without rebuilding the tiles.</summary>
    public void RefreshState()
    {
        if (_source is not { } source)
        {
            return;
        }

        foreach (var (id, tile) in _tiles)
        {
            var enabled = source.IsEnabled(id);
            tile.IsEnabled = enabled;
            tile.ToolTip = TipFor(id, enabled, source);
        }
    }

    private Button BuildTile(string id, GlobalSettings settings, TileSource source)
    {
        var item = TileCatalog.Find(id)!;
        var compact = settings.IsTileIconOnly(id) || settings.TileSize == TileSize.Compact;
        var (width, height, iconSize) = compact
            ? (CompactSize, CompactSize, 24.0)
            : settings.TileSize == TileSize.Medium ? (MediumWidth, MediumHeight, 22.0) : (LargeWidth, LargeHeight, 28.0);
        var enabled = source.IsEnabled(id);

        var body = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(ToolIcons.Create(id, iconSize, "Theme.Accent", "Theme.TextDisabled"));
        body.Children.Add(new TextBlock
        {
            Name = "TileLabel",
            Text = source.Label(id),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            FontSize = 11,
            MaxWidth = width - 8,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = !compact && settings.TileLabelsVisible ? Visibility.Visible : Visibility.Collapsed,
        });

        var grid = new Grid();
        grid.Children.Add(body);

        var badgeText = new TextBlock { FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Border
        {
            Name = "TileBadge",
            CornerRadius = new CornerRadius(8),
            MinWidth = 16,
            Height = 16,
            Padding = new Thickness(3, 0, 3, 0),
            Background = BadgeBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = badgeText,
        });
        ApplyBadge(grid, BadgeOf(id));

        // Warning sign at the top left (the badge sits at the top right); an element of its own so it stays visible without a label.
        var warning = new TextBlock { Name = "TileWarning", Text = "⚠", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Warning");
        grid.Children.Add(warning);
        ApplyWarning(grid, WarningOf(id));

        if (item.HasMenu)
        {
            var arrow = new TextBlock { Name = "TileMenuArrow", Text = "▾", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextMuted");
            grid.Children.Add(arrow);
        }

        var tile = new Button
        {
            Tag = id,
            Width = width,
            Height = height,
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 4, 0),
            Focusable = false,
            IsEnabled = enabled,
            ToolTip = TipFor(id, enabled, source),
            Content = grid,
            Style = TileStyle(),
        };
        ToolTipService.SetShowOnDisabled(tile, true);
        tile.Click += (_, _) =>
        {
            if (item.HasMenu && source.Menu(id) is { } menu)
            {
                menu.PlacementTarget = tile;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
                return;
            }

            TileClicked?.Invoke(this, id);
        };
        return tile;
    }

    /// <summary>Border from Theme.Separator, background Theme.PanelBackground, accent border under the mouse; builds on the theme button style.</summary>
    private Style TileStyle()
    {
        var style = new Style(typeof(Button), TryFindResource(typeof(Button)) as Style);
        style.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("Theme.Separator")));
        style.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("Theme.PanelBackground")));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("Theme.Accent")));
        style.Triggers.Add(hover);
        return style;
    }

    /// <summary>Tooltip of a tile: normal or for the disabled state, plus the warning text as a further line.</summary>
    private string TipFor(string id, bool enabled, TileSource source)
    {
        var tip = enabled ? source.Tip(id) : source.DisabledTip(id) ?? source.Tip(id);
        return WarningOf(id) is { } warning ? tip + "\n" + warning : tip;
    }

    private static void ApplyWarning(Grid grid, string? tip) =>
        grid.Children.OfType<TextBlock>().First(block => block.Name == "TileWarning").Visibility = tip is null ? Visibility.Collapsed : Visibility.Visible;

    private static void ApplyBadge(Grid grid, int count)
    {
        var badge = grid.Children.OfType<Border>().First(border => border.Name == "TileBadge");
        ((TextBlock)badge.Child).Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

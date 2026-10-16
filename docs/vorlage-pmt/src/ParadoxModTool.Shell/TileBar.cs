using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ParadoxModTool.Settings;

namespace ParadoxModTool.Shell;

/// <summary>
/// Was die Kachelleiste vom Hauptfenster braucht: Beschriftung und Tooltip je Kennung, ob eine Kachel angeboten wird (GUI-Werkzeuge aus →
/// GUI-Kacheln weg), ob sie bedienbar ist (Save Explorer ohne Profil-Unterstützung) samt Tooltip im deaktivierten Zustand, und das Menü
/// einer Untermenü-Kachel.
/// </summary>
public sealed record TileSource(
    Func<string, string> Label,
    Func<string, string> Tip,
    Func<string, bool> IsOffered,
    Func<string, bool> IsEnabled,
    Func<string, string?> DisabledTip,
    Func<string, ContextMenu?> Menu);

/// <summary>
/// Die Kachelleiste (Spec "Interface-Rework 2", Abschnitt Kachelleiste): ein waagerecht scrollender Streifen mit einer Kachel je sichtbarer
/// Kennung aus <see cref="AppSettings.TileOrder"/>. Größe, Beschriftung und "Nur Symbol" kommen aus den Einstellungen; die Leiste baut sich bei
/// jeder Änderung dieser Werte neu. Kacheln sind per Drag-and-drop umsortierbar (<see cref="ReorderDragHost"/>), Abzeichen zeigen Zahlen
/// (Error-Log, Bugtracker), ein Warnzeichen ⚠ Probleme (Bugtracker-Abruf fehlgeschlagen), Untermenü-Kacheln öffnen beim Klick das Menü aus <see cref="TileSource.Menu"/>.
/// </summary>
public sealed class TileBar : UserControl
{
    public const double LargeWidth = 104;
    public const double LargeHeight = 78;
    public const double MediumWidth = 84;
    public const double MediumHeight = 62;
    public const double CompactSize = 44;

    /// <summary>Leisten-Kennung in den Ziehdaten; Knöpfe der kleinen Leiste (<see cref="ToolbarBuilder.ListKey"/>) werden hier nie angenommen.</summary>
    public const string ListKey = "tiles";

    /// <summary>Rot des Abzeichens; die einzige feste Farbe (in beiden Themes lesbar, Schrift weiß).</summary>
    public static readonly Brush BadgeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)));

    private static readonly string[] RebuildTriggers =
    {
        nameof(AppSettings.TileOrder), nameof(AppSettings.HiddenTiles), nameof(AppSettings.TileSize), nameof(AppSettings.IconOnlyTiles),
        nameof(AppSettings.TileLabelsVisible),
    };

    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Button> _tiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _badges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _warnings = new(StringComparer.Ordinal);
    private List<string> _visible = new();
    private AppSettings? _settings;
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

    /// <summary>Kennungen der gezeigten Kacheln in Leistenreihenfolge.</summary>
    public IReadOnlyList<string> VisibleIds => _visible;

    /// <summary>
    /// Verbindet Leiste, Einstellungen und Quelle und baut sofort; spätere Änderungen der Kachel-Einstellungen bauen neu, ebenso ein
    /// Neuladen aller Einstellungen (<see cref="AppSettings.Reloaded"/> beim Wiederherstellen der Sitzung - löst kein einzelnes Changed aus).
    /// </summary>
    public void Bind(AppSettings settings, TileSource source)
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
        settings.Reloaded += (_, _) => Rebuild();
        Rebuild();
    }

    public bool IsVisibleTile(string id) => _tiles.ContainsKey(id);

    /// <summary>Der Knopf der Kachel <paramref name="id"/>; <see cref="KeyNotFoundException"/>, wenn sie nicht gezeigt wird.</summary>
    public Button TileButton(string id) => _tiles[id];

    public int BadgeOf(string id) => _badges.GetValueOrDefault(id);

    /// <summary>Zahl des Abzeichens; 0 blendet es aus. Wirkt sofort auf eine gezeigte Kachel und bleibt über Neuaufbauten erhalten.</summary>
    public void SetBadge(string id, int count)
    {
        _badges[id] = count;
        if (_tiles.TryGetValue(id, out var tile) && tile.Content is Grid grid)
        {
            ApplyBadge(grid, count);
        }
    }

    /// <summary>Warnhinweis einer Kachel (<see cref="SetWarning"/>), <see langword="null"/> ohne Warnung.</summary>
    public string? WarningOf(string id) => _warnings.GetValueOrDefault(id);

    /// <summary>
    /// Warnzeichen ⚠ an der Kachel (z. B. Bugtracker nach fehlgeschlagenem Abruf); <paramref name="tip"/> steht als weitere Zeile im Tooltip,
    /// <see langword="null"/> entfernt beides. Wirkt sofort auf eine gezeigte Kachel und bleibt über Neuaufbauten erhalten.
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

    /// <summary>Verschiebt <paramref name="dragged"/> neben <paramref name="target"/> (Regel von <see cref="ReorderList.MoveNextTo"/>) in <see cref="AppSettings.TileOrder"/>.</summary>
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

    /// <summary>Baut alle Kacheln neu aus Einstellungen und Quelle.</summary>
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

    private Button BuildTile(string id, AppSettings settings, TileSource source)
    {
        var item = TileCatalog.Find(id)!;
        var compact = settings.IsTileIconOnly(id) || settings.TileSize == "Compact";
        var (width, height, iconSize) = compact
            ? (CompactSize, CompactSize, 24.0)
            : settings.TileSize == "Medium" ? (MediumWidth, MediumHeight, 22.0) : (LargeWidth, LargeHeight, 28.0);
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

        // Warnzeichen oben links (das Abzeichen sitzt oben rechts); eigenes Element, damit es auch ohne Beschriftung sichtbar bleibt.
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

    /// <summary>Rahmen aus Theme.Separator, Hintergrund Theme.PanelBackground, Rahmen in Akzentfarbe unter der Maus; baut auf dem Theme-Knopfstil auf.</summary>
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

    /// <summary>Tooltip einer Kachel: normal oder im deaktivierten Zustand, dazu der Warnhinweis als weitere Zeile.</summary>
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

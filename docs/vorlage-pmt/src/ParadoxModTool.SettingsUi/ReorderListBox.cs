using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ParadoxModTool.Settings;

namespace ParadoxModTool.SettingsUi;

/// <summary>
/// Eine Zeile der <see cref="ReorderListBox"/>: Kennung, Beschriftung und optionale Zusätze.
/// </summary>
/// <param name="Id">Kennung des Eintrags (Schlüssel in der vom Aufrufer gehaltenen Liste).</param>
/// <param name="Label">Angezeigter Text.</param>
/// <param name="Muted">Zeile gedämpft darstellen (z. B. ausgeblendeter Eintrag).</param>
/// <param name="MutedHint">Erklärung für eine gedämpfte Zeile; erscheint als ToolTip und ersetzt das Durchstreichen.</param>
/// <param name="IsSeparator">Trennlinie: kursive Beschriftung mit einer Linie dahinter.</param>
/// <param name="Icon">Symbol vor der Beschriftung (z. B. <c>ToolIcons.Create</c>); wie <paramref name="Trailing"/> bei jedem Fabrikaufruf ein NEUES Element.</param>
/// <param name="Trailing">
/// Element am rechten Rand (z. B. ein Schalter). Muss bei jedem Aufruf der Zeilenfabrik ein NEUES Element sein: das Steuerelement hängt es in
/// sein eigenes Grid um und setzt dabei <c>Margin</c> und <c>VerticalAlignment</c>. Dasselbe gilt später für ein Symbol-Element.
/// </param>
/// <param name="Remove">Wenn gesetzt, zeigt die Zeile einen Entfernen-Knopf, der diese Aktion ruft.</param>
public sealed record ReorderRow(
    string Id,
    string Label,
    bool Muted = false,
    string? MutedHint = null,
    bool IsSeparator = false,
    FrameworkElement? Icon = null,
    FrameworkElement? Trailing = null,
    Action? Remove = null);

/// <summary>
/// Liste von Kennungen, die der Nutzer per Pfeilknöpfen, Drag-and-drop oder Alt+Pfeil umsortiert. Die Kennungsliste gehört dem
/// Aufrufer: jede Änderung läuft über <see cref="ReorderList"/> auf einer Kopie von <c>order()</c> und wird als ganze Liste an
/// <c>commit</c> gegeben. <c>commit</c> baut das Steuerelement nicht neu auf; das tut der Besitzer über <see cref="Rebuild"/>.
/// </summary>
public sealed class ReorderListBox : ItemsControl
{
    /// <summary>Datenformat, unter dem beim Ziehen die Kennung der gezogenen Zeile mitgegeben wird.</summary>
    public const string DataFormat = "pmt-reorder-id";

    /// <summary>Abstand (in Pixeln) zum Rand des umgebenden <see cref="ScrollViewer"/>, ab dem beim Ziehen gescrollt wird.</summary>
    private const double AutoScrollEdge = 24;

    private readonly Func<IReadOnlyList<string>> _order;
    private readonly Action<IReadOnlyList<string>> _commit;
    private readonly Func<string, ReorderRow> _row;
    private readonly Dictionary<string, FrameworkElement> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _upButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _downButtons = new(StringComparer.Ordinal);
    private List<string> _ids = new();
    private string? _dragId;
    private Point _dragStart;

    public ReorderListBox(Func<IReadOnlyList<string>> order, Action<IReadOnlyList<string>> commit, Func<string, ReorderRow> row)
    {
        _order = order;
        _commit = commit;
        _row = row;
        // Auch freier Platz neben und unter den Zeilen zählt fürs Auto-Scrollen (die Zeilen verbrauchen DragOver selbst).
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            AutoScroll(e);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        };
        Rebuild();
    }

    /// <summary>
    /// Scrollrichtung beim Ziehen: -1 (nach oben) näher als <see cref="AutoScrollEdge"/> am oberen Rand, +1 (nach unten) näher als
    /// <see cref="AutoScrollEdge"/> am unteren Rand, sonst 0. <paramref name="y"/> ist die Mausposition relativ zum sichtbaren Bereich.
    /// </summary>
    public static int AutoScrollDirection(double y, double viewportHeight)
    {
        if (y < AutoScrollEdge)
        {
            return -1;
        }

        return y > viewportHeight - AutoScrollEdge ? 1 : 0;
    }

    /// <summary>
    /// Ob die gezogene Zeile hier abgelegt werden darf: Sie muss zu dieser Liste gehören und darf nicht die Zielzeile selbst sein
    /// (fremde Kennungen, z. B. aus einer anderen Liste, werden abgelehnt).
    /// </summary>
    public bool CanAcceptDrop(string draggedId, string targetId) =>
        _ids.Contains(draggedId, StringComparer.Ordinal) && !string.Equals(draggedId, targetId, StringComparison.Ordinal);

    /// <summary>
    /// Scrollt den umgebenden <see cref="ScrollViewer"/> eine Zeile, wenn die Maus beim Ziehen nahe an dessen oberem oder unterem Rand
    /// ist; WPF scrollt während <see cref="DragDrop.DoDragDrop"/> nicht von selbst.
    /// </summary>
    private void AutoScroll(DragEventArgs e)
    {
        if (FindScrollViewer(this) is not { } viewer)
        {
            return;
        }

        var direction = AutoScrollDirection(e.GetPosition(viewer).Y, viewer.ActualHeight);
        if (direction < 0)
        {
            viewer.LineUp();
        }
        else if (direction > 0)
        {
            viewer.LineDown();
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject start)
    {
        for (var node = VisualTreeHelper.GetParent(start); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer viewer)
            {
                return viewer;
            }
        }

        return null;
    }

    /// <summary>Kennung der gezogenen Zeile aus den Ziehdaten, oder <see langword="null"/> bei fremden Daten.</summary>
    private static string? DraggedId(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormat) ? e.Data.GetData(DataFormat) as string : null;

    /// <summary>Kennungen in Anzeigereihenfolge, wie beim letzten <see cref="Rebuild"/> gelesen.</summary>
    public IReadOnlyList<string> CurrentIds => _ids;

    /// <summary>Liest <c>order()</c> neu und baut alle Zeilen auf.</summary>
    public void Rebuild()
    {
        Items.Clear();
        _rows.Clear();
        _upButtons.Clear();
        _downButtons.Clear();
        _ids = _order().ToList();
        for (var i = 0; i < _ids.Count; i++)
        {
            var id = _ids[i];
            var element = BuildRow(_row(id), i > 0, i < _ids.Count - 1);
            _rows[id] = element;
            Items.Add(element);
        }
    }

    /// <summary>Die Zeile mit der Kennung <paramref name="id"/>; <see cref="KeyNotFoundException"/> bei unbekannter Kennung.</summary>
    public FrameworkElement Row(string id) => _rows[id];

    /// <summary>Der Pfeil nach oben der Zeile <paramref name="id"/>.</summary>
    public Button UpButton(string id) => _upButtons[id];

    /// <summary>Der Pfeil nach unten der Zeile <paramref name="id"/>.</summary>
    public Button DownButton(string id) => _downButtons[id];

    /// <summary>Rückt <paramref name="id"/> eine Stelle nach oben; <see langword="false"/> ohne Änderung (schon oben oder unbekannt).</summary>
    public bool MoveUp(string id)
    {
        var copy = _order().ToList();
        var index = copy.IndexOf(id);
        return index >= 0 && ReorderList.Swap(copy, index, index - 1) && Commit(copy);
    }

    /// <summary>Rückt <paramref name="id"/> eine Stelle nach unten; <see langword="false"/> ohne Änderung (schon unten oder unbekannt).</summary>
    public bool MoveDown(string id)
    {
        var copy = _order().ToList();
        var index = copy.IndexOf(id);
        return index >= 0 && ReorderList.Swap(copy, index, index + 1) && Commit(copy);
    }

    /// <summary>Verschiebt <paramref name="id"/> neben <paramref name="targetId"/> (Regel von <see cref="ReorderList.MoveNextTo"/>).</summary>
    public bool MoveNextTo(string id, string targetId)
    {
        var copy = _order().ToList();
        return ReorderList.MoveNextTo(copy, id, targetId) && Commit(copy);
    }

    /// <summary>
    /// Tastaturbefehl einer Zeile: Alt+Hoch / Alt+Runter verschiebt sie. <see langword="true"/>, wenn die Taste verbraucht wurde und die
    /// Liste sich geändert hat; der Fokus geht danach auf die neu gebaute Zeile derselben Kennung.
    /// </summary>
    public bool HandleKey(string id, Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Alt)
        {
            return false;
        }

        var moved = key switch
        {
            Key.Up => MoveUp(id),
            Key.Down => MoveDown(id),
            _ => false,
        };
        if (moved)
        {
            FocusLater(id, null);
        }

        return moved;
    }

    /// <summary>
    /// Setzt den Fokus nach dem Neuaufbau (die neuen Elemente sind erst nach dem Layout fokussierbar): auf den Pfeilknopf aus
    /// <paramref name="arrows"/> der Kennung <paramref name="id"/>, wenn er existiert und aktiv ist, sonst auf die Zeile dieser Kennung.
    /// </summary>
    private void FocusLater(string id, Dictionary<string, Button>? arrows)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (arrows is not null && arrows.TryGetValue(id, out var button) && button.IsEnabled)
            {
                button.Focus();
            }
            else if (_rows.TryGetValue(id, out var row))
            {
                row.Focus();
            }
        }));
    }

    private void MoveAndRefocus(string id, bool up)
    {
        if (up ? MoveUp(id) : MoveDown(id))
        {
            FocusLater(id, up ? _upButtons : _downButtons);
        }
    }

    private bool Commit(List<string> copy)
    {
        _commit(copy);
        return true;
    }

    private FrameworkElement BuildRow(ReorderRow row, bool canMoveUp, bool canMoveDown)
    {
        var id = row.Id;
        var grid = new Grid();
        for (var i = 0; i < 8; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                // 0 Griff, 1 Hoch, 2 Runter, 3 Symbol, 4 Beschriftung, 5 Linie/Rest, 6 Zusatz, 7 Entfernen
                Width = i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
        }

        var handle = new TextBlock
        {
            Text = "⋮⋮",
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.SizeAll,
            ToolTip = SettingsTexts.ReorderDragHint,
        };
        handle.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextMuted");
        Grid.SetColumn(handle, 0);
        grid.Children.Add(handle);

        var up = ArrowButton("▲", SettingsTexts.ReorderUp, canMoveUp, () => MoveAndRefocus(id, true));
        up.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(up, 1);
        grid.Children.Add(up);
        _upButtons[id] = up;

        var down = ArrowButton("▼", SettingsTexts.ReorderDown, canMoveDown, () => MoveAndRefocus(id, false));
        down.Margin = new Thickness(2, 0, 0, 0);
        Grid.SetColumn(down, 2);
        grid.Children.Add(down);
        _downButtons[id] = down;

        if (row.Icon is { } icon)
        {
            icon.Margin = new Thickness(8, 0, 0, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(icon, 3);
            grid.Children.Add(icon);
        }

        var label = new TextBlock
        {
            Text = row.Label,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (row.IsSeparator)
        {
            label.FontStyle = FontStyles.Italic;
        }

        if (row.Muted)
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextMuted");
            if (row.MutedHint is null)
            {
                label.TextDecorations = TextDecorations.Strikethrough;
            }
            else
            {
                label.ToolTip = row.MutedHint;
            }
        }

        Grid.SetColumn(label, 4);
        if (row.IsSeparator)
        {
            grid.Children.Add(label);
            var line = new Rectangle { Height = 1, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            line.SetResourceReference(Shape.FillProperty, "Theme.Separator");
            Grid.SetColumn(line, 5);
            grid.Children.Add(line);
        }
        else
        {
            Grid.SetColumnSpan(label, 2);
            grid.Children.Add(label);
        }

        if (row.Trailing is { } trailing)
        {
            trailing.Margin = new Thickness(6, 0, 0, 0);
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 6);
            grid.Children.Add(trailing);
        }

        if (row.Remove is { } remove)
        {
            var button = ArrowButton("✕", SettingsTexts.ReorderRemove, true, () => remove());
            button.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(button, 7);
            grid.Children.Add(button);
        }

        // Gestrichelte Umrandung als Drop-Hinweis; liegt über der ganzen Zeile und fängt keine Mausereignisse.
        var highlight = new Rectangle
        {
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        highlight.SetResourceReference(Shape.StrokeProperty, "Theme.Accent");
        Grid.SetColumnSpan(highlight, 8);
        grid.Children.Add(highlight);

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(6, 2, 6, 2),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.Transparent,
            Focusable = true,
            AllowDrop = true,
        };
        border.SetResourceReference(Border.BorderBrushProperty, "Theme.Separator");
        WireInteraction(border, id, highlight);
        return border;
    }

    private static Button ArrowButton(string text, string toolTip, bool enabled, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Width = 22,
            Height = 20,
            Padding = new Thickness(0),
            IsEnabled = enabled,
            ToolTip = toolTip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void WireInteraction(Border row, string id, Rectangle highlight)
    {
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInsideClickable(e.OriginalSource as DependencyObject, row))
            {
                return;
            }

            row.Focus();
            _dragId = id;
            _dragStart = e.GetPosition(row);
        };
        row.PreviewMouseLeftButtonUp += (_, _) => _dragId = null;
        row.PreviewMouseMove += (_, e) =>
        {
            if (_dragId is null || e.LeftButton != MouseButtonState.Pressed)
            {
                _dragId = null;
                return;
            }

            var position = e.GetPosition(row);
            if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var dragged = _dragId;
            _dragId = null;
            DragDrop.DoDragDrop(this, new DataObject(DataFormat, dragged), DragDropEffects.Move);
        };

        row.DragOver += (_, e) =>
        {
            AutoScroll(e);
            var ours = DraggedId(e) is { } draggedId && CanAcceptDrop(draggedId, id);
            e.Effects = ours ? DragDropEffects.Move : DragDropEffects.None;
            highlight.Visibility = ours ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        };
        row.DragLeave += (_, _) => highlight.Visibility = Visibility.Collapsed;
        row.Drop += (_, e) =>
        {
            highlight.Visibility = Visibility.Collapsed;
            if (DraggedId(e) is { } dragged && CanAcceptDrop(dragged, id))
            {
                MoveNextTo(dragged, id);
            }

            e.Handled = true;
        };

        row.KeyDown += (_, e) =>
        {
            // Mit gedrückter Alt-Taste meldet WPF Key.System; die eigentliche Taste steht in SystemKey.
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HandleKey(id, key, e.KeyboardDevice.Modifiers))
            {
                e.Handled = true;
            }
        };
    }

    /// <summary>Ob die Maus über einem Knopf oder Kontrollkästchen der Zeile liegt (dort startet kein Ziehen).</summary>
    private static bool IsInsideClickable(DependencyObject? source, DependencyObject row)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, row); node = ParentOf(node))
        {
            if (node is ButtonBase or CheckBox)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}

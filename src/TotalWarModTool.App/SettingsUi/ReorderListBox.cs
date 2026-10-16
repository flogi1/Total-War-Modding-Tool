using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>
/// One row of a <see cref="ReorderListBox"/>: id, label and optional extras.
/// </summary>
/// <param name="Id">Id of the entry (the key in the list the caller owns).</param>
/// <param name="Label">Text shown.</param>
/// <param name="Muted">Show the row dimmed (for example a hidden entry).</param>
/// <param name="MutedHint">Explanation of a dimmed row; shown as a tooltip instead of striking the label through.</param>
/// <param name="IsSeparator">Separator: italic label with a line behind it.</param>
/// <param name="Icon">Icon before the label (for example <c>ToolIcons.Create</c>); like <paramref name="Trailing"/> a NEW element on every call of the row factory.</param>
/// <param name="Trailing">
/// Element at the right edge (for example a check box). Must be a NEW element on every call of the row factory: the control moves it into its own
/// grid and sets <c>Margin</c> and <c>VerticalAlignment</c> on it. The same holds for the icon.
/// </param>
/// <param name="Remove">When set, the row shows a remove button that calls this action.</param>
public sealed record ReorderRow(
    string Id,
    string Label,
    bool Muted = false,
    string? MutedHint = null,
    bool IsSeparator = false,
    FrameworkElement? Icon = null,
    FrameworkElement? Trailing = null,
    Action? Remove = null);

/// <summary>Tooltips of the buttons of a <see cref="ReorderListBox"/>, in the current UI language.</summary>
public sealed record ReorderTexts(string MoveUp, string MoveDown, string Remove);

/// <summary>
/// List of ids the user reorders with arrow buttons, drag and drop or Alt+arrow. The id list belongs to the caller: every change runs through
/// <see cref="ReorderList"/> on a copy of <c>order()</c> and is handed to <c>commit</c> as a whole list. <c>commit</c> does not rebuild the
/// control; the owner does that through <see cref="Rebuild"/>.
/// </summary>
public sealed class ReorderListBox : ItemsControl
{
    /// <summary>Data format under which the id of the dragged row travels.</summary>
    public const string DataFormat = "twmt-reorder-id";

    /// <summary>Distance (in pixels) to the edge of the surrounding <see cref="ScrollViewer"/> from which dragging scrolls.</summary>
    private const double AutoScrollEdge = 24;

    private readonly Func<IReadOnlyList<string>> _order;
    private readonly Action<IReadOnlyList<string>> _commit;
    private readonly Func<string, ReorderRow> _row;
    private readonly ReorderTexts _texts;
    private readonly Dictionary<string, FrameworkElement> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _upButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _downButtons = new(StringComparer.Ordinal);
    private List<string> _ids = new();
    private string? _dragId;
    private Point _dragStart;

    public ReorderListBox(Func<IReadOnlyList<string>> order, Action<IReadOnlyList<string>> commit, Func<string, ReorderRow> row, ReorderTexts texts)
    {
        _order = order;
        _commit = commit;
        _row = row;
        _texts = texts;
        // Free space beside and below the rows counts for auto-scrolling too (the rows handle DragOver themselves).
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
    /// Scroll direction while dragging: -1 (up) closer than <see cref="AutoScrollEdge"/> to the top edge, +1 (down) closer than
    /// <see cref="AutoScrollEdge"/> to the bottom edge, else 0. <paramref name="y"/> is the mouse position relative to the visible area.
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
    /// Whether the dragged row may be dropped here: it has to belong to this list and must not be the target row itself (foreign ids, for
    /// example from another list, are refused).
    /// </summary>
    public bool CanAcceptDrop(string draggedId, string targetId) =>
        _ids.Contains(draggedId, StringComparer.Ordinal) && !string.Equals(draggedId, targetId, StringComparison.Ordinal);

    /// <summary>
    /// Scrolls the surrounding <see cref="ScrollViewer"/> by one line when the mouse is near its top or bottom edge while dragging; WPF does
    /// not scroll on its own during <see cref="DragDrop.DoDragDrop"/>.
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

    /// <summary>Id of the dragged row from the drag data, or <see langword="null"/> for foreign data.</summary>
    private static string? DraggedId(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormat) ? e.Data.GetData(DataFormat) as string : null;

    /// <summary>Ids in display order, as read by the last <see cref="Rebuild"/>.</summary>
    public IReadOnlyList<string> CurrentIds => _ids;

    /// <summary>Reads <c>order()</c> again and builds all rows.</summary>
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

    /// <summary>The row with the id <paramref name="id"/>; <see cref="KeyNotFoundException"/> for an unknown id.</summary>
    public FrameworkElement Row(string id) => _rows[id];

    /// <summary>The up arrow of the row <paramref name="id"/>.</summary>
    public Button UpButton(string id) => _upButtons[id];

    /// <summary>The down arrow of the row <paramref name="id"/>.</summary>
    public Button DownButton(string id) => _downButtons[id];

    /// <summary>Moves <paramref name="id"/> up by one; <see langword="false"/> without a change (already at the top or unknown).</summary>
    public bool MoveUp(string id)
    {
        var copy = _order().ToList();
        var index = copy.IndexOf(id);
        return index >= 0 && ReorderList.Swap(copy, index, index - 1) && Commit(copy);
    }

    /// <summary>Moves <paramref name="id"/> down by one; <see langword="false"/> without a change (already at the bottom or unknown).</summary>
    public bool MoveDown(string id)
    {
        var copy = _order().ToList();
        var index = copy.IndexOf(id);
        return index >= 0 && ReorderList.Swap(copy, index, index + 1) && Commit(copy);
    }

    /// <summary>Moves <paramref name="id"/> next to <paramref name="targetId"/> (rule of <see cref="ReorderList.MoveNextTo"/>).</summary>
    public bool MoveNextTo(string id, string targetId)
    {
        var copy = _order().ToList();
        return ReorderList.MoveNextTo(copy, id, targetId) && Commit(copy);
    }

    /// <summary>
    /// Keyboard command of a row: Alt+Up / Alt+Down moves it. <see langword="true"/> when the key was used and the list changed; the focus
    /// then goes to the rebuilt row of the same id.
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
    /// Sets the focus after the rebuild (the new elements can take the focus only after the layout): on the arrow button from
    /// <paramref name="arrows"/> of the id <paramref name="id"/> when it exists and is enabled, else on the row of that id.
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
                // 0 handle, 1 up, 2 down, 3 icon, 4 label, 5 line/rest, 6 extra, 7 remove
                Width = i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
        }

        // The page hint explains dragging, so the handle has no tooltip of its own (ReorderTexts carries only the button texts).
        var handle = new TextBlock
        {
            Text = "⋮⋮",
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.SizeAll,
        };
        handle.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextMuted");
        Grid.SetColumn(handle, 0);
        grid.Children.Add(handle);

        var up = ArrowButton("▲", _texts.MoveUp, canMoveUp, () => MoveAndRefocus(id, true));
        up.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(up, 1);
        grid.Children.Add(up);
        _upButtons[id] = up;

        var down = ArrowButton("▼", _texts.MoveDown, canMoveDown, () => MoveAndRefocus(id, false));
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
            var button = ArrowButton("✕", _texts.Remove, true, () => remove());
            button.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(button, 7);
            grid.Children.Add(button);
        }

        // Dashed outline as a drop hint; lies over the whole row and takes no mouse events.
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
            // With Alt held down WPF reports Key.System; the real key is in SystemKey.
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HandleKey(id, key, e.KeyboardDevice.Modifiers))
            {
                e.Handled = true;
            }
        };
    }

    /// <summary>Whether the mouse is over a button or check box of the row (no drag starts there).</summary>
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

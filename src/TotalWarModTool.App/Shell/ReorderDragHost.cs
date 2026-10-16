using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TotalWarModTool.App.Shell;

/// <summary>
/// Wrapper around one element of a reorderable bar (tile, button, separator): drag source once the mouse has moved the minimum distance, and
/// drop target for elements of the same bar. On drop it calls <c>dropNextTo(dragged, this id)</c>; the "before or after" rule is up to the
/// caller (<see cref="TotalWarModTool.Settings.ReorderList.MoveNextTo"/>). Ids of another bar (for example a tile over the toolbar) are
/// refused by <c>canAccept</c> and by the list key in the drag data.
/// </summary>
public sealed class ReorderDragHost : Grid
{
    /// <summary>Data format under which the id travels while dragging.</summary>
    public const string DataFormat = "twmt-reorder-id";

    private readonly Func<string, bool> _canAccept;
    private readonly Action<string, string> _dropNextTo;
    private readonly Rectangle _highlight;
    private Point _dragStart;
    private bool _dragArmed;

    /// <param name="listKey">Id of the bar; only elements of the same bar may be dropped here, because tile and toolbar ids may overlap.</param>
    public ReorderDragHost(string listKey, string id, FrameworkElement content, Func<string, bool> canAccept, Action<string, string> dropNextTo)
    {
        ListKey = listKey;
        Id = id;
        Content = content;
        _canAccept = canAccept;
        _dropNextTo = dropNextTo;
        Tag = id;
        AllowDrop = true;
        Background = Brushes.Transparent;

        Children.Add(content);
        _highlight = new Rectangle
        {
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _highlight.SetResourceReference(Shape.StrokeProperty, "Theme.Accent");
        Children.Add(_highlight);

        PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(this);
            _dragArmed = StartsDrag(e.OriginalSource as DependencyObject);
        };
        PreviewMouseLeftButtonUp += (_, _) => _dragArmed = false;
        PreviewMouseMove += OnPreviewMouseMove;
        DragOver += (_, e) =>
        {
            var accepted = AcceptsDrop(e.Data);
            e.Effects = accepted ? DragDropEffects.Move : DragDropEffects.None;
            _highlight.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        };
        DragLeave += (_, _) => _highlight.Visibility = Visibility.Collapsed;
        Drop += (_, e) =>
        {
            _highlight.Visibility = Visibility.Collapsed;
            if (AcceptsDrop(e.Data))
            {
                _dropNextTo(DraggedId(e.Data, ListKey)!, Id);
            }

            e.Handled = true;
        };
    }

    public string ListKey { get; }

    public string Id { get; }

    public FrameworkElement Content { get; }

    /// <summary>Whether the drop highlight is currently visible.</summary>
    public bool IsDropTarget => _highlight.Visibility == Visibility.Visible;

    /// <summary>
    /// The id from the drag data (<c>listKey|id</c>), <see langword="null"/> for foreign data or another bar. A payload without a list key
    /// (such as one from a settings list) is never accepted.
    /// </summary>
    public static string? DraggedId(IDataObject data, string listKey)
    {
        if (!data.GetDataPresent(DataFormat) || data.GetData(DataFormat) is not string payload)
        {
            return null;
        }

        var separator = payload.IndexOf('|');
        return separator > 0 && payload.AsSpan(0, separator).SequenceEqual(listKey) ? payload[(separator + 1)..] : null;
    }

    /// <summary>Whether the drag data may be dropped here: same bar, not this wrapper itself, and confirmed as belonging by the caller.</summary>
    public bool AcceptsDrop(IDataObject data) => AcceptsDrop(DraggedId(data, ListKey));

    /// <summary>Whether <paramref name="draggedId"/> (already checked against this bar) may be dropped here: not this wrapper itself and confirmed as belonging by the caller.</summary>
    public bool AcceptsDrop(string? draggedId) =>
        draggedId is not null && !string.Equals(draggedId, Id, StringComparison.Ordinal) && _canAccept(draggedId);

    /// <summary>
    /// Whether a mouse press on <paramref name="source"/> may start a drag: not inside a combo box or text box including its drop-down list
    /// (press and drag belong to the control there, for example to scroll a list). Buttons stay draggable. The list of a combo box lives in
    /// its own popup tree, so logical parents and the templated parent count as well.
    /// </summary>
    public static bool StartsDrag(DependencyObject? source)
    {
        for (var current = source; current is not null; current = ParentOf(current))
        {
            if (current is ComboBox or TextBoxBase
                || current is FrameworkElement { TemplatedParent: ComboBox or TextBoxBase }
                || current is FrameworkContentElement { TemplatedParent: ComboBox or TextBoxBase })
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Visual parent, otherwise the logical one (content elements such as <c>Run</c> only have the latter, and so does the root of a popup).</summary>
    private static DependencyObject? ParentOf(DependencyObject current) =>
        (current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : null) ?? LogicalTreeHelper.GetParent(current);

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragArmed = false;
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragArmed = false;
        DragDrop.DoDragDrop(this, new DataObject(DataFormat, ListKey + "|" + Id), DragDropEffects.Move);
    }
}

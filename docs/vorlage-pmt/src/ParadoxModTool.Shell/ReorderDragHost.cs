using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ParadoxModTool.Shell;

/// <summary>
/// Hülle um ein Element einer umsortierbaren Leiste (Kachel, Knopf, Trennlinie): Ziehquelle ab der Mindest-Mausdistanz und Ziel für Elemente
/// derselben Leiste. Beim Ablegen ruft sie <c>dropNextTo(gezogen, diese Kennung)</c>; die Regel "vor oder hinter" entscheidet der Aufrufer
/// (<see cref="ParadoxModTool.Settings.ReorderList.MoveNextTo"/>). Das Datenformat ist dasselbe wie in der Einstellungsliste, fremde
/// Kennungen (z. B. eine Kachel über der kleinen Leiste) lehnt <c>canAccept</c> ab.
/// </summary>
public sealed class ReorderDragHost : Grid
{
    /// <summary>Datenformat, unter dem beim Ziehen die Kennung mitgegeben wird (identisch mit <c>ReorderListBox.DataFormat</c>).</summary>
    public const string DataFormat = "pmt-reorder-id";

    private readonly Func<string, bool> _canAccept;
    private readonly Action<string, string> _dropNextTo;
    private readonly Rectangle _highlight;
    private Point _dragStart;
    private bool _dragArmed;

    /// <param name="listKey">Kennung der Leiste; nur Elemente derselben Leiste dürfen hier abgelegt werden (Kachel- und Werkzeugleisten-Kennungen teilen vier Strings).</param>
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

    /// <summary>Ob die Ablage-Markierung gerade sichtbar ist.</summary>
    public bool IsDropTarget => _highlight.Visibility == Visibility.Visible;

    /// <summary>
    /// Kennung aus den Ziehdaten (<c>listKey|id</c>), <see langword="null"/> bei fremden Daten oder einer anderen Leiste. Die Einstellungsliste
    /// (<c>ReorderListBox</c>) gibt nur die Kennung ohne Leiste mit und wird daher nie angenommen.
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

    /// <summary>Ob die Ziehdaten hier abgelegt werden dürfen: dieselbe Leiste, nicht diese Hülle selbst, vom Aufrufer als zugehörig bestätigt.</summary>
    public bool AcceptsDrop(IDataObject data) => AcceptsDrop(DraggedId(data, ListKey));

    /// <summary>Ob <paramref name="draggedId"/> (schon auf diese Leiste geprüft) hier abgelegt werden darf: nicht diese Hülle selbst und vom Aufrufer als zugehörig bestätigt.</summary>
    public bool AcceptsDrop(string? draggedId) =>
        draggedId is not null && !string.Equals(draggedId, Id, StringComparison.Ordinal) && _canAccept(draggedId);

    /// <summary>
    /// Ob ein Mausdruck auf <paramref name="source"/> ein Ziehen beginnen darf: nicht in einem Auswahl- oder Textfeld samt dessen
    /// aufgeklappter Liste (Drücken und Ziehen gehört dort dem Feld, z. B. zum Blättern in der Startoptionen-Liste). Knöpfe bleiben ziehbar.
    /// Die Liste eines Auswahlfelds steht in einem eigenen Popup-Baum, daher zählen auch logische Eltern und der TemplatedParent.
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

    /// <summary>Sichtbarer Elternteil, sonst der logische (Inhaltselemente wie <c>Run</c> haben nur diesen, die Wurzel eines Popups nur jenen).</summary>
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

using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TotalWarModTool.Schema;

namespace TotalWarModTool.App;

/// <summary>
/// A grid for large tables: rows and columns are virtualized. Two modes:
/// <list type="bullet">
/// <item><see cref="ColumnNames"/> makes it build one read-only text column per name that shows <c>row[i]</c> of rows that are arrays; without
/// <see cref="EditColumns"/> and <see cref="ColumnNames"/> the columns come from XAML.</item>
/// <item><see cref="EditColumns"/> makes it an editable grid over <see cref="EditRow"/>: one editor per column (a check box for a Boolean
/// column), a row header with the row number, no sorting (the row index of the view must be the row index of the document), the keys and the
/// copy of rows (see TableGrid.Editing.cs).</item>
/// </list>
/// Pure view wiring: what a key or a menu entry does is decided by the commands of the view model that is the data context.
/// </summary>
public sealed partial class TableGrid : DataGrid
{
    public static readonly DependencyProperty ColumnNamesProperty = DependencyProperty.Register(
        nameof(ColumnNames), typeof(IReadOnlyList<string>), typeof(TableGrid), new PropertyMetadata(null, OnColumnNamesChanged));

    public static readonly DependencyProperty EditColumnsProperty = DependencyProperty.Register(
        nameof(EditColumns), typeof(IReadOnlyList<FieldDefinition>), typeof(TableGrid), new PropertyMetadata(null, OnEditColumnsChanged));

    public static readonly DependencyProperty SelectedRowIndexesProperty = DependencyProperty.Register(
        nameof(SelectedRowIndexes), typeof(IReadOnlyList<int>), typeof(TableGrid), new PropertyMetadata(Array.Empty<int>()));

    // The keys of the styles in Controls.xaml that give the editors of the cells the colours of the theme.
    private const string TextEditorStyleKey = "ThemeGridCellEditor";
    private const string CheckBoxStyleKey = "ThemeGridCheckBox";
    private const string CheckBoxEditingStyleKey = "ThemeGridCheckBoxEditing";

    // A selection that the view model asks for is applied row by row; above this many rows only the first one is selected.
    private const int MaxRequestedSelection = 2_000;

    private bool _renumberQueued;

    public TableGrid()
    {
        AutoGenerateColumns = false;
        IsReadOnly = true;
        CanUserAddRows = false;
        CanUserDeleteRows = false;
        EnableRowVirtualization = true;
        EnableColumnVirtualization = true;
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        SelectionUnit = DataGridSelectionUnit.FullRow;
        SelectionMode = DataGridSelectionMode.Extended;

        // The grid has its own commands for copy and for rows: the built-in copy would put another format on the clipboard.
        ClipboardCopyMode = DataGridClipboardCopyMode.None;

        // An implicit style of DataGrid does not reach a derived control; this takes the one in scope (the themed one of the app).
        SetResourceReference(StyleProperty, typeof(DataGrid));

        _commitPendingEdit = CommitPendingEdit;
        _selectionRequested = (_, rows) => ApplySelection(rows);
        InitializeEditing();
    }

    /// <summary>The headers, in the order of the cells of a row.</summary>
    public IReadOnlyList<string>? ColumnNames
    {
        get => (IReadOnlyList<string>?)GetValue(ColumnNamesProperty);
        set => SetValue(ColumnNamesProperty, value);
    }

    /// <summary>
    /// The columns of an editable grid; setting it makes the grid editable and builds one column per field over the rows of
    /// <see cref="EditRow"/> (the cell <c>[i]</c> of a row is the value of column i). Null makes it a read-only grid again.
    /// </summary>
    public IReadOnlyList<FieldDefinition>? EditColumns
    {
        get => (IReadOnlyList<FieldDefinition>?)GetValue(EditColumnsProperty);
        set => SetValue(EditColumnsProperty, value);
    }

    /// <summary>
    /// The indexes of the selected rows in the rows of <see cref="DataGrid.ItemsSource"/> (not in the order of a sorted view), ascending. The grid
    /// also hands them to the view model of its data context.
    /// </summary>
    public IReadOnlyList<int> SelectedRowIndexes
    {
        get => (IReadOnlyList<int>)GetValue(SelectedRowIndexesProperty);
        private set => SetCurrentValue(SelectedRowIndexesProperty, value);
    }

    private static void OnColumnNamesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (TableGrid)d;
        grid.Columns.Clear();
        if (e.NewValue is not IReadOnlyList<string> names)
        {
            return;
        }

        for (var i = 0; i < names.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                // A TextBlock as header, because a plain string header treats an underscore as an access key and hides it.
                Header = new TextBlock { Text = names[i] },
                Binding = new Binding($"[{i}]") { Mode = BindingMode.OneTime },
            });
        }
    }

    private static void OnEditColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (TableGrid)d;

        // A cell that is still being edited belongs to the columns that go now.
        grid.CancelPendingEdit();
        grid.Columns.Clear();
        var fields = e.NewValue as IReadOnlyList<FieldDefinition>;
        if (fields is null)
        {
            grid.IsReadOnly = true;
            grid.ClearValue(CanUserSortColumnsProperty);
            grid.ClearValue(HeadersVisibilityProperty);
            return;
        }

        grid.IsReadOnly = false;
        grid.CanUserSortColumns = false;
        grid.HeadersVisibility = DataGridHeadersVisibility.All;
        for (var i = 0; i < fields.Count; i++)
        {
            grid.Columns.Add(grid.CreateEditColumn(i, fields[i]));
        }
    }

    // One column per field: a text editor with the rule of the cell, a check box for a Boolean. The header is a TextBlock for the underscore of
    // a name, and bold for a key column.
    private DataGridColumn CreateEditColumn(int index, FieldDefinition field)
    {
        var header = new TextBlock { Text = field.Name };
        if (field.IsKey)
        {
            header.FontWeight = FontWeights.Bold;
        }

        if (field.Type == FieldType.Boolean)
        {
            return new GridCheckBoxColumn
            {
                Header = header,
                Binding = new Binding($"[{index}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            };
        }

        var binding = new Binding($"[{index}]")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            ValidatesOnExceptions = true,
            NotifyOnValidationError = true,
        };

        // The tab is asked when the rule runs: one grid shows the tabs one after another, and two tabs of the same table share their columns.
        binding.ValidationRules.Add(new CellRule(index, () => DataContext as EditableRowsTabViewModel));
        return new GridTextColumn { Header = header, Binding = binding };
    }

    // ----- selection -----

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        var indexes = IndexesOfSelection();
        SelectedRowIndexes = indexes;
        if (_attached is not null)
        {
            _attached.SelectedRows = indexes;
        }
    }

    // The positions of the selected items in the source, found in one pass over it (a selection of every row of a large table would
    // cost the square of the row count with IndexOf). Items are compared by reference: a loc row is a record that equals an identical row.
    private IReadOnlyList<int> IndexesOfSelection()
    {
        if (SelectedItems.Count == 0 || ItemsSource is not { } source)
        {
            return [];
        }

        var wanted = new HashSet<object>(SelectedItems.Cast<object>(), ReferenceEqualityComparer.Instance);
        var indexes = new List<int>(wanted.Count);
        var position = 0;
        foreach (var item in source)
        {
            if (item is not null && wanted.Contains(item))
            {
                indexes.Add(position);
                if (indexes.Count == wanted.Count)
                {
                    break;
                }
            }

            position++;
        }

        return indexes;
    }

    // The view model asks for a selection (after insert, duplicate, paste, delete): select these rows, make the first one the current row and
    // scroll to it.
    private void ApplySelection(IReadOnlyList<int> rows)
    {
        if (ItemsSource is not IList items)
        {
            return;
        }

        var valid = rows.Where(index => index >= 0 && index < items.Count).Take(MaxRequestedSelection + 1).ToList();
        if (valid.Count > MaxRequestedSelection)
        {
            valid = [valid[0]];
        }

        // One change of the selection, so that the indexes are worked out once (see IndexesOfSelection), not once per row.
        BeginUpdateSelectedItems();
        try
        {
            SelectedItems.Clear();
            foreach (var index in valid)
            {
                SelectedItems.Add(items[index]);
            }
        }
        finally
        {
            EndUpdateSelectedItems();
        }

        if (valid.Count == 0)
        {
            return;
        }

        var first = items[valid[0]];
        if (Columns.Count > 0)
        {
            CurrentCell = new DataGridCellInfo(first, Columns[0]);
        }

        ScrollIntoView(first);
    }

    // ----- row numbers -----

    // The row header shows the number of the row, counted from 1: the messages of a paste and of a build name rows by that number.
    protected override void OnLoadingRow(DataGridRowEventArgs e)
    {
        base.OnLoadingRow(e);
        if (EditColumns is not null)
        {
            e.Row.Header = e.Row.GetIndex() + 1;
        }
    }

    // Rows that are shown already keep their container when others are inserted or removed before them: their numbers are set again. Not
    // at once: while the change is still being processed the generator has not given every container its new index. After the layout, once
    // for a burst of changes. The same moment checks that a cell that was in edit mode still is: the grid drops the edit of a row that went
    // away (an undo, a reload) without telling anybody.
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (EditColumns is not null && !_renumberQueued)
        {
            _renumberQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                _renumberQueued = false;
                RenumberRows(this);
                if (_isEditing && !AnyCellEditing(this))
                {
                    SetEditing(false);
                }
            });
        }
    }

    private static bool AnyCellEditing(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is DataGridCell { IsEditing: true } || (child is not DataGridCell && AnyCellEditing(child)))
            {
                return true;
            }
        }

        return false;
    }

    private static void RenumberRows(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is DataGridRow row)
            {
                var index = row.GetIndex();
                if (index >= 0)
                {
                    row.Header = index + 1;
                }
            }
            else
            {
                RenumberRows(child);
            }
        }
    }

    // ----- columns with the editors of the theme -----

    // The editing element and the shown element are made by the column; the style that the column gives them is the one of the system
    // theme, so the keyed styles of the app are set on them (a dynamic reference: they follow the theme).
    private sealed class GridTextColumn : DataGridTextColumn
    {
        protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
        {
            var element = base.GenerateEditingElement(cell, dataItem);
            element.SetResourceReference(FrameworkElement.StyleProperty, TextEditorStyleKey);
            return element;
        }
    }

    private sealed class GridCheckBoxColumn : DataGridCheckBoxColumn
    {
        protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
        {
            var element = base.GenerateElement(cell, dataItem);
            element.SetResourceReference(FrameworkElement.StyleProperty, CheckBoxStyleKey);
            return element;
        }

        protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
        {
            var element = base.GenerateEditingElement(cell, dataItem);
            element.SetResourceReference(FrameworkElement.StyleProperty, CheckBoxEditingStyleKey);
            return element;
        }
    }
}

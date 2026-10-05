using System.ComponentModel;
using System.Windows.Data;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.App;

/// <summary>
/// One row of the grid. The indexer gives the value the grid shows: a bool for Boolean columns, otherwise the cell text.
/// Setting it asks the tab to change the cell; a refused value throws <see cref="FormatException"/> with the UI message (the grid shows it).
/// A row wraps the cell array of the document row and lives as long as that row is part of the document.
/// </summary>
public sealed class EditRow : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs IndexerChanged = new(Binding.IndexerName);

    private readonly IReadOnlyList<FieldDefinition> _columns;
    private readonly EditableRowsTabViewModel _owner;

    internal EditRow(object?[] cells, IReadOnlyList<FieldDefinition> columns, EditableRowsTabViewModel owner)
    {
        Cells = cells;
        _columns = columns;
        _owner = owner;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The cells of the document row. The identity of the row.</summary>
    internal object?[] Cells { get; }

    /// <summary>
    /// The value of a column: a bool for a Boolean column, else the text of the cell (<see cref="CellText.Format"/>, formatted when asked).
    /// Set: a bool or a text that <see cref="CellText"/> accepts; the same value as before does nothing.
    /// </summary>
    /// <exception cref="FormatException">The value is refused; the message is the one of <see cref="EditableRowsTabViewModel.CheckCell"/>.</exception>
    public object? this[int column]
    {
        get
        {
            var field = _columns[column];
            var value = Cells[column];
            return field.Type == FieldType.Boolean && value is bool flag ? flag : CellText.Format(field.Type, value);
        }
        set => _owner.SetCellFromGrid(this, column, value);
    }

    /// <summary>Tells the grid that the values of the row changed.</summary>
    internal void RaiseChanged() => PropertyChanged?.Invoke(this, IndexerChanged);
}

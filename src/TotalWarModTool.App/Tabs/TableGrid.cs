using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace TotalWarModTool.App;

/// <summary>
/// A read-only grid for large tables: rows and columns are virtualized. <see cref="ColumnNames"/> makes it build one text column per name that
/// shows <c>row[i]</c> of rows that are arrays; without it the columns come from XAML. Pure view wiring.
/// </summary>
public sealed class TableGrid : DataGrid
{
    public static readonly DependencyProperty ColumnNamesProperty = DependencyProperty.Register(
        nameof(ColumnNames), typeof(IReadOnlyList<string>), typeof(TableGrid), new PropertyMetadata(null, OnColumnNamesChanged));

    public TableGrid()
    {
        AutoGenerateColumns = false;
        IsReadOnly = true;
        CanUserAddRows = false;
        CanUserDeleteRows = false;
        EnableRowVirtualization = true;
        EnableColumnVirtualization = true;
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);

        // An implicit style of DataGrid does not reach a derived control; this takes the one in scope (the themed one of the app).
        SetResourceReference(StyleProperty, typeof(DataGrid));
    }

    /// <summary>The headers, in the order of the cells of a row.</summary>
    public IReadOnlyList<string>? ColumnNames
    {
        get => (IReadOnlyList<string>?)GetValue(ColumnNamesProperty);
        set => SetValue(ColumnNamesProperty, value);
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
}

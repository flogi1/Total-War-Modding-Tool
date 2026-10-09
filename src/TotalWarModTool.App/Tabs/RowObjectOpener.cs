namespace TotalWarModTool.App;

/// <summary>Table is the schema name with suffix ("building_levels_tables"); KeyValues are the key cells as CellText.Format writes them, in schema order.</summary>
public sealed record RowKey(string Table, IReadOnlyList<string> KeyValues);

/// <summary>Set on <see cref="TabsViewModel.ObjectOpener"/> by the composition root; the tabs ask it.</summary>
public interface IRowObjectOpener
{
    /// <summary>
    /// Why no object can be shown from this tab right now (no game, no schema, an operation runs); null when it can. The tab adds its own
    /// reasons (no key column, no row) before it asks.
    /// </summary>
    string? DisabledReason(RowsTabViewModel tab);

    void Show(RowsTabViewModel tab, RowKey key);

    /// <summary>The answer of <see cref="DisabledReason"/> may have changed.</summary>
    event EventHandler? Changed;
}

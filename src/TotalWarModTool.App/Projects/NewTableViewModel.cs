namespace TotalWarModTool.App;

/// <summary>
/// The model of the new table dialog: a list of tables with a filter, and a name. The filter keeps the tables that contain its text anywhere
/// in the name, ignoring case; the selection stays while the table is still listed, else the first listed table is selected. With no table
/// selected (nothing listed, or no schema) OK is blocked with a text; with one, the caller's <c>validate</c> checks the name for that table
/// on every change.
/// </summary>
public sealed class NewTableViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _tables;
    private readonly Func<NewTableInput, string?> _validate;
    private string _filter = string.Empty;
    private IReadOnlyList<string> _filtered;
    private string? _selected;
    private string _name;
    private string? _error;

    // True while the list is replaced: the list box clears its selection then and reports that, which is not a choice of the user.
    private bool _replacingList;

    /// <param name="tables">All tables to choose from, in the order they are listed; empty when no schema is loaded.</param>
    /// <param name="suggestion">The name the name box starts with.</param>
    /// <param name="validate">Returns the text that says why the name cannot be used for the table, or null. Not asked while no table is selected.</param>
    public NewTableViewModel(AppTexts texts, IReadOnlyList<string> tables, string suggestion, Func<NewTableInput, string?> validate)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(validate);

        Texts = texts;
        _tables = tables;
        _validate = validate;
        _filtered = tables;
        _selected = tables.Count > 0 ? tables[0] : null;
        _name = suggestion;
        _error = Check();
    }

    public AppTexts Texts { get; }

    /// <summary>The text of the filter box.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value ?? string.Empty))
            {
                return;
            }

            Refilter();
        }
    }

    /// <summary>The tables that the filter lets through, in the order of the source list.</summary>
    public IReadOnlyList<string> FilteredTables => _filtered;

    /// <summary>The chosen table; null when the list shows none. Setting null while the list shows tables is ignored.</summary>
    public string? SelectedTable
    {
        get => _selected;
        set
        {
            // A table stays chosen while the list shows tables: a deselect from the view (Ctrl+click) is not a choice.
            if (_replacingList || (value is null && _filtered.Count > 0))
            {
                return;
            }

            if (SetProperty(ref _selected, value))
            {
                Revalidate();
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                Revalidate();
            }
        }
    }

    /// <summary>Why the input cannot be accepted; null when it can.</summary>
    public string? Error => _error;

    public bool CanAccept => _error is null;

    /// <summary>The input as it stands; the table is empty while none is selected.</summary>
    public NewTableInput Result => new(_selected ?? string.Empty, _name);

    private void Refilter()
    {
        var filtered = _filter.Length == 0
            ? _tables
            : _tables.Where(table => table.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var selected = _selected is { } current && filtered.Contains(current) ? current : filtered.Count > 0 ? filtered[0] : null;

        // The field is set first, so that the list box, which clears its selection while it takes the new list, is not heard.
        _selected = selected;
        _filtered = filtered;
        _replacingList = true;
        try
        {
            OnPropertyChanged(nameof(FilteredTables));
        }
        finally
        {
            _replacingList = false;
        }

        OnPropertyChanged(nameof(SelectedTable));
        Revalidate();
    }

    private void Revalidate()
    {
        if (SetProperty(ref _error, Check(), nameof(Error)))
        {
            OnPropertyChanged(nameof(CanAccept));
        }
    }

    private string? Check()
    {
        if (_tables.Count == 0)
        {
            return Texts.FileNoSchema;
        }

        return _selected is null ? Texts.FileTableNoMatch : _validate(Result);
    }
}

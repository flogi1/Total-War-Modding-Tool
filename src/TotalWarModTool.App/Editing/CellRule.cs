using System.Globalization;
using System.Windows.Controls;

namespace TotalWarModTool.App;

/// <summary>
/// The validation of what is typed into a cell of the editable grid: asks the tab whether the text is a value of the column
/// (<see cref="EditableRowsTabViewModel.CheckCell"/>). It runs on the raw text, before the binding writes it to the row, so a refused text
/// keeps the cell in edit mode and shows the message of the tab. The tab is asked each time (a function), because one grid shows the tabs one after another.
/// </summary>
public sealed class CellRule : ValidationRule
{
    private readonly int _column;
    private readonly Func<EditableRowsTabViewModel?> _tab;

    public CellRule(int column, Func<EditableRowsTabViewModel?> tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _column = column;
        _tab = tab;
        ValidationStep = ValidationStep.RawProposedValue;
    }

    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        // Only a text can be refused; a tab that is gone (the grid shows another one) has nothing to say.
        if (value is not string text || _tab() is not { } tab || _column >= tab.Columns.Count)
        {
            return ValidationResult.ValidResult;
        }

        return tab.CheckCell(_column, text) is { } message ? new ValidationResult(false, message) : ValidationResult.ValidResult;
    }
}

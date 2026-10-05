using System.Windows;

namespace TotalWarModTool.App;

/// <summary>The WPF side of <see cref="IEditDialogs"/>: message boxes owned by the main window. Wiring only.</summary>
public sealed class EditDialogs : IEditDialogs
{
    private readonly Func<Window?> _owner;

    /// <param name="owner">Returns the window that owns the dialogs (the main window); null when there is none.</param>
    public EditDialogs(Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public void ShowMessage(string title, string message)
    {
        if (_owner() is { IsLoaded: true } owner)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

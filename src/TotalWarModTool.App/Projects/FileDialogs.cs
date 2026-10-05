using System.Windows;

namespace TotalWarModTool.App;

/// <summary>The WPF side of <see cref="IFileDialogs"/>: the name window, the new table window, the confirmation and message boxes. Wiring only; the logic is in the view models.</summary>
public sealed class FileDialogs : IFileDialogs
{
    private readonly AppTexts _texts;
    private readonly Func<Window?> _owner;

    /// <param name="owner">Returns the window that owns the dialogs (the main window); null when there is none.</param>
    public FileDialogs(AppTexts texts, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(owner);

        _texts = texts;
        _owner = owner;
    }

    public string? AskName(string title, string label, string suggestion, Func<string, string?> validate)
    {
        var window = new NameInputWindow(new NameInputViewModel(_texts, label, suggestion, validate), title);
        Own(window);
        window.ShowDialog();
        return window.Result;
    }

    public NewTableInput? AskNewTable(IReadOnlyList<string> tables, string suggestion, Func<NewTableInput, string?> validate)
    {
        var window = new NewTableWindow(new NewTableViewModel(_texts, tables, suggestion, validate), _texts.FileNewTableTitle);
        Own(window);
        window.ShowDialog();
        return window.Result;
    }

    // "Choose another name" is the default button and the answer for Esc: Enter must never replace a whole file of the game by accident.
    public bool ConfirmSameName(string file)
    {
        var window = new ChoiceWindow(_texts.FileSameNameTitle, _texts.FileSameNameMessage, [file],
            [_texts.FileSameNameKeep, _texts.FileSameNameChange], defaultIndex: 1, cancelIndex: 1);
        Own(window);
        window.ShowDialog();
        return window.Result == 0;
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

    // A window can only be owned by one that has been shown; without one it stays on its own.
    private void Own(Window window)
    {
        if (_owner() is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }
    }
}

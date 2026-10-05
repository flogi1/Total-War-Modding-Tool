using System.Windows;

namespace TotalWarModTool.App;

/// <summary>The WPF side of <see cref="IEditDialogs"/>: message boxes and <see cref="ChoiceWindow"/> questions owned by the main window. Wiring only.</summary>
public sealed class EditDialogs : IEditDialogs
{
    private readonly AppTexts _texts;
    private readonly Func<Window?> _owner;

    /// <param name="owner">Returns the window that owns the dialogs (the main window); null when there is none.</param>
    public EditDialogs(AppTexts texts, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(owner);
        _texts = texts;
        _owner = owner;
    }

    public SaveChoice AskUnsaved(IReadOnlyList<string> files)
    {
        var answer = Ask(_texts.UnsavedTitle, _texts.UnsavedMessage, files, [_texts.UnsavedSave, _texts.UnsavedDiscard, _texts.DialogCancel],
            defaultIndex: 0, cancelIndex: 2);
        return answer switch
        {
            0 => SaveChoice.Save,
            1 => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };
    }

    public bool AskSaveAllBeforeBuild(IReadOnlyList<string> files) =>
        Ask(_texts.UnsavedTitle, _texts.UnsavedBuildMessage, files, [_texts.UnsavedBuildSaveAll, _texts.DialogCancel], defaultIndex: 0, cancelIndex: 1) == 0;

    // Cancel is the default button: Enter must never overwrite what another program wrote.
    public ConflictChoice AskConflict(string file)
    {
        var answer = Ask(_texts.ConflictTitle, _texts.ConflictMessage, [file], [_texts.ConflictOverwrite, _texts.TabReload, _texts.DialogCancel],
            defaultIndex: 2, cancelIndex: 2);
        return answer switch
        {
            0 => ConflictChoice.Overwrite,
            1 => ConflictChoice.Reload,
            _ => ConflictChoice.Cancel,
        };
    }

    // Writing the file again loses nothing, so it is the default button.
    public bool AskCreateAgain(string file) =>
        Ask(_texts.DeletedTitle, _texts.DeletedMessage, [file], [_texts.DeletedCreateAgain, _texts.DialogCancel], defaultIndex: 0, cancelIndex: 1) == 0;

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

    // The button at defaultIndex is the one that Enter presses (the safe one); the one at cancelIndex is the answer for Esc and the close box.
    private int Ask(string title, string message, IReadOnlyList<string> files, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex)
    {
        var lines = CappedLines.Of(files, _texts.ReportMoreLines);
        var window = new ChoiceWindow(title, message, lines, buttons, defaultIndex, cancelIndex);
        if (_owner() is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return window.Result;
    }
}

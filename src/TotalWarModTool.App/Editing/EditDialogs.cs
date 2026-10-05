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
        var answer = Ask(_texts.UnsavedMessage, files, [_texts.UnsavedSave, _texts.UnsavedDiscard, _texts.DialogCancel], cancelIndex: 2);
        return answer switch
        {
            0 => SaveChoice.Save,
            1 => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };
    }

    public bool AskSaveAllBeforeBuild(IReadOnlyList<string> files) =>
        Ask(_texts.UnsavedBuildMessage, files, [_texts.UnsavedBuildSaveAll, _texts.DialogCancel], cancelIndex: 1) == 0;

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

    // The first button is the safe one (save) and the default; the last is cancel.
    private int Ask(string message, IReadOnlyList<string> files, IReadOnlyList<string> buttons, int cancelIndex)
    {
        var lines = CappedLines.Of(files, _texts.ReportMoreLines);
        var window = new ChoiceWindow(_texts.UnsavedTitle, message, lines, buttons, defaultIndex: 0, cancelIndex);
        if (_owner() is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return window.Result;
    }
}

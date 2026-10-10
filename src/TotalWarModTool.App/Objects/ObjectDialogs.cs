using System.Windows;

namespace TotalWarModTool.App;

/// <summary>The WPF side of <see cref="IObjectDialogs"/>: message boxes, <see cref="ChoiceWindow"/> questions and the <see cref="NameInputWindow"/> owned by the main window. Wiring only.</summary>
public sealed class ObjectDialogs : IObjectDialogs
{
    private readonly AppTexts _texts;
    private readonly Func<Window?> _owner;

    /// <param name="owner">Returns the window that owns the dialogs (the main window); null when there is none.</param>
    public ObjectDialogs(AppTexts texts, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(owner);
        _texts = texts;
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

    public SaveChoice AskUnapplied(string objectName)
    {
        var answer = Ask(_texts.ObjectsUnappliedTitle, _texts.ObjectsUnappliedMessage(objectName), [],
            [_texts.ObjectsApply, _texts.ObjectsUnappliedDiscard, _texts.DialogCancel], defaultIndex: 0, cancelIndex: 2);
        return answer switch
        {
            0 => SaveChoice.Save,
            1 => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };
    }

    public string? AskTargetFile(string tableOrLoc, bool isLoc, IReadOnlyList<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var message = isLoc ? _texts.ObjectsTargetLocMessage : _texts.ObjectsTargetMessage(tableOrLoc);
        var answer = Ask(_texts.ObjectsTargetTitle, message, [], [.. candidates, _texts.DialogCancel], defaultIndex: 0, cancelIndex: candidates.Count);
        return answer < candidates.Count ? candidates[answer] : null;
    }

    // Cancel is the default button: the copy has consequences beyond the project, so Enter must not confirm it.
    public bool AskCopies(IReadOnlyList<string> lines) =>
        Ask(_texts.ObjectsCopyTitle, _texts.ObjectsCopyMessage, lines, [_texts.ObjectsCopyConfirm, _texts.DialogCancel], defaultIndex: 1, cancelIndex: 1) == 0;

    public string? AskNewKey(string objectName, string suggestion, Func<string, string?> validate)
    {
        var window = new NameInputWindow(new NameInputViewModel(_texts, _texts.ObjectsCopyAsLabel, suggestion, validate), _texts.ObjectsCopyAsTitle(objectName));
        if (_owner() is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return window.Result;
    }

    // The button at defaultIndex is the one that Enter presses (the safe one); the one at cancelIndex is the answer for Esc and the close box.
    private int Ask(string title, string message, IReadOnlyList<string> lines, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex)
    {
        var shown = CappedLines.Of(lines, _texts.ReportMoreLines);
        var window = new ChoiceWindow(title, message, shown, buttons, defaultIndex, cancelIndex);
        if (_owner() is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return window.Result;
    }
}

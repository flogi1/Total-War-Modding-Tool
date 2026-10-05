namespace TotalWarModTool.App;

/// <summary>
/// The clipboard as text. An outside-world interface: the WPF clipboard needs an STA thread and can fail when another program holds it, so
/// the tests use a fake that holds a string.
/// </summary>
public interface IClipboard
{
    /// <summary>The text on the clipboard; null when there is no text or the clipboard cannot be opened.</summary>
    string? GetText();

    /// <summary>Puts the text on the clipboard. False when the clipboard cannot be opened; then nothing was changed.</summary>
    bool SetText(string text);
}

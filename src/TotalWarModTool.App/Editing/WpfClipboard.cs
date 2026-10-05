using System.Runtime.InteropServices;
using System.Windows;

namespace TotalWarModTool.App;

/// <summary>
/// The clipboard of the system. Must be used on the UI thread. Another program that holds the clipboard makes the calls throw a
/// <see cref="COMException"/> (an <see cref="ExternalException"/>); that is reported as "no text" and "not set", never as a crash.
/// </summary>
public sealed class WpfClipboard : IClipboard
{
    public string? GetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    public bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            // copy: true keeps the text on the clipboard after the app ends.
            Clipboard.SetDataObject(text, true);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }
}

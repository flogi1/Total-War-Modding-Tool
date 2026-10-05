namespace TotalWarModTool.App.Tests;

/// <summary>Stand-in for the clipboard: holds one string. <see cref="Available"/> false makes it behave like a clipboard another program holds.</summary>
internal sealed class FakeClipboard : IClipboard
{
    /// <summary>The text on the clipboard; null for none.</summary>
    public string? Text { get; set; }

    /// <summary>False: <see cref="GetText"/> gives null and <see cref="SetText"/> gives false and changes nothing.</summary>
    public bool Available { get; set; } = true;

    /// <summary>Number of successful <see cref="SetText"/> calls.</summary>
    public int Sets { get; private set; }

    public string? GetText() => Available ? Text : null;

    public bool SetText(string text)
    {
        if (!Available)
        {
            return false;
        }

        Text = text;
        Sets++;
        return true;
    }
}

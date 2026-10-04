namespace TotalWarModTool.Projects;

/// <summary>Which names can be the file name of a mod pack.</summary>
public static class PackNameRules
{
    private const int MaxLength = 100;
    private const string Extension = ".pack";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Null when <paramref name="packName"/> can be the file name of a mod pack; otherwise a short English reason.</summary>
    public static string? Check(string packName)
    {
        if (string.IsNullOrEmpty(packName))
        {
            return "The pack name is empty.";
        }

        if (packName.Length > MaxLength)
        {
            return $"The pack name is longer than {MaxLength} characters.";
        }

        if (!IsWellFormed(packName))
        {
            return "The pack name contains an unpaired surrogate character.";
        }

        if (packName.StartsWith(' ') || packName.EndsWith(' '))
        {
            return "The pack name starts or ends with a space.";
        }

        if (packName.Contains('\\') || packName.Contains('/') || packName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "The pack name contains a character that is not allowed in a file name.";
        }

        if (!packName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || packName.Length == Extension.Length)
        {
            return "The pack name must be a name followed by .pack.";
        }

        var firstDot = packName.IndexOf('.');
        if (ReservedNames.Contains(packName[..firstDot].TrimEnd(' ')))
        {
            return "The pack name is a reserved Windows device name.";
        }

        return null;
    }

    /// <summary>True when <paramref name="text"/> has no unpaired surrogate and can therefore be encoded as UTF-8.</summary>
    internal static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}

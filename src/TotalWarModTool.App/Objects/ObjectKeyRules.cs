namespace TotalWarModTool.App;

/// <summary>The checks of a new key, shared by "Copy as" and the wizards.</summary>
internal static class ObjectKeyRules
{
    /// <summary>
    /// Empty: objects.copyAs.key.empty; any char.IsWhiteSpace: objects.copyAs.key.whitespace; existsIn(key) not null: objects.copyAs.key.exists with
    /// that text (escaped); else null.
    /// </summary>
    /// <param name="existsIn">Where the key exists already (the source of its first row), or null when it does not.</param>
    public static string? Problem(AppTexts texts, string key, Func<string, string?> existsIn)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(existsIn);
        if (key.Length == 0)
        {
            return texts.ObjectsCopyAsKeyEmpty;
        }

        if (key.Any(char.IsWhiteSpace))
        {
            return texts.ObjectsCopyAsKeyWhitespace;
        }

        return existsIn(key) is { } source ? texts.ObjectsCopyAsKeyExists(MessageText.EscapeControlCharacters(source)) : null;
    }
}

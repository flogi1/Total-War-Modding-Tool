using System.Globalization;

namespace TotalWarModTool.Localization;

/// <summary>A selectable UI language: its code (file name in <c>locales</c>) and its name in the language itself.</summary>
public sealed record UiLanguageOption(string Code, string Name);

/// <summary>The UI languages that have a file in the <c>locales</c> folder. English (reference language) is always included and comes first.</summary>
public static class LanguageCatalog
{
    public static IReadOnlyList<UiLanguageOption> Find(string localesDirectory)
    {
        var codes = Directory.Exists(localesDirectory)
            ? Directory.EnumerateFiles(localesDirectory, "*.json")
                .Select(path => Path.GetFileNameWithoutExtension(path).ToLowerInvariant())
                .Where(code => code.Length > 0 && !code.StartsWith('_'))
            : [];

        return codes
            .Append(UiText.ReferenceLanguage)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code == UiText.ReferenceLanguage ? 0 : 1)
            .ThenBy(code => code, StringComparer.Ordinal)
            .Select(code => new UiLanguageOption(code, DisplayName(code)))
            .ToList();
    }

    /// <summary>Name of the language in itself with a capital first letter (English, Deutsch, Français); an unknown code stays as it is.</summary>
    public static string DisplayName(string code)
    {
        switch (code)
        {
            case "de":
                return "Deutsch";
            case "en":
                return "English";
        }

        // Predefined cultures only: for an invented code like "xx-nope" .NET would otherwise build a name from the code itself.
        var culture = CultureInfo.GetCultures(CultureTypes.AllCultures)
            .FirstOrDefault(c => string.Equals(c.Name, code, StringComparison.OrdinalIgnoreCase));
        if (culture is null || culture.NativeName.Length == 0)
        {
            return code;
        }

        return char.ToUpper(culture.NativeName[0], culture) + culture.NativeName[1..];
    }
}

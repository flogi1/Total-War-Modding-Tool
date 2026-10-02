using System.Globalization;

namespace TotalWarModTool.Localization;

/// <summary>
/// UI texts of the running session, read from the community-editable files <c>locales/&lt;language&gt;.json</c> (flat keys).
/// Lookup chain: chosen language, then English (the reference language), then the inline fallback given in code.
/// Empty texts count as missing. A new instance loads nothing: until the first <see cref="Load"/> the inline fallbacks apply.
/// </summary>
public sealed class UiText
{
    public const string ReferenceLanguage = "en";

    private readonly string _localesDirectory;

    // One immutable snapshot, replaced in a single assignment, so readers never see a language with the chain of another.
    private volatile Snapshot _snapshot = new(ReferenceLanguage, null, []);

    public UiText(string localesDirectory)
    {
        _localesDirectory = localesDirectory;
    }

    /// <summary>Raised at the end of every <see cref="Load"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>The loaded language code; <c>en</c> before the first <see cref="Load"/>.</summary>
    public string Language => _snapshot.Language;

    /// <summary>JSON error of the file of the chosen language; <see langword="null"/> if that file is missing or valid.</summary>
    public LocaleParseError? LoadError => _snapshot.Error;

    /// <summary>Loads the language files. Null or blank means <c>en</c>; any other value is trimmed and lower-cased.</summary>
    public void Load(string? language)
    {
        var chosen = string.IsNullOrWhiteSpace(language) ? ReferenceLanguage : language.Trim().ToLowerInvariant();
        var selected = LocaleFileReader.Read(FilePath(chosen));
        var chain = new List<IReadOnlyDictionary<string, string>> { selected.Texts };
        if (chosen != ReferenceLanguage)
        {
            chain.Add(LocaleFileReader.Read(FilePath(ReferenceLanguage)).Texts);
        }

        _snapshot = new Snapshot(chosen, selected.Error, chain);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key, string fallback)
    {
        foreach (var texts in _snapshot.Chain)
        {
            if (texts.TryGetValue(key, out var text) && text.Length > 0)
            {
                return text;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Like <see cref="Get"/> with the placeholders {0}, {1} filled in using the current culture. If a pattern is broken
    /// (<see cref="FormatException"/>), the next link of the chain applies, finally the inline fallback pattern.
    /// </summary>
    public string Format(string key, string fallback, params object?[] args)
    {
        foreach (var texts in _snapshot.Chain)
        {
            if (texts.TryGetValue(key, out var pattern) && pattern.Length > 0)
            {
                try
                {
                    return string.Format(CultureInfo.CurrentCulture, pattern, args);
                }
                catch (FormatException)
                {
                    // Broken pattern in this language file: try the next link of the chain.
                }
            }
        }

        return string.Format(CultureInfo.CurrentCulture, fallback, args);
    }

    private string FilePath(string language) => Path.Combine(_localesDirectory, language + ".json");

    private sealed record Snapshot(string Language, LocaleParseError? Error, IReadOnlyList<IReadOnlyDictionary<string, string>> Chain);
}

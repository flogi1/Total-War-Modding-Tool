using System.Text.Json;

namespace TotalWarModTool.Localization;

/// <summary>Line and position (both 1-based) and message of a JSON error in a locale file.</summary>
public sealed record LocaleParseError(long Line, long Position, string Message);

/// <summary>Content of a locale file: its texts (without comment keys) and the JSON error if the file is broken.</summary>
public sealed record LocaleFile(IReadOnlyDictionary<string, string> Texts, LocaleParseError? Error);

/// <summary>Reads a flat JSON locale file (<c>locales/&lt;language&gt;.json</c>) into key/text pairs.</summary>
public static class LocaleFileReader
{
    private static readonly IReadOnlyDictionary<string, string> NoTexts = new Dictionary<string, string>();

    /// <summary>
    /// Keys starting with an underscore are comments, values that are not strings are ignored. Empty texts are kept in the result;
    /// callers treat them as missing. A missing or unreadable file, or a root that is not an object, gives an empty result without an error.
    /// A broken file gives an empty result with the JSON error.
    /// </summary>
    public static LocaleFile Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new LocaleFile(NoTexts, null);
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new LocaleFile(NoTexts, null);
            }

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.StartsWith('_') && property.Value.ValueKind == JsonValueKind.String)
                {
                    texts[property.Name] = property.Value.GetString()!;
                }
            }

            return new LocaleFile(texts, null);
        }
        catch (JsonException ex)
        {
            return new LocaleFile(NoTexts, new LocaleParseError((ex.LineNumber ?? 0) + 1, (ex.BytePositionInLine ?? 0) + 1, ex.Message));
        }
        catch (IOException)
        {
            return new LocaleFile(NoTexts, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new LocaleFile(NoTexts, null);
        }
    }
}

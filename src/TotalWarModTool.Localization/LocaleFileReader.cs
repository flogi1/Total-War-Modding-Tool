using System.Text;
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
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Keys starting with an underscore are comments, values that are not strings are ignored. Empty texts are kept in the result;
    /// callers treat them as missing. A missing or unreadable file, or a root that is not an object, gives an empty result without an error.
    /// A broken file (invalid JSON, or bytes that are not valid UTF-8, e.g. saved as ANSI) gives an empty result with the error.
    /// </summary>
    public static LocaleFile Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new LocaleFile(NoTexts, null);
            }

            // JsonDocument accepts invalid UTF-8 bytes and only throws later when a text is read, so check the encoding first.
            var bytes = File.ReadAllBytes(path);
            var content = bytes.AsMemory(bytes.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0);
            try
            {
                StrictUtf8.GetCharCount(content.Span);
            }
            catch (DecoderFallbackException ex)
            {
                return new LocaleFile(NoTexts, NotUtf8Error(content.Span, ex.Index));
            }

            using var document = JsonDocument.Parse(content);
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

    // Line and position (1-based, in bytes) of the first byte that is not valid UTF-8.
    private static LocaleParseError NotUtf8Error(ReadOnlySpan<byte> content, int index)
    {
        var lastLineBreak = content[..index].LastIndexOf((byte)'\n');
        var line = content[..index].Count((byte)'\n') + 1L;
        return new LocaleParseError(line, index - lastLineBreak, "The file is not valid UTF-8. Save it as UTF-8.");
    }
}

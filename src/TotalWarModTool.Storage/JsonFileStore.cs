using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TotalWarModTool.Storage;

public enum LoadStatus { Loaded, Missing, Corrupt, NewerVersion, Unreadable }

/// <summary>Outcome of <see cref="JsonFileStore.Load{T}"/>. <paramref name="BadCopyPath"/> is set only if a corrupt file was copied to a .bad file.</summary>
public sealed record LoadResult<T>(T Value, LoadStatus Status, string? BadCopyPath);

/// <summary>
/// Loads and saves one JSON file per object. The root object carries <c>schemaVersion</c> as its first property,
/// next to the properties of the stored object. A broken file never throws: it is copied to a .bad file and defaults are used.
/// </summary>
public static class JsonFileStore
{
    private const string SchemaVersionProperty = "schemaVersion";
    private const string BadSuffix = ".bad";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static LoadResult<T> Load<T>(string path, int currentSchemaVersion, Func<T> createDefault) where T : class
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new LoadResult<T>(createDefault(), LoadStatus.Missing, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LoadResult<T>(createDefault(), LoadStatus.Unreadable, null);
        }

        if (!TryParse<T>(text, out var value, out var version))
        {
            return new LoadResult<T>(createDefault(), LoadStatus.Corrupt, CopyToBad(path));
        }

        var status = version > currentSchemaVersion ? LoadStatus.NewerVersion : LoadStatus.Loaded;
        return new LoadResult<T>(value, status, null);
    }

    public static void Save<T>(string path, int schemaVersion, T value) where T : class
    {
        var body = JsonSerializer.SerializeToNode(value, Options) as JsonObject
            ?? throw new InvalidOperationException(typeof(T).Name + " must serialize to a JSON object.");

        var root = new JsonObject { [SchemaVersionProperty] = schemaVersion };
        foreach (var (name, node) in body)
        {
            root.Add(name, node?.DeepClone());
        }

        AtomicFile.WriteAllText(path, root.ToJsonString(Options));
    }

    private static bool TryParse<T>(string text, [NotNullWhen(true)] out T? value, out int version) where T : class
    {
        value = null;
        version = 0;

        // JsonDocument instead of JsonNode: it tolerates duplicate property names in a hand-edited file instead of throwing later.
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(SchemaVersionProperty, out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out version))
            {
                return false;
            }

            try
            {
                value = root.Deserialize<T>(Options);
                return value is not null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
    /// <summary>Keeps the broken file for the user. If even the copy fails, the caller still gets defaults and no path.</summary>
    private static string? CopyToBad(string path)
    {
        var badPath = path + BadSuffix;
        try
        {
            File.Copy(path, badPath, overwrite: true);
            return badPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
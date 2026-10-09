using System.Text.Json;

namespace TotalWarModTool.Objects;

public enum SectionMode
{
    List,
    Sentence,
}

/// <summary>FieldName null means every section of the table. Pattern holds {fieldName} placeholders for Sentence.</summary>
public sealed record SectionRule(string Table, string? FieldName, string? TitleKey, SectionMode Mode, string? Pattern, bool Hidden);

public sealed record CategoryDefinition(string RootTable, string TitleKey, IReadOnlyList<SectionRule> Sections);

public sealed record CategoryProblem(string FileName, string Detail);

public sealed record CategoryLoadResult(IReadOnlyDictionary<string, CategoryDefinition> ByRootTable, IReadOnlyList<CategoryProblem> Problems);

/// <summary>Loads the category files that shape how an object kind is shown. A broken file becomes a problem, it never throws.</summary>
public static class CategoryDefinitions
{
    public const int SchemaVersion = 1;

    /// <summary>Loads every top-level *.json of the folder in ordinal name order; a missing folder gives an empty result. Never throws.</summary>
    public static CategoryLoadResult LoadAll(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var byRoot = new Dictionary<string, CategoryDefinition>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<CategoryProblem>();
        if (!Directory.Exists(folder))
        {
            return new CategoryLoadResult(byRoot, problems);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (IOException ex)
        {
            problems.Add(new CategoryProblem(folder, "Category folder cannot be listed: " + ex.Message));
            return new CategoryLoadResult(byRoot, problems);
        }
        catch (UnauthorizedAccessException ex)
        {
            problems.Add(new CategoryProblem(folder, "Category folder cannot be listed: " + ex.Message));
            return new CategoryLoadResult(byRoot, problems);
        }

        var names = files.Select(Path.GetFileName).OfType<string>().ToList();
        names.Sort(StringComparer.Ordinal);

        foreach (var name in names)
        {
            var category = TryLoad(Path.Combine(folder, name), out var detail);
            if (category is null)
            {
                problems.Add(new CategoryProblem(name, detail));
            }
            else if (!byRoot.TryAdd(category.RootTable, category))
            {
                problems.Add(new CategoryProblem(name, "Root table '" + category.RootTable + "' is already defined by an earlier file."));
            }
        }

        return new CategoryLoadResult(byRoot, problems);
    }

    private static CategoryDefinition? TryLoad(string path, out string detail)
    {
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            detail = "File cannot be read: " + ex.Message;
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            detail = "File cannot be read: " + ex.Message;
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return Parse(document.RootElement, out detail);
        }
        catch (JsonException ex)
        {
            detail = "Not valid JSON: " + ex.Message;
            return null;
        }
        catch (InvalidOperationException ex)
        {
            // JsonElement.GetString() throws this for a lone escaped surrogate, which JsonDocument.Parse accepts.
            detail = "Contains text that is not valid Unicode: " + ex.Message;
            return null;
        }
    }

    private static CategoryDefinition? Parse(JsonElement root, out string detail)
    {
        detail = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            detail = "The category must be a JSON object.";
            return null;
        }

        if (!TryGetProperty(root, "schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var versionNumber)
            || versionNumber != SchemaVersion)
        {
            detail = "schemaVersion is missing or not " + SchemaVersion + ".";
            return null;
        }

        if (!TryGetRequiredString(root, "rootTable", out var rootTable, out detail)
            || !TryGetRequiredString(root, "titleKey", out var titleKey, out detail))
        {
            return null;
        }

        var rules = new List<SectionRule>();
        if (TryGetProperty(root, "sections", out var sections) && sections.ValueKind != JsonValueKind.Null)
        {
            if (sections.ValueKind != JsonValueKind.Array)
            {
                detail = "sections must be an array.";
                return null;
            }

            var index = 0;
            foreach (var item in sections.EnumerateArray())
            {
                var rule = ParseRule(item, index, out detail);
                if (rule is null)
                {
                    return null;
                }

                rules.Add(rule);
                index++;
            }
        }

        return new CategoryDefinition(rootTable, titleKey, rules);
    }

    private static SectionRule? ParseRule(JsonElement item, int index, out string detail)
    {
        var where = "sections[" + index + "]: ";
        if (item.ValueKind != JsonValueKind.Object)
        {
            detail = where + "must be an object.";
            return null;
        }

        if (!TryGetRequiredString(item, "table", out var table, out detail)
            || !TryGetOptionalString(item, "field", out var fieldName, out detail)
            || !TryGetOptionalString(item, "titleKey", out var titleKey, out detail)
            || !TryGetOptionalString(item, "pattern", out var pattern, out detail))
        {
            detail = where + detail;
            return null;
        }

        var mode = SectionMode.List;
        if (TryGetProperty(item, "mode", out var modeElement) && modeElement.ValueKind != JsonValueKind.Null)
        {
            var modeText = modeElement.ValueKind == JsonValueKind.String ? modeElement.GetString() : null;
            if (string.Equals(modeText, "list", StringComparison.OrdinalIgnoreCase))
            {
                mode = SectionMode.List;
            }
            else if (string.Equals(modeText, "sentence", StringComparison.OrdinalIgnoreCase))
            {
                mode = SectionMode.Sentence;
            }
            else
            {
                detail = where + "mode must be \"list\" or \"sentence\".";
                return null;
            }
        }

        if (mode == SectionMode.Sentence)
        {
            if (pattern is null)
            {
                detail = where + "pattern is required for mode \"sentence\".";
                return null;
            }

            if (!ObjectText.TryParsePattern(pattern, out _))
            {
                detail = where + "pattern has an unclosed or empty {placeholder}.";
                return null;
            }
        }

        var hidden = false;
        if (TryGetProperty(item, "hidden", out var hiddenElement) && hiddenElement.ValueKind != JsonValueKind.Null)
        {
            if (hiddenElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                detail = where + "hidden must be true or false.";
                return null;
            }

            hidden = hiddenElement.GetBoolean();
        }

        detail = string.Empty;
        return new SectionRule(table, fieldName, titleKey, mode, pattern, hidden);
    }

    // Property names are matched ignoring case, like the other loaders of the profile folder.
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetRequiredString(JsonElement element, string name, out string value, out string detail)
    {
        value = string.Empty;
        if (!TryGetProperty(element, name, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            detail = name + " is missing or not a non-empty string.";
            return false;
        }

        value = property.GetString()!;
        detail = string.Empty;
        return true;
    }

    private static bool TryGetOptionalString(JsonElement element, string name, out string? value, out string detail)
    {
        value = null;
        detail = string.Empty;
        if (!TryGetProperty(element, name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            detail = name + " must be a non-empty string when present.";
            return false;
        }

        value = property.GetString();
        return true;
    }
}

using System.Text.Json;

namespace TotalWarModTool.Objects;

/// <summary>A text column kept in the loc files: the table whose rows own the text and the column that holds its key.</summary>
public sealed record TextRef(string Table, string Column);

/// <summary>
/// The tables and fields the building wizard works on, read from the game profile. The only game rule in code is the chain and stage logic of the wizard.
/// </summary>
public sealed record BuildingWizardDefinition(string LevelTable, string ChainField, string LevelField, string ChainTable, string ChainKeySuffix,
    string UpgradeTable, string UpgradeFromField, string UpgradeToField, string VariantTable, string VariantBuildingField, string VariantCultureField,
    string VariantSubcultureField, string SubcultureTable, string SubcultureCultureField, TextRef NameText)
{
    /// <summary>"table" or "table.field" for every name the graph does not know (Latest(table) null or no such field), in declaration order.</summary>
    public IReadOnlyList<string> MissingIn(ReferenceGraph graph)
    {
        var missing = new List<string>();
        WizardNames.Check(graph, LevelTable, [ChainField, LevelField], missing);
        WizardNames.Check(graph, ChainTable, [], missing);
        WizardNames.Check(graph, UpgradeTable, [UpgradeFromField, UpgradeToField], missing);
        WizardNames.Check(graph, VariantTable, [VariantBuildingField, VariantCultureField, VariantSubcultureField], missing);
        WizardNames.Check(graph, SubcultureTable, [SubcultureCultureField], missing);

        // The column of a text is a loc column, not a schema field: only the table is checked.
        WizardNames.Check(graph, NameText.Table, [], missing);
        return missing;
    }
}

public sealed record TraitWizardDefinition(string TraitTable, TextRef NameText)
{
    /// <summary>"table" or "table.field" for every name the graph does not know, in declaration order.</summary>
    public IReadOnlyList<string> MissingIn(ReferenceGraph graph)
    {
        var missing = new List<string>();
        WizardNames.Check(graph, TraitTable, [], missing);
        WizardNames.Check(graph, NameText.Table, [], missing);
        return missing;
    }
}

/// <summary>NameSuffix is appended to the copied names; it is data, not a UI text. A wizard whose section is absent is not offered.</summary>
public sealed record WizardDefinitions(string NameSuffix, BuildingWizardDefinition? Building, TraitWizardDefinition? Trait);

/// <summary>Definitions null and Problem null: the file does not exist. Definitions null and Problem set: the file cannot be used.</summary>
public sealed record WizardLoadResult(WizardDefinitions? Definitions, string? Problem);

/// <summary>Loads the wizard file of the game profile. A broken file becomes a problem, it never throws.</summary>
public static class WizardDefinitionLoader
{
    public const int SchemaVersion = 1;
    public const string FileName = "wizards.json";

    /// <summary>Reads the file like CategoryDefinitions reads a category file (shared read, never throws).</summary>
    public static WizardLoadResult Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new WizardLoadResult(null, null);
        }

        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return new WizardLoadResult(null, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new WizardLoadResult(null, null);
        }
        catch (IOException ex)
        {
            return new WizardLoadResult(null, "File cannot be read: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new WizardLoadResult(null, "File cannot be read: " + ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var definitions = Parse(document.RootElement, out var detail);
            return new WizardLoadResult(definitions, definitions is null ? detail : null);
        }
        catch (JsonException ex)
        {
            return new WizardLoadResult(null, "Invalid JSON: " + ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // JsonElement.GetString() throws this for a lone escaped surrogate, which JsonDocument.Parse accepts.
            return new WizardLoadResult(null, "Invalid JSON: " + ex.Message);
        }
    }

    private static WizardDefinitions? Parse(JsonElement root, out string detail)
    {
        detail = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            detail = "The file must be a JSON object.";
            return null;
        }

        if (!TryGetProperty(root, "schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var versionNumber)
            || versionNumber != SchemaVersion)
        {
            detail = "schemaVersion must be " + SchemaVersion + ".";
            return null;
        }

        var nameSuffix = string.Empty;
        if (TryGetProperty(root, "nameSuffix", out var suffix) && suffix.ValueKind != JsonValueKind.Null)
        {
            if (suffix.ValueKind != JsonValueKind.String)
            {
                detail = "nameSuffix must be a string.";
                return null;
            }

            nameSuffix = suffix.GetString()!;
        }

        BuildingWizardDefinition? building = null;
        if (TryGetSection(root, "building", out var buildingElement, out detail))
        {
            if (buildingElement is { } element)
            {
                building = ParseBuilding(element, out detail);
                if (building is null)
                {
                    return null;
                }
            }
        }
        else
        {
            return null;
        }

        TraitWizardDefinition? trait = null;
        if (TryGetSection(root, "trait", out var traitElement, out detail))
        {
            if (traitElement is { } element)
            {
                trait = ParseTrait(element, out detail);
                if (trait is null)
                {
                    return null;
                }
            }
        }
        else
        {
            return null;
        }

        detail = string.Empty;
        return new WizardDefinitions(nameSuffix, building, trait);
    }

    private static BuildingWizardDefinition? ParseBuilding(JsonElement element, out string detail)
    {
        const string section = "building";
        if (!TryGetRequiredString(element, section, "levelTable", out var levelTable, out detail)
            || !TryGetRequiredString(element, section, "chainField", out var chainField, out detail)
            || !TryGetRequiredString(element, section, "levelField", out var levelField, out detail)
            || !TryGetRequiredString(element, section, "chainTable", out var chainTable, out detail)
            || !TryGetRequiredString(element, section, "chainKeySuffix", out var chainKeySuffix, out detail)
            || !TryGetRequiredString(element, section, "upgradeTable", out var upgradeTable, out detail)
            || !TryGetRequiredString(element, section, "upgradeFromField", out var upgradeFromField, out detail)
            || !TryGetRequiredString(element, section, "upgradeToField", out var upgradeToField, out detail)
            || !TryGetRequiredString(element, section, "variantTable", out var variantTable, out detail)
            || !TryGetRequiredString(element, section, "variantBuildingField", out var variantBuildingField, out detail)
            || !TryGetRequiredString(element, section, "variantCultureField", out var variantCultureField, out detail)
            || !TryGetRequiredString(element, section, "variantSubcultureField", out var variantSubcultureField, out detail)
            || !TryGetRequiredString(element, section, "subcultureTable", out var subcultureTable, out detail)
            || !TryGetRequiredString(element, section, "subcultureCultureField", out var subcultureCultureField, out detail)
            || !TryGetTextRef(element, section, out var nameText, out detail))
        {
            return null;
        }

        return new BuildingWizardDefinition(levelTable, chainField, levelField, chainTable, chainKeySuffix, upgradeTable, upgradeFromField,
            upgradeToField, variantTable, variantBuildingField, variantCultureField, variantSubcultureField, subcultureTable,
            subcultureCultureField, nameText);
    }

    private static TraitWizardDefinition? ParseTrait(JsonElement element, out string detail)
    {
        const string section = "trait";
        if (!TryGetRequiredString(element, section, "traitTable", out var traitTable, out detail)
            || !TryGetTextRef(element, section, out var nameText, out detail))
        {
            return null;
        }

        return new TraitWizardDefinition(traitTable, nameText);
    }

    // A section that is absent or null is simply not offered; one that is present must be an object.
    private static bool TryGetSection(JsonElement root, string name, out JsonElement? section, out string detail)
    {
        section = null;
        detail = string.Empty;
        if (!TryGetProperty(root, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            detail = name + " must be an object.";
            return false;
        }

        section = value;
        return true;
    }

    private static bool TryGetTextRef(JsonElement section, string sectionName, out TextRef value, out string detail)
    {
        value = new TextRef(string.Empty, string.Empty);
        if (!TryGetProperty(section, "nameText", out var text) || text.ValueKind != JsonValueKind.Object)
        {
            detail = sectionName + ".nameText is missing.";
            return false;
        }

        if (!TryGetRequiredString(text, sectionName + ".nameText", "table", out var table, out detail)
            || !TryGetRequiredString(text, sectionName + ".nameText", "column", out var column, out detail))
        {
            return false;
        }

        value = new TextRef(table, column);
        return true;
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

    private static bool TryGetRequiredString(JsonElement element, string owner, string name, out string value, out string detail)
    {
        value = string.Empty;
        if (!TryGetProperty(element, name, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            detail = owner + "." + name + " is missing.";
            return false;
        }

        value = property.GetString()!;
        detail = string.Empty;
        return true;
    }
}

internal static class WizardNames
{
    /// <summary>Adds "table" if the graph does not know the table (and then skips its fields), else "table.field" for each unknown field; each entry once.</summary>
    internal static void Check(ReferenceGraph graph, string table, string[] fields, List<string> missing)
    {
        var definition = graph.Latest(table);
        if (definition is null)
        {
            AddOnce(missing, table);
            return;
        }

        foreach (var field in fields)
        {
            if (!definition.Fields.Any(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase)))
            {
                AddOnce(missing, table + "." + field);
            }
        }
    }

    private static void AddOnce(List<string> missing, string name)
    {
        if (!missing.Contains(name, StringComparer.Ordinal))
        {
            missing.Add(name);
        }
    }
}

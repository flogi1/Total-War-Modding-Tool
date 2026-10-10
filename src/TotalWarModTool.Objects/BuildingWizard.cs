using System.Globalization;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

/// <summary>A choice of a wizard list: the key and its game text (null without one).</summary>
public sealed record WizardChoice(string Key, string? Name);

public sealed record BuildingStage(string LevelKey, long Level, string? Name);

/// <summary>The subcultures, templates and chains the building wizard offers, read once from the merged tables of a load order. Read-only.</summary>
public sealed class BuildingCatalog
{
    private sealed record Level(string Key, string Chain, long Number);

    private sealed record Variant(string Building, string Culture, string Subculture, string? Name);

    private readonly IReadOnlyDictionary<string, string> _cultureOf;
    private readonly IReadOnlyList<Level> _levels;
    private readonly IReadOnlyDictionary<string, Level> _levelByKey;
    private readonly IReadOnlyList<Variant> _variants;
    private readonly IReadOnlyDictionary<string, string?> _nameOfLevel;
    private readonly HashSet<string> _chains;
    private readonly string _variantTable;
    private readonly string _variantBuildingField;
    private readonly string _variantSubcultureField;
    private readonly string _variantCultureField;
    private readonly string _levelTable;

    internal BuildingCatalog(BuildingWizardDefinition definition, IReadOnlyList<WizardChoice> subcultures, IReadOnlyDictionary<string, string> cultureOf,
        IReadOnlyList<(string Key, string Chain, long Number)> levels, IReadOnlyList<(string Building, string Culture, string Subculture, string? Name)> variants)
    {
        Subcultures = subcultures;
        _cultureOf = cultureOf;
        _levels = levels.Select(l => new Level(l.Key, l.Chain, l.Number)).ToList();
        _levelByKey = _levels.GroupBy(l => l.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _variants = variants.Select(v => new Variant(v.Building, v.Culture, v.Subculture, v.Name)).ToList();
        _chains = _levels.Where(l => l.Chain.Length > 0).Select(l => l.Chain).ToHashSet(StringComparer.Ordinal);

        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var variant in _variants)
        {
            if (variant.Name is not null && !names.ContainsKey(variant.Building))
            {
                names[variant.Building] = variant.Name;
            }
        }

        _nameOfLevel = names;
        _variantTable = definition.VariantTable;
        _variantBuildingField = definition.VariantBuildingField;
        _variantSubcultureField = definition.VariantSubcultureField;
        _variantCultureField = definition.VariantCultureField;
        _levelTable = definition.LevelTable;
    }

    /// <summary>Every subculture with its name (DisplayName of the subculture), sorted by Name ?? Key (OrdinalIgnoreCase, then Key ordinal).</summary>
    public IReadOnlyList<WizardChoice> Subcultures { get; }

    /// <summary>The levels with a variant whose subculture is this one or whose culture is the culture of this one; each once; Name from the loc text of
    /// the first matching variant that has one (NameText column, loc key of the variant's ObjectId); sorted like Subcultures. Empty for an unknown subculture.</summary>
    public IReadOnlyList<WizardChoice> TemplatesOf(string subculture)
    {
        ArgumentNullException.ThrowIfNull(subculture);
        if (!_cultureOf.TryGetValue(subculture, out var culture))
        {
            return [];
        }

        var matched = new List<string>();
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var variant in _variants)
        {
            var matches = string.Equals(variant.Subculture, subculture, StringComparison.Ordinal)
                || (culture.Length > 0 && string.Equals(variant.Culture, culture, StringComparison.Ordinal));
            if (!matches || !_levelByKey.ContainsKey(variant.Building))
            {
                continue;
            }

            if (!names.TryGetValue(variant.Building, out var name))
            {
                matched.Add(variant.Building);
                names[variant.Building] = variant.Name;
            }
            else if (name is null && variant.Name is not null)
            {
                names[variant.Building] = variant.Name;
            }
        }

        return Sorted(matched.Select(key => new WizardChoice(key, names[key])));
    }

    public bool HasLevel(string levelKey)
    {
        ArgumentNullException.ThrowIfNull(levelKey);
        return _levelByKey.ContainsKey(levelKey);
    }

    /// <summary>A level of the level table names this chain (ordinal). Chains without levels are not known here; TakenKeys catches them.</summary>
    public bool HasChain(string chainKey)
    {
        ArgumentNullException.ThrowIfNull(chainKey);
        return _chains.Contains(chainKey);
    }

    /// <summary>The chain of the level; null when the level is unknown or its chain is empty.</summary>
    public string? ChainOf(string levelKey)
    {
        ArgumentNullException.ThrowIfNull(levelKey);
        return _levelByKey.TryGetValue(levelKey, out var level) && level.Chain.Length > 0 ? level.Chain : null;
    }

    /// <summary>The levels of the chain sorted by Level, then LevelKey (ordinal). Name: the loc text of the first variant of the level that has one.</summary>
    public IReadOnlyList<BuildingStage> StagesOf(string chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return _levels
            .Where(l => string.Equals(l.Chain, chain, StringComparison.Ordinal))
            .OrderBy(l => l.Number)
            .ThenBy(l => l.Key, StringComparer.Ordinal)
            .Select(l => new BuildingStage(l.Key, l.Number, _nameOfLevel.GetValueOrDefault(l.Key)))
            .ToList();
    }

    /// <summary>
    /// The subculture to preselect for a view: null unless view.Id.Table is the level table. The first row of the variant section with a known
    /// subculture gives it; else the first row with a culture that has a subculture gives the first subculture of that culture in Subcultures order; else null.
    /// </summary>
    public string? PreselectFor(ObjectView? view)
    {
        if (view is null || !string.Equals(view.Id.Table, _levelTable, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rows = view.Sections
            .Where(s => string.Equals(s.SourceTable, _variantTable, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.FieldName, _variantBuildingField, StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.Rows)
            .ToList();

        foreach (var row in rows)
        {
            var subculture = TextOf(row, _variantSubcultureField);
            if (subculture.Length > 0 && _cultureOf.ContainsKey(subculture))
            {
                return subculture;
            }
        }

        foreach (var row in rows)
        {
            var culture = TextOf(row, _variantCultureField);
            if (culture.Length == 0)
            {
                continue;
            }

            var first = Subcultures.FirstOrDefault(s => string.Equals(_cultureOf[s.Key], culture, StringComparison.Ordinal));
            if (first is not null)
            {
                return first.Key;
            }
        }

        return null;
    }

    internal static IReadOnlyList<WizardChoice> Sorted(IEnumerable<WizardChoice> choices) => choices
        .OrderBy(c => c.Name ?? c.Key, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.Key, StringComparer.Ordinal)
        .ToList();

    private static string TextOf(ObjectRow row, string field) =>
        row.Fields.FirstOrDefault(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase))?.Text ?? string.Empty;
}

/// <summary>The building wizard (spec 5.8): reads its lists from the tables and composes the copy of a template. The only game rule in code is the chain and stage logic of <see cref="Compose"/>.</summary>
public static class BuildingWizard
{
    private const string TableSuffix = "_tables";

    /// <summary>table(name) returns the merged table of the load order. Reads the level, variant and subculture tables.</summary>
    public static BuildingCatalog Catalog(BuildingWizardDefinition definition, Func<string, MergedTable> table, LocTable loc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(loc);

        var cultureOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var subcultures = new List<WizardChoice>();
        var subcultureTable = table(definition.SubcultureTable);
        var cultureField = subcultureTable.FieldIndex(definition.SubcultureCultureField);
        if (subcultureTable.Definition is not null && subcultureTable.KeyFields.Count > 0)
        {
            foreach (var row in subcultureTable.Rows)
            {
                var keyValues = subcultureTable.KeyFields.Select(i => Text(subcultureTable, row, i)).ToList();
                var key = keyValues[0];
                if (cultureOf.ContainsKey(key))
                {
                    continue;
                }

                cultureOf[key] = cultureField < 0 ? string.Empty : Text(subcultureTable, row, cultureField);
                subcultures.Add(new WizardChoice(key, ObjectBuilder.DisplayName(loc, new ObjectId(definition.SubcultureTable, keyValues))));
            }
        }

        var levels = new List<(string Key, string Chain, long Number)>();
        var levelTable = table(definition.LevelTable);
        var chainField = levelTable.FieldIndex(definition.ChainField);
        var levelField = levelTable.FieldIndex(definition.LevelField);
        if (levelTable.Definition is not null && levelTable.KeyFields.Count > 0)
        {
            foreach (var row in levelTable.Rows)
            {
                var number = 0L;
                if (levelField >= 0)
                {
                    long.TryParse(Text(levelTable, row, levelField), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                }

                levels.Add((Text(levelTable, row, levelTable.KeyFields[0]), chainField < 0 ? string.Empty : Text(levelTable, row, chainField), number));
            }
        }

        var variants = new List<(string Building, string Culture, string Subculture, string? Name)>();
        var variantTable = table(definition.VariantTable);
        var buildingField = variantTable.FieldIndex(definition.VariantBuildingField);
        var variantCultureField = variantTable.FieldIndex(definition.VariantCultureField);
        var variantSubcultureField = variantTable.FieldIndex(definition.VariantSubcultureField);
        if (variantTable.Definition is not null && variantTable.KeyFields.Count > 0 && buildingField >= 0)
        {
            var prefix = StripSuffix(definition.VariantTable) + "_" + definition.NameText.Column + "_";
            foreach (var row in variantTable.Rows)
            {
                var culture = variantCultureField < 0 ? string.Empty : Text(variantTable, row, variantCultureField);
                var subculture = variantSubcultureField < 0 ? string.Empty : Text(variantTable, row, variantSubcultureField);
                if (culture.Length == 0 && subculture.Length == 0)
                {
                    // A variant of a faction only does not make the level a template of any subculture.
                    continue;
                }

                var id = new ObjectId(definition.VariantTable, variantTable.KeyFields.Select(i => Text(variantTable, row, i)).ToList());
                variants.Add((Text(variantTable, row, buildingField), culture, subculture, loc.TryGet(prefix + id.JoinedKey, out var name) ? name.Text : null));
            }
        }

        return new BuildingCatalog(definition, BuildingCatalog.Sorted(subcultures), cultureOf, levels, variants);
    }

    /// <summary>The chain of a new level: the table of the chains, the key of the level plus the suffix of the definition.</summary>
    public static ObjectId ChainIdFor(BuildingWizardDefinition definition, string newLevelKey)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(newLevelKey);
        return new ObjectId(definition.ChainTable, [newLevelKey + definition.ChainKeySuffix]);
    }

    /// <summary>
    /// The copy of a building: levelCopy (ObjectCopy.Rename of the template), chainCopy (Rename of the template's chain to ChainIdFor; null for a
    /// stage in an existing chain or a template without chain), after (null: new chain; else the stage the new building follows). The upgrade rows
    /// of the template are dropped in both modes; the name texts get nameSuffix. Throws InvalidOperationException when the level lacks a field of the definition.
    /// </summary>
    public static CopyResult Compose(BuildingWizardDefinition definition, CopyResult levelCopy, CopyResult? chainCopy, BuildingStage? after, string nameSuffix)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(levelCopy);
        ArgumentNullException.ThrowIfNull(nameSuffix);

        var level = levelCopy.Objects[0];
        var sections = level.Sections
            .Where(s => !string.Equals(s.SourceTable, definition.UpgradeTable, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var objects = new List<ParsedObject>();
        var notes = new List<CopyNote>(levelCopy.Notes);

        if (after is not null)
        {
            level = level with { Root = SetField(level.Root, definition.LevelField, (after.Level + 1).ToString(CultureInfo.InvariantCulture)) };
            sections.Add(new ParsedSection(definition.UpgradeTable, definition.UpgradeToField,
                [new ParsedRow([new ParsedField(definition.UpgradeFromField, after.LevelKey, 0)], [], 0)], 0));
            objects.Add(level with { Sections = sections });
            objects.AddRange(levelCopy.Objects.Skip(1));
        }
        else if (chainCopy is not null)
        {
            var root = SetField(level.Root, definition.ChainField, chainCopy.Objects[0].Id.KeyValues[0]);
            level = level with { Root = SetField(root, definition.LevelField, "0") };
            objects.Add(level with { Sections = sections });
            objects.AddRange(levelCopy.Objects.Skip(1));

            // A stage whose key starts with the chain key plus "_" became a sub-object of the chain copy (ObjectCopy rule 3). A new chain
            // starts with the new level only, so no stage of the template chain is taken over.
            objects.AddRange(chainCopy.Objects.Where(o => !string.Equals(o.Id.Table, definition.LevelTable, StringComparison.OrdinalIgnoreCase)));

            // The stages of the template chain point at the template chain, not at the copy: expected, not a problem.
            foreach (var note in chainCopy.Notes)
            {
                if (note.Kind == CopyNoteKind.ForeignKeyRows && string.Equals(note.Table, definition.LevelTable, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddNote(notes, note);
            }
        }
        else
        {
            level = level with { Root = SetField(level.Root, definition.LevelField, "0") };
            objects.Add(level with { Sections = sections });
            objects.AddRange(levelCopy.Objects.Skip(1));
            notes.Add(new CopyNote(CopyNoteKind.NoChain, definition.ChainTable, 0));
        }

        return ObjectCopy.AppendToTexts(new CopyResult(objects, notes), definition.NameText.Table, definition.NameText.Column, nameSuffix);
    }

    private static List<ParsedField> SetField(IReadOnlyList<ParsedField> fields, string name, string text)
    {
        var index = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException("The level has no field " + name + ".");
        }

        var result = fields.ToList();
        result[index] = result[index] with { Text = text };
        return result;
    }

    private static void AddNote(List<CopyNote> notes, CopyNote note)
    {
        var index = notes.FindIndex(n => n.Kind == note.Kind && string.Equals(n.Table, note.Table, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            notes.Add(note);
        }
        else
        {
            notes[index] = notes[index] with { Count = notes[index].Count + note.Count };
        }
    }

    private static string Text(MergedTable table, MergedRow row, int field) => CellText.Format(table.Definition!.Fields[field].Type, row.Cells[field]);

    private static string StripSuffix(string table) =>
        table.EndsWith(TableSuffix, StringComparison.OrdinalIgnoreCase) ? table[..^TableSuffix.Length] : table;
}

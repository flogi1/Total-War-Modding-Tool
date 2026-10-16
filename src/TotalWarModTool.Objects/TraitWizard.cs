using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

/// <summary>The trait wizard (spec 5.8): lists the traits as templates and composes the copy of one. It has no game rule of its own: the levels of a trait
/// are copied by the copy rule of <see cref="ObjectCopy.Rename"/>.</summary>
public static class TraitWizard
{
    private const string TableSuffix = "_tables";

    /// <summary>
    /// Every row of the trait table (each key once). Name = loc text of the first key (ordinal) with the prefix
    /// "&lt;NameText.Table without _tables&gt;_&lt;NameText.Column&gt;_&lt;trait key&gt;_", null without one; sorted by Name ?? Key (OrdinalIgnoreCase, then Key ordinal).
    /// Empty when the table has no definition or no key field.
    /// </summary>
    public static IReadOnlyList<WizardChoice> Templates(TraitWizardDefinition definition, MergedTable traits, LocTable loc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(traits);
        ArgumentNullException.ThrowIfNull(loc);
        if (traits.Definition is null || traits.KeyFields.Count == 0)
        {
            return [];
        }

        var prefix = StripSuffix(definition.NameText.Table) + "_" + definition.NameText.Column + "_";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var choices = new List<WizardChoice>();
        foreach (var row in traits.Rows)
        {
            var key = CellText.Format(traits.Definition.Fields[traits.KeyFields[0]].Type, row.Cells[traits.KeyFields[0]]);
            if (!seen.Add(key))
            {
                continue;
            }

            var names = loc.KeysWithPrefix(prefix + key + "_");
            choices.Add(new WizardChoice(key, names.Count > 0 && loc.TryGet(names[0], out var name) ? name.Text : null));
        }

        return BuildingCatalog.Sorted(choices);
    }

    /// <summary>The copy of a trait (<see cref="ObjectCopy.Rename"/> of the template) with nameSuffix appended to the level names (the texts of the NameText column).</summary>
    public static CopyResult Compose(TraitWizardDefinition definition, CopyResult traitCopy, string nameSuffix)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(traitCopy);
        ArgumentNullException.ThrowIfNull(nameSuffix);
        return ObjectCopy.AppendToTexts(traitCopy, definition.NameText.Table, definition.NameText.Column, nameSuffix);
    }

    private static string StripSuffix(string table) =>
        table.EndsWith(TableSuffix, StringComparison.OrdinalIgnoreCase) ? table[..^TableSuffix.Length] : table;
}

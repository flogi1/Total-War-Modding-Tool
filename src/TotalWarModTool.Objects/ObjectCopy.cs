using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects;

public enum CopyNoteKind
{
    ForeignKeyRows,
    DepthExceeded,
    HiddenSection,
}

/// <summary>Table with suffix; Count the rows concerned.</summary>
public sealed record CopyNote(CopyNoteKind Kind, string Table, int Count);

/// <summary>
/// Objects in write order: the copied root first, then its sub-objects. Every ParsedObject has no diagnostics and holds the data of an
/// object that does not exist yet (its rows are all inserts against a view without root).
/// </summary>
public sealed record CopyResult(IReadOnlyList<ParsedObject> Objects, IReadOnlyList<CopyNote> Notes);

/// <summary>A new value for an integer key field of a copied row (table with suffix, field name). Returns the value as text.</summary>
public delegate string NewIntegerKey(string table, string field);

/// <summary>"Copy as": an object written under a new key (spec 5.8). A pure function apart from the <c>build</c> callback.</summary>
public static class ObjectCopy
{
    /// <summary>A sub-object is a row of the root's sections that has a key of its own; depth 1 is the root, depth 2 its sub-objects.</summary>
    private const int MaxDepth = 1;

    /// <summary>
    /// newId has the table of source.Id and the same number of key values. build gives the view of a sub-object (called for prefix rows
    /// only); categoryOf hides sections of the root and of sub-objects alike (looked up by the object's table). newIntegerKey gives the new value
    /// of an integer key field in the rows of a section marked copy owned. Pure apart from build and newIntegerKey.
    /// </summary>
    public static CopyResult Rename(ObjectView source, ObjectId newId, Func<ObjectId, ObjectView> build, Func<string, CategoryDefinition?> categoryOf,
        ReferenceGraph graph, NewIntegerKey newIntegerKey)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(newId);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(categoryOf);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(newIntegerKey);
        return Copy(source, newId, build, categoryOf, graph, newIntegerKey, 0);
    }

    private static CopyResult Copy(ObjectView source, ObjectId newId, Func<ObjectId, ObjectView> build, Func<string, CategoryDefinition?> categoryOf,
        ReferenceGraph graph, NewIntegerKey newIntegerKey, int depth)
    {
        if (source.Root is null)
        {
            throw new ArgumentException("The source has no root row to copy.", nameof(source));
        }

        if (!string.Equals(newId.Table, source.Id.Table, StringComparison.OrdinalIgnoreCase) || newId.KeyValues.Count != source.Id.KeyValues.Count)
        {
            throw new ArgumentException("The new id must name the table of the source and have as many key values.", nameof(newId));
        }

        var notes = new List<CopyNote>();
        var subObjects = new List<ParsedObject>();
        var category = categoryOf(source.Id.Table);
        var sections = new List<ParsedSection>();
        var oldKey = source.Id.KeyValues.Count == 1 ? source.Id.KeyValues[0] : null;

        foreach (var section in source.Sections)
        {
            if (section.Rows.Count == 0)
            {
                continue;
            }

            var rule = ObjectText.RuleFor(category, section.SourceTable, section.FieldName);
            if (rule is { Hidden: true })
            {
                AddNote(notes, CopyNoteKind.HiddenSection, section.SourceTable, section.Rows.Count);
                continue;
            }

            var rows = new List<ParsedRow>();
            foreach (var row in section.Rows)
            {
                switch (Classify(section, row, oldKey, graph, rule))
                {
                    case RowClass.CopyOwned:
                        rows.Add(CopyRow(source, newId, section, row, newIntegerKey));
                        break;
                    case RowClass.Owned:
                        rows.Add(CopyRow(source, newId, section, row, null));
                        break;
                    case RowClass.SubObject when depth < MaxDepth:
                        CopySubObject(source, newId, section, row, oldKey!, build, categoryOf, graph, newIntegerKey, depth, subObjects, notes);
                        break;
                    case RowClass.SubObject:
                        AddNote(notes, CopyNoteKind.DepthExceeded, section.SourceTable, 1);
                        break;
                    default:
                        AddNote(notes, CopyNoteKind.ForeignKeyRows, section.SourceTable, 1);
                        break;
                }
            }

            if (rows.Count > 0)
            {
                sections.Add(new ParsedSection(section.SourceTable, section.FieldName, rows, 0));
            }
        }

        var root = new ParsedObject(newId, RootFields(source, newId), RootTexts(source), sections, []);
        return new CopyResult([root, .. subObjects], notes);
    }

    private enum RowClass
    {
        CopyOwned,
        Owned,
        SubObject,
        ForeignKey,
    }

    /// <summary>Spec 5.8: a row of a section marked copy owned, a junction row, a row of a table without key of its own or one keyed by the pointing field belongs to the root; a key that starts with the root key and "_" is a sub-object; anything else only points at the root.</summary>
    private static RowClass Classify(ObjectSection section, ObjectRow row, string? oldKey, ReferenceGraph graph, SectionRule? rule)
    {
        if (rule is { CopyOwned: true })
        {
            return RowClass.CopyOwned;
        }

        var keys = row.Fields.Where(f => f.IsKey).ToList();
        if (section.IsJunction || graph.IsJunction(section.SourceTable) || keys.Count == 0
            || keys.Any(f => string.Equals(f.Name, section.FieldName, StringComparison.OrdinalIgnoreCase)))
        {
            return RowClass.Owned;
        }

        return oldKey is not null && keys.Count == 1 && row.RowId is not null && keys[0].Text.StartsWith(oldKey + "_", StringComparison.Ordinal)
            ? RowClass.SubObject
            : RowClass.ForeignKey;
    }

    private static void CopySubObject(ObjectView source, ObjectId newId, ObjectSection section, ObjectRow row, string oldKey,
        Func<ObjectId, ObjectView> build, Func<string, CategoryDefinition?> categoryOf, ReferenceGraph graph, NewIntegerKey newIntegerKey, int depth,
        List<ParsedObject> subObjects, List<CopyNote> notes)
    {
        var key = row.Fields.First(f => f.IsKey).Text;
        var view = build(row.RowId!);
        if (view.Root is null)
        {
            AddNote(notes, CopyNoteKind.ForeignKeyRows, section.SourceTable, 1);
            return;
        }

        var subId = new ObjectId(section.SourceTable, [newId.KeyValues[0] + key[oldKey.Length..]]);
        var sub = Copy(view, subId, build, categoryOf, graph, newIntegerKey, depth + 1);

        // The stage points at the copy of the root, not at the original.
        var head = sub.Objects[0];
        var root = head.Root
            .Select(f => string.Equals(f.Name, section.FieldName, StringComparison.OrdinalIgnoreCase) ? f with { Text = newId.KeyValues[0] } : f)
            .ToList();
        subObjects.Add(head with { Root = root });
        subObjects.AddRange(sub.Objects.Skip(1));
        foreach (var note in sub.Notes)
        {
            AddNote(notes, note.Kind, note.Table, note.Count);
        }
    }

    /// <summary>The fields of the root with the key fields set to the new key, position for position.</summary>
    private static List<ParsedField> RootFields(ObjectView source, ObjectId newId)
    {
        var fields = new List<ParsedField>();
        var position = 0;
        foreach (var field in source.Root!.Fields)
        {
            var text = field.Text;
            if (field.IsKey)
            {
                text = position < newId.KeyValues.Count ? newId.KeyValues[position] : text;
                position++;
            }

            fields.Add(new ParsedField(field.Name, text, 0));
        }

        return fields;
    }

    private static List<ParsedText> RootTexts(ObjectView source) =>
        source.Texts.Where(t => t.Owner.Equals(source.Id)).Select(t => new ParsedText(t.Column, t.Text, 0)).ToList();

    /// <summary>All fields but the one that points at the root: the key fields first, then the others, empty values included. A key field that holds the old root key gets the new one;
    /// with newIntegerKey (a row of a section marked copy owned) any other integer key field gets a new value.</summary>
    private static ParsedRow CopyRow(ObjectView source, ObjectId newId, ObjectSection section, ObjectRow row, NewIntegerKey? newIntegerKey)
    {
        var fields = row.Fields
            .Where(f => !string.Equals(f.Name, section.FieldName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.IsKey ? 0 : 1)
            .Select(f => new ParsedField(f.Name, f.IsKey ? KeyText(f, source.Id, newId, section, newIntegerKey) : f.Text, 0))
            .ToList();
        var texts = row.RowId is { } rowId
            ? source.Texts.Where(t => t.Owner.Equals(rowId)).Select(t => new ParsedText(t.Column, t.Text, 0)).ToList()
            : [];
        return new ParsedRow(fields, texts, 0);
    }

    private static string KeyText(FieldValue field, ObjectId oldId, ObjectId newId, ObjectSection section, NewIntegerKey? newIntegerKey)
    {
        for (var k = 0; k < oldId.KeyValues.Count; k++)
        {
            if (string.Equals(field.Text, oldId.KeyValues[k], StringComparison.Ordinal))
            {
                return newId.KeyValues[k];
            }
        }

        return newIntegerKey is not null && (field.Type is FieldType.I32 or FieldType.I64) ? newIntegerKey(section.SourceTable, field.Name) : field.Text;
    }

    private static void AddNote(List<CopyNote> notes, CopyNoteKind kind, string table, int count)
    {
        var index = notes.FindIndex(n => n.Kind == kind && string.Equals(n.Table, table, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            notes.Add(new CopyNote(kind, table, count));
        }
        else
        {
            notes[index] = notes[index] with { Count = notes[index].Count + count };
        }
    }
}

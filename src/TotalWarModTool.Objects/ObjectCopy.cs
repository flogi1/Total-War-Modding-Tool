using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects;

public enum CopyNoteKind
{
    ForeignKeyRows,
    DepthExceeded,
    HiddenSection,

    /// <summary>The building wizard found no chain to copy (a template without chain); Table is the table of the chains.</summary>
    NoChain,
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

/// <summary>A key an insert of a copy would write although the load order or another row of the same copy has it. Table with suffix; KeyValues in key field order.</summary>
public sealed record TakenKey(string Table, IReadOnlyList<string> KeyValues);

/// <summary>A key of Table (with suffix) that a copy renamed: a reference holding OldKey gets NewKey.</summary>
public sealed record KeyRename(string Table, string OldKey, string NewKey);

/// <summary>Keys of Table (with suffix) the copy does not bring along: a row that still references one of them is left out.</summary>
public sealed record NotCopiedKeys(string Table, IReadOnlyCollection<string> Keys);

/// <summary>"Copy as": an object written under a new key (spec 5.8). A pure function apart from the <c>build</c> callback.</summary>
public static class ObjectCopy
{
    /// <summary>A sub-object is a row of the root's sections that has a key of its own; depth 1 is the root, depth 2 its sub-objects.</summary>
    private const int MaxDepth = 1;

    private const char KeySeparator = '\u0001';

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

    /// <summary>
    /// Every object of the copy diffed against a view without root (new ObjectView(object.Id, null, [], [], [], order, [])) with categoryOf(object.Id.Table),
    /// concatenated in object order: Rows, Texts, Diagnostics. An insert whose table (ignoring case) and cells (ordinal) equal an earlier insert is
    /// left out: the same row reached twice (from two sections or from both copies of a wizard) is written once. Pure.
    /// </summary>
    public static ObjectChanges Changes(CopyResult copy, LoadOrder order, Func<string, CategoryDefinition?> categoryOf, ReferenceGraph graph,
        ReferenceExists? referenceExists)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(categoryOf);
        ArgumentNullException.ThrowIfNull(graph);
        var rows = new List<RowChange>();
        var texts = new List<TextChange>();
        var diagnostics = new List<BlockDiagnostic>();
        var inserted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parsed in copy.Objects)
        {
            var empty = new ObjectView(parsed.Id, null, [], [], [], order, []);
            var changes = ObjectDiff.Compute(empty, parsed, categoryOf(parsed.Id.Table), graph, referenceExists);
            rows.AddRange(changes.Rows.Where(r => r.Kind != RowChangeKind.Insert
                || inserted.Add(r.Table.ToLowerInvariant() + KeySeparator + string.Join(KeySeparator, r.Cells))));
            texts.AddRange(changes.Texts);
            diagnostics.AddRange(changes.Diagnostics);
        }

        return new ObjectChanges(rows, texts, diagnostics);
    }

    /// <summary>
    /// The inserts whose key (cells at graph.KeyFields(table)) keyExists(table, keyValues) reports, or that an earlier insert of the same table in
    /// changes already has. Tables without key fields are skipped. Each (table, key) once, in order of first appearance.
    /// </summary>
    public static IReadOnlyList<TakenKey> TakenKeys(ObjectChanges changes, ReferenceGraph graph, Func<string, IReadOnlyList<string>, bool> keyExists)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(keyExists);
        var taken = new List<TakenKey>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in changes.Rows)
        {
            if (row.Kind != RowChangeKind.Insert)
            {
                continue;
            }

            var keyFields = graph.KeyFields(row.Table);
            if (keyFields.Count == 0)
            {
                continue;
            }

            var values = keyFields.Select(i => row.Cells[i]).ToList();
            var identity = row.Table.ToLowerInvariant() + KeySeparator + string.Join(KeySeparator, values);
            var again = !seen.Add(identity);
            if ((again || keyExists(row.Table, values)) && reported.Add(identity))
            {
                taken.Add(new TakenKey(row.Table, values));
            }
        }

        return taken;
    }

    /// <summary>
    /// Spec 5.8 (new chain): in every row of the sections of the copy, a field whose schema reference (latest definition of the section table)
    /// resolves to the table of a rename and holds its OldKey gets NewKey, key field or not. A row with a field that then references
    /// notCopied.Table and holds one of its keys is left out and noted as ForeignKeyRows on its table; a section left without rows is dropped.
    /// Root fields stay. Tables compared ignoring case, values ordinal. Pure.
    /// </summary>
    public static CopyResult RetargetReferences(CopyResult copy, ReferenceGraph graph, IReadOnlyList<KeyRename> renames, NotCopiedKeys notCopied)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(renames);
        ArgumentNullException.ThrowIfNull(notCopied);
        var notCopiedKeys = notCopied.Keys.ToHashSet(StringComparer.Ordinal);
        var notes = copy.Notes.ToList();
        var objects = new List<ParsedObject>();
        foreach (var parsed in copy.Objects)
        {
            var sections = new List<ParsedSection>();
            foreach (var section in parsed.Sections)
            {
                var rows = new List<ParsedRow>();
                foreach (var row in section.Rows)
                {
                    var fields = new List<ParsedField>();
                    var pointsAtNotCopied = false;
                    foreach (var field in row.Fields)
                    {
                        var target = ReferencedTable(graph, section.SourceTable, field.Name);
                        var rename = target is null
                            ? null
                            : renames.FirstOrDefault(r => string.Equals(r.Table, target, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(r.OldKey, field.Text, StringComparison.Ordinal));
                        var text = rename?.NewKey ?? field.Text;
                        pointsAtNotCopied |= string.Equals(target, notCopied.Table, StringComparison.OrdinalIgnoreCase) && notCopiedKeys.Contains(text);
                        fields.Add(field with { Text = text });
                    }

                    if (pointsAtNotCopied)
                    {
                        AddNote(notes, CopyNoteKind.ForeignKeyRows, section.SourceTable, 1);
                        continue;
                    }

                    rows.Add(row with { Fields = fields });
                }

                if (rows.Count > 0)
                {
                    sections.Add(section with { Rows = rows });
                }
            }

            objects.Add(parsed with { Sections = sections });
        }

        return new CopyResult(objects, notes);
    }

    /// <summary>The copy with suffix appended to every text of the column: root texts of objects of the table and texts of rows of its sections.</summary>
    public static CopyResult AppendToTexts(CopyResult copy, string table, string column, string suffix)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(suffix);

        ParsedText Append(ParsedText text) =>
            string.Equals(text.Column, column, StringComparison.OrdinalIgnoreCase) ? text with { Text = text.Text + suffix } : text;

        var objects = copy.Objects.Select(o => o with
        {
            Texts = string.Equals(o.Id.Table, table, StringComparison.OrdinalIgnoreCase) ? o.Texts.Select(Append).ToList() : o.Texts,
            Sections = o.Sections.Select(s => string.Equals(s.SourceTable, table, StringComparison.OrdinalIgnoreCase)
                ? s with { Rows = s.Rows.Select(r => r with { Texts = r.Texts.Select(Append).ToList() }).ToList() }
                : s).ToList(),
        }).ToList();
        return copy with { Objects = objects };
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

    /// <summary>The table (with suffix) that field of the latest definition of table references; null without reference or for an unknown table or field.</summary>
    private static string? ReferencedTable(ReferenceGraph graph, string table, string field)
    {
        var reference = graph.Current(table)?.Fields.FirstOrDefault(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase))?.Reference;
        return reference is null ? null : graph.ResolveTableName(reference.Table);
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

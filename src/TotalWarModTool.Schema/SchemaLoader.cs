using System.Globalization;
using System.Text;

namespace TotalWarModTool.Schema;

public enum SchemaProblem { None, NotFound, Unreadable, InvalidSyntax, InvalidStructure }

/// <summary><paramref name="Detail"/> is short technical English, not a UI text. It is null when <paramref name="Problem"/> is <see cref="SchemaProblem.None"/>.</summary>
public sealed record SchemaLoadResult(GameSchema? Schema, SchemaProblem Problem, string? Detail);

/// <summary>Reads an RPFM schema file (RON). Never throws for a missing, unreadable or broken file; the result carries a problem instead.</summary>
public static class SchemaLoader
{
    // The real Warhammer III schema is about 11 MB. A limit keeps a wrong or hostile file from filling the memory.
    private const long MaxFileBytes = 256L * 1024 * 1024;

    public static SchemaLoadResult Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxFileBytes)
            {
                return Failure(SchemaProblem.Unreadable, "File is larger than " + MaxFileBytes.ToString(CultureInfo.InvariantCulture) + " bytes: " + path);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return Failure(SchemaProblem.NotFound, "File not found: " + path);
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(SchemaProblem.NotFound, "Folder not found: " + path);
        }
        catch (IOException ex)
        {
            return Failure(SchemaProblem.Unreadable, ex.Message + " (" + path + ")");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(SchemaProblem.Unreadable, ex.Message + " (" + path + ")");
        }
        catch (NotSupportedException ex)
        {
            return Failure(SchemaProblem.Unreadable, ex.Message + " (" + path + ")");
        }

        if (!RonParser.TryParse(text, out var root, out var error))
        {
            return Failure(SchemaProblem.InvalidSyntax,
                "Line " + error!.Line.ToString(CultureInfo.InvariantCulture) + ", column " + error.Column.ToString(CultureInfo.InvariantCulture) + ": " + error.Message);
        }

        try
        {
            return new SchemaLoadResult(Build(root!), SchemaProblem.None, null);
        }
        catch (StructureException ex)
        {
            return Failure(SchemaProblem.InvalidStructure, ex.Message);
        }
    }

    private static SchemaLoadResult Failure(SchemaProblem problem, string detail) => new(null, problem, detail);

    private sealed class StructureException(string message) : Exception(message);

    private static GameSchema Build(RonValue root)
    {
        if (root is not RonStruct rootStruct)
        {
            throw new StructureException("The root value is not a struct");
        }

        if (!rootStruct.Fields.TryGetValue("definitions", out var definitionsValue) || definitionsValue is not RonMap definitionsMap)
        {
            throw new StructureException("The root struct has no 'definitions' map");
        }

        var tables = new Dictionary<string, List<TableDefinition>>(definitionsMap.Entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in definitionsMap.Entries)
        {
            if (key is not RonString tableName)
            {
                throw new StructureException("'definitions' contains a table name that is not a text");
            }

            if (value is not RonList definitionList)
            {
                throw new StructureException("Table '" + tableName.Value + "': the definitions are not a list");
            }

            if (!tables.TryGetValue(tableName.Value, out var versions))
            {
                versions = new List<TableDefinition>(definitionList.Items.Count);
                tables[tableName.Value] = versions;
            }

            foreach (var item in definitionList.Items)
            {
                versions.Add(BuildDefinition(tableName.Value, item));
            }
        }

        var result = new Dictionary<string, IReadOnlyList<TableDefinition>>(tables.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, versions) in tables)
        {
            result[name] = versions;
        }

        return new GameSchema(result);
    }

    private static TableDefinition BuildDefinition(string table, RonValue item)
    {
        if (item is not RonStruct definition)
        {
            throw new StructureException("Table '" + table + "': a definition is not a struct");
        }

        if (!definition.Fields.TryGetValue("version", out var versionValue)
            || versionValue is not RonNumber versionNumber
            || !int.TryParse(versionNumber.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            throw new StructureException("Table '" + table + "': a definition has no integer 'version'");
        }

        if (!definition.Fields.TryGetValue("fields", out var fieldsValue) || fieldsValue is not RonList fieldList)
        {
            throw new StructureException("Table '" + table + "' version " + version.ToString(CultureInfo.InvariantCulture) + ": 'fields' is missing or not a list");
        }

        var fields = new List<FieldDefinition>(fieldList.Items.Count);
        for (var i = 0; i < fieldList.Items.Count; i++)
        {
            fields.Add(BuildField(table, version, i, fieldList.Items[i]));
        }

        return new TableDefinition(version, fields);
    }

    private static FieldDefinition BuildField(string table, int version, int index, RonValue item)
    {
        var where = "Table '" + table + "' version " + version.ToString(CultureInfo.InvariantCulture) + " field " + index.ToString(CultureInfo.InvariantCulture) + ": ";
        if (item is not RonStruct field)
        {
            throw new StructureException(where + "not a struct");
        }

        if (!field.Fields.TryGetValue("name", out var nameValue) || nameValue is not RonString name)
        {
            throw new StructureException(where + "'name' is missing or not a text");
        }

        if (!field.Fields.TryGetValue("field_type", out var typeValue) || typeValue is not RonIdent type)
        {
            throw new StructureException(where + "'field_type' is missing or not a name");
        }

        var isKey = false;
        if (field.Fields.TryGetValue("is_key", out var keyValue))
        {
            isKey = keyValue is RonBool keyBool ? keyBool.Value : throw new StructureException(where + "'is_key' is not true or false");
        }

        TableReference? reference = null;
        if (field.Fields.TryGetValue("is_reference", out var referenceValue))
        {
            reference = ReadReference(referenceValue) ?? (referenceValue is RonIdent { Name: "None", Payload: null }
                ? null
                : throw new StructureException(where + "'is_reference' is neither None nor Some((table, column))"));
        }

        return new FieldDefinition(name.Value, MapType(type), type.Name, isKey, reference);
    }

    /// <summary>The <c>Some(("table", "column"))</c> form; null for anything else.</summary>
    private static TableReference? ReadReference(RonValue value) =>
        value is RonIdent { Name: "Some", Payload: RonTuple { Items: [RonTuple { Items: [RonString table, RonString column] }] } }
            ? new TableReference(table.Value, column.Value)
            : null;

    private static FieldType MapType(RonIdent type) => type.Payload is not null
        ? FieldType.Unsupported
        : type.Name switch
        {
            "Boolean" => FieldType.Boolean,
            "F32" => FieldType.F32,
            "F64" => FieldType.F64,
            "I16" => FieldType.I16,
            "I32" => FieldType.I32,
            "I64" => FieldType.I64,
            "ColourRGB" => FieldType.ColourRgb,
            "StringU8" => FieldType.StringU8,
            "StringU16" => FieldType.StringU16,
            "OptionalStringU8" => FieldType.OptionalStringU8,
            _ => FieldType.Unsupported,
        };
}

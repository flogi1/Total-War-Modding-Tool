using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables;

/// <summary>
/// Writes and reads DB tables and Loc files as TSV text in the dialect of RPFM: line 1 holds the column names, line 2 the meta line
/// (<c>#table;version;path</c>, padded with tabs to the column count), then one line per row. A tab in a text is written as two
/// backslashes and a t, a line break as two backslashes and an n. Three numeric fields that form a colour (r, g, b) are one column of
/// six hex digits. The model is the decoder's: <see cref="DecodedTable.Rows"/>, <see cref="LocRow"/>.
/// <c>Write*</c> returns no text for a table it cannot write without loss; <c>Read*</c> returns a diagnosis, never partial rows.
/// Only argument errors and model errors (a cell of the wrong type) throw.
/// </summary>
public static class TableTsv
{
    private const char Tab = '\t';
    private const int GroupColourDigits = 6;
    private const int MaxDetailValueLength = 50;
    private const string LocName = "Loc";
    private const string LocKey = "key";
    private const string LocText = "text";
    private const string LocTooltip = "tooltip";

    // One TSV column: a single field, or (Group not null) the three channel fields of a colour group. Index is the field index.
    internal sealed record Column(string Name, FieldType Type, int Index, ColourGroup? Group);

    internal sealed record ColourGroup(string ColumnName, int Red, int Green, int Blue);

    private sealed record Meta(string Name, int Version, string PackPath);

    // The header and the cells of the data lines, already checked for column names and cell counts.
    private sealed record Frame(IReadOnlyList<Column> HeaderColumns, IReadOnlyList<string[]> Rows);

    public static TsvWriteResult Write(string tableName, int version, string packPath, TableDefinition definition, IReadOnlyList<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(rows);
        if (tableName.AsSpan().IndexOfAny("\t\n\r;") >= 0)
        {
            throw new ArgumentException("The table name must not contain a tab, a line break or ';'", nameof(tableName));
        }

        RequireWellFormed(tableName, nameof(tableName));
        RequireSingleLineCells(packPath, nameof(packPath));
        RequireWellFormed(packPath, nameof(packPath));

        foreach (var field in definition.Fields)
        {
            if (field.Type == FieldType.Unsupported)
            {
                return Refused($"field '{Shorten(field.Name)}' has the unsupported type '{Shorten(field.RawType)}'");
            }
        }

        foreach (var field in definition.Fields)
        {
            // The reader splits the header line at tabs and line breaks, so such a name would not come back.
            if (field.Name.AsSpan().IndexOfAny(Tab, '\n', '\r') >= 0)
            {
                return Refused($"the field name '{Shorten(field.Name)}' contains a tab or a line break");
            }
        }

        var columns = ColumnsOf(definition);
        if (columns.Count == 0)
        {
            return Refused("the table has no columns");
        }

        if (FirstDuplicateName(columns) is { } duplicate)
        {
            return Refused($"the column name '{Shorten(duplicate)}' occurs twice");
        }

        var sb = new StringBuilder();
        AppendHeader(sb, columns.Select(c => c.Name).ToList(), tableName, version, packPath);
        var fields = definition.Fields;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row is null || row.Length != fields.Count)
            {
                throw new ArgumentException(
                    $"Row {r.ToString(CultureInfo.InvariantCulture)} has {row?.Length.ToString(CultureInfo.InvariantCulture) ?? "no"} cells, " +
                    $"the definition has {fields.Count.ToString(CultureInfo.InvariantCulture)} fields", nameof(rows));
            }

            for (var c = 0; c < columns.Count; c++)
            {
                if (c > 0)
                {
                    sb.Append(Tab);
                }

                var column = columns[c];
                string? refusal;
                if (column.Group is { } group)
                {
                    refusal = TryAppendGroup(sb, r, fields, group, row, out var channelField);
                    if (refusal is not null)
                    {
                        return Refused($"row {r.ToString(CultureInfo.InvariantCulture)}, field '{Shorten(fields[channelField].Name)}': {refusal}");
                    }
                }
                else if (!TryAppendCell(sb, r, fields[column.Index], row[column.Index], out refusal))
                {
                    return Refused($"row {r.ToString(CultureInfo.InvariantCulture)}, field '{Shorten(fields[column.Index].Name)}': {refusal}");
                }
            }

            sb.Append('\n');
        }

        return new TsvWriteResult(sb.ToString(), null);
    }

    public static TsvTableResult Read(string text, GameSchema schema)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(schema);

        var lines = SplitLines(text);
        if (!TryReadMeta(lines, out var meta, out var problem))
        {
            return new TsvTableResult(null, problem);
        }

        if (!schema.Tables.ContainsKey(meta.Name))
        {
            return Failed(TsvProblem.TableNotInSchema, 2, "Table is not in the schema: " + Shorten(meta.Name));
        }

        var definition = schema.Find(meta.Name, meta.Version);
        if (definition is null)
        {
            return Failed(TsvProblem.VersionNotInSchema, 2,
                "Version " + meta.Version.ToString(CultureInfo.InvariantCulture) + " of the table is not in the schema: " + Shorten(meta.Name));
        }

        foreach (var field in definition.Fields)
        {
            if (field.Type == FieldType.Unsupported)
            {
                return Failed(TsvProblem.UnsupportedFieldType, 2, $"Field '{Shorten(field.Name)}' has the unsupported type '{Shorten(field.RawType)}'");
            }
        }

        var columns = ColumnsOf(definition);
        if (FirstDuplicateName(columns) is { } duplicate)
        {
            return Failed(TsvProblem.DuplicateColumn, 1, $"The definition of the table has the column name '{Shorten(duplicate)}' twice");
        }

        if (!TryReadFrame(lines, columns, out var frame, out problem))
        {
            return new TsvTableResult(null, problem);
        }

        var fields = definition.Fields;
        var rows = new List<object?[]>(frame.Rows.Count);
        for (var i = 0; i < frame.Rows.Count; i++)
        {
            var cells = frame.Rows[i];
            var row = new object?[fields.Count];
            for (var j = 0; j < cells.Length; j++)
            {
                var column = frame.HeaderColumns[j];
                string? reason = null;
                if (column.Group is { } group)
                {
                    if (TryParseGroup(cells[j], out var rgb))
                    {
                        row[group.Red] = ChannelValue(fields[group.Red].Type, (int)(rgb >> 16) & 0xFF);
                        row[group.Green] = ChannelValue(fields[group.Green].Type, (int)(rgb >> 8) & 0xFF);
                        row[group.Blue] = ChannelValue(fields[group.Blue].Type, (int)rgb & 0xFF);
                    }
                    else
                    {
                        reason = $"a colour group needs exactly {GroupColourDigits.ToString(CultureInfo.InvariantCulture)} hex digits";
                    }
                }
                else if (!CellText.TryParseTsvCell(column.Type, cells[j], out row[column.Index], out var cellReason))
                {
                    reason = cellReason;
                }

                if (reason is not null)
                {
                    return Failed(TsvProblem.InvalidValue, i + 3, InvalidValueDetail(column.Name, cells[j], reason));
                }
            }

            rows.Add(row);
        }

        return new TsvTableResult(new TsvTable(meta.Name, meta.Version, meta.PackPath, definition, rows), null);
    }

    public static TsvWriteResult WriteLoc(string packPath, IReadOnlyList<LocRow> rows)
    {
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(rows);
        RequireSingleLineCells(packPath, nameof(packPath));
        RequireWellFormed(packPath, nameof(packPath));

        var sb = new StringBuilder();
        AppendHeader(sb, [LocKey, LocText, LocTooltip], LocName, LocDecoder.SupportedVersion, packPath);
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r] ?? throw new ArgumentException($"Row {r.ToString(CultureInfo.InvariantCulture)} is null", nameof(rows));
            if (!TryAppendText(sb, row.Key, utf8: false, out var refusal))
            {
                return Refused($"row {r.ToString(CultureInfo.InvariantCulture)}, key: {refusal}");
            }

            sb.Append(Tab);
            if (!TryAppendText(sb, row.Text, utf8: false, out refusal))
            {
                return Refused($"row {r.ToString(CultureInfo.InvariantCulture)}, text: {refusal}");
            }

            sb.Append(Tab).Append(row.Tooltip ? "true" : "false").Append('\n');
        }

        return new TsvWriteResult(sb.ToString(), null);
    }

    public static TsvLocResult ReadLoc(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = SplitLines(text);
        if (!TryReadMeta(lines, out var meta, out var problem))
        {
            return new TsvLocResult(null, problem);
        }

        if (meta.Name != LocName || meta.Version != LocDecoder.SupportedVersion)
        {
            return FailedLoc(TsvProblem.InvalidMetaLine, 2,
                $"The meta line must be #{LocName};{LocDecoder.SupportedVersion.ToString(CultureInfo.InvariantCulture)};<path>");
        }

        // Index 0 key, 1 text, 2 tooltip.
        Column[] columns =
        [
            new(LocKey, FieldType.StringU16, 0, null),
            new(LocText, FieldType.StringU16, 1, null),
            new(LocTooltip, FieldType.Boolean, 2, null),
        ];
        if (!TryReadFrame(lines, columns, out var frame, out problem))
        {
            return new TsvLocResult(null, problem);
        }

        var rows = new List<LocRow>(frame.Rows.Count);
        var values = new object?[columns.Length];
        for (var i = 0; i < frame.Rows.Count; i++)
        {
            var cells = frame.Rows[i];
            for (var j = 0; j < cells.Length; j++)
            {
                var column = frame.HeaderColumns[j];
                if (!CellText.TryParseTsvCell(column.Type, cells[j], out values[column.Index], out var reason))
                {
                    return FailedLoc(TsvProblem.InvalidValue, i + 3, InvalidValueDetail(column.Name, cells[j], reason!));
                }
            }

            rows.Add(new LocRow((string)values[0]!, (string)values[1]!, (bool)values[2]!));
        }

        return new TsvLocResult(new TsvLoc(meta.PackPath, rows), null);
    }

    private static TsvWriteResult Refused(string detail) => new(null, detail);

    private static TsvTableResult Failed(TsvProblem problem, int line, string detail) => new(null, new TsvDiagnosis(problem, line, detail));

    private static TsvLocResult FailedLoc(TsvProblem problem, int line, string detail) => new(null, new TsvDiagnosis(problem, line, detail));

    private static void RequireSingleLineCells(string value, string parameter)
    {
        if (value.AsSpan().IndexOfAny(Tab, '\n', '\r') >= 0)
        {
            throw new ArgumentException("The value must not contain a tab or a line break", parameter);
        }
    }

    // A text with an unpaired surrogate would turn into U+FFFD when the TSV text is saved as UTF-8.
    private static void RequireWellFormed(string value, string parameter)
    {
        if (!ByteWriter.IsWellFormed(value, out var reason))
        {
            throw new ArgumentException("The value must be well-formed text: " + reason, parameter);
        }
    }

    // ----- Columns -----

    // First the fields without a group in schema order, then the colour groups in the order they were discovered. Internal because
    // CellText.ColourChannelFields must name the very groups Write writes.
    internal static List<Column> ColumnsOf(TableDefinition definition)
    {
        var fields = definition.Fields;
        var firstByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            firstByName.TryAdd(fields[i].Name, i);
        }

        // Tried in this order for every field; a local, so that no caller can change the naming.
        (string Red, string Green, string Blue)[] channelNames = [("r", "g", "b"), ("red", "green", "blue")];
        var grouped = new bool[fields.Count];
        var groups = new List<ColourGroup>();
        var unnamed = 0;
        for (var i = 0; i < fields.Count; i++)
        {
            if (grouped[i] || !CellText.IsNumeric(fields[i].Type))
            {
                continue;
            }

            foreach (var (red, green, blue) in channelNames)
            {
                var name = fields[i].Name;
                string prefix;
                if (name == red)
                {
                    prefix = string.Empty;
                }
                else if (name.EndsWith("_" + red, StringComparison.Ordinal))
                {
                    prefix = name[..^red.Length];
                }
                else
                {
                    continue;
                }

                if (!firstByName.TryGetValue(prefix + green, out var g) || !firstByName.TryGetValue(prefix + blue, out var b)
                    || !CellText.IsNumeric(fields[g].Type) || !CellText.IsNumeric(fields[b].Type) || grouped[g] || grouped[b])
                {
                    continue;
                }

                grouped[i] = grouped[g] = grouped[b] = true;
                var columnName = prefix.Length == 0
                    ? "unnamed colour group_" + (++unnamed).ToString(CultureInfo.InvariantCulture)
                    : prefix + "hex";
                groups.Add(new ColourGroup(columnName, i, g, b));
                break;
            }
        }

        var columns = new List<Column>();
        for (var i = 0; i < fields.Count; i++)
        {
            if (!grouped[i])
            {
                columns.Add(new Column(fields[i].Name, fields[i].Type, i, null));
            }
        }

        foreach (var group in groups)
        {
            columns.Add(new Column(group.ColumnName, FieldType.ColourRgb, group.Red, group));
        }

        return columns;
    }

    private static string? FirstDuplicateName(IReadOnlyList<Column> columns)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (!seen.Add(column.Name))
            {
                return column.Name;
            }
        }

        return null;
    }

    // ----- Writing -----

    private static void AppendHeader(StringBuilder sb, IReadOnlyList<string> columnNames, string name, int version, string packPath)
    {
        sb.AppendJoin(Tab, columnNames).Append('\n');
        sb.Append('#').Append(name).Append(';').Append(version.ToString(CultureInfo.InvariantCulture)).Append(';').Append(packPath);
        sb.Append(Tab, columnNames.Count - 1).Append('\n');
    }

    private static string ModelError(int row, FieldDefinition field, object? value) =>
        $"Row {row.ToString(CultureInfo.InvariantCulture)}, column '{field.Name}' ({field.RawType}): " +
        (value is null ? "the cell is null" : "the cell is a " + value.GetType().Name + ", which does not fit the field type " + field.Type);

    // Returns false with a refusal reason when the cell cannot be written without loss. A cell of the wrong type is a model error.
    private static bool TryAppendCell(StringBuilder sb, int row, FieldDefinition field, object? value, out string? refusal)
    {
        refusal = null;
        // A NaN with a payload would come back as the standard NaN.
        if ((field.Type == FieldType.F32 && value is float f && float.IsNaN(f) && BitConverter.SingleToInt32Bits(f) != BitConverter.SingleToInt32Bits(float.NaN))
            || (field.Type == FieldType.F64 && value is double d && double.IsNaN(d) && BitConverter.DoubleToInt64Bits(d) != BitConverter.DoubleToInt64Bits(double.NaN)))
        {
            refusal = "a NaN with a payload cannot be written as text";
            return false;
        }

        switch (CellText.TryFormat(field.Type, value, out var text))
        {
            case CellText.FormatResult.WrongType:
                throw new ArgumentException(ModelError(row, field, value), "rows");
            case CellText.FormatResult.NotAColour:
                refusal = $"'{Shorten((string)value!)}' is {CellText.NotAColourReason()}";
                return false;
        }

        if (CellText.IsText(field.Type))
        {
            return TryAppendText(sb, text, CellText.IsUtf8(field.Type), out refusal);
        }

        sb.Append(text);
        return true;
    }

    // Appends the six hex digits of the group. On refusal the returned reason is set and channelField is the field of the bad channel.
    private static string? TryAppendGroup(StringBuilder sb, int row, IReadOnlyList<FieldDefinition> fields, ColourGroup group, object?[] cells, out int channelField)
    {
        Span<int> indexes = [group.Red, group.Green, group.Blue];
        Span<int> channels = stackalloc int[3];
        for (var k = 0; k < 3; k++)
        {
            channelField = indexes[k];
            var field = fields[channelField];
            double d = field.Type switch
            {
                FieldType.I16 when cells[channelField] is short v => v,
                FieldType.I32 when cells[channelField] is int v => v,
                FieldType.I64 when cells[channelField] is long v => v,
                FieldType.F32 when cells[channelField] is float v => v,
                FieldType.F64 when cells[channelField] is double v => v,
                _ => throw new ArgumentException(ModelError(row, field, cells[channelField]), "rows"),
            };

            // -0 is excluded: it would come back as +0.
            if (!(d >= 0 && d <= 255 && d == Math.Floor(d)) || (d == 0 && double.IsNegative(d)))
            {
                return $"the colour channel value {d.ToString("R", CultureInfo.InvariantCulture)} is not an integer from 0 to 255";
            }

            channels[k] = (int)d;
        }

        foreach (var channel in channels)
        {
            sb.Append(channel.ToString("X2", CultureInfo.InvariantCulture));
        }

        channelField = -1;
        return null;
    }

    private static bool TryAppendText(StringBuilder sb, string value, bool utf8, out string? refusal)
    {
        refusal = CellText.TextRefusal(value, utf8);
        if (refusal is not null)
        {
            return false;
        }

        sb.Append(CellText.Escape(value));
        return true;
    }

    // ----- Reading -----

    // A BOM at the start is skipped. Lines end with \n, a \r directly before it belongs to the line end, and one missing or present
    // line end at the very end does not add a line.
    private static List<string> SplitLines(string text)
    {
        var start = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        var lines = new List<string>();
        while (true)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
            {
                if (start < text.Length)
                {
                    lines.Add(text[start..]);
                }

                return lines;
            }

            var stop = end > start && text[end - 1] == '\r' ? end - 1 : end;
            lines.Add(text[start..stop]);
            start = end + 1;
        }
    }

    private static bool TryReadMeta(List<string> lines, [NotNullWhen(true)] out Meta? meta, [NotNullWhen(false)] out TsvDiagnosis? problem)
    {
        meta = null;
        if (lines.Count < 2 || !lines[1].StartsWith('#'))
        {
            problem = new TsvDiagnosis(TsvProblem.MissingMetaLine, 2, "Line 2 must be the meta line, starting with '#'");
            return false;
        }

        var line = lines[1];
        var tab = line.IndexOf(Tab);
        var cell = tab < 0 ? line[1..] : line[1..tab];
        if (cell.Contains('\r'))
        {
            // Write cannot write a carriage return into the meta cell, so Read does not accept one either.
            problem = new TsvDiagnosis(TsvProblem.InvalidMetaLine, 2, "The meta line must not contain a carriage return");
            return false;
        }

        if (!ByteWriter.IsWellFormed(cell, out var surrogateReason))
        {
            // Write throws for such a table name or pack path, so Read does not accept one either.
            problem = new TsvDiagnosis(TsvProblem.InvalidMetaLine, 2, "The meta line must be well-formed text: " + surrogateReason);
            return false;
        }

        var first = cell.IndexOf(';');
        var second = first < 0 ? -1 : cell.IndexOf(';', first + 1);
        if (second < 0 || !int.TryParse(cell.AsSpan(first + 1, second - first - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var version))
        {
            problem = new TsvDiagnosis(TsvProblem.InvalidMetaLine, 2, "The meta line must be #<table>;<version>;<path>");
            return false;
        }

        meta = new Meta(cell[..first], version, cell[(second + 1)..]);
        problem = null;
        return true;
    }

    // Checks, in this order: duplicate, unknown and missing column names (line 1), then the cell count of every data line.
    private static bool TryReadFrame(List<string> lines, IReadOnlyList<Column> expected, [NotNullWhen(true)] out Frame? frame, [NotNullWhen(false)] out TsvDiagnosis? problem)
    {
        frame = null;
        var names = lines[0].Split(Tab);
        var byName = new Dictionary<string, Column>(StringComparer.Ordinal);
        foreach (var column in expected)
        {
            byName[column.Name] = column;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!seen.Add(name))
            {
                problem = new TsvDiagnosis(TsvProblem.DuplicateColumn, 1, $"Column '{Shorten(name)}' occurs twice");
                return false;
            }
        }

        foreach (var name in names)
        {
            if (!byName.ContainsKey(name))
            {
                problem = new TsvDiagnosis(TsvProblem.UnknownColumn, 1, $"Column '{Shorten(name)}' is not a column of the table");
                return false;
            }
        }

        foreach (var column in expected)
        {
            if (!seen.Contains(column.Name))
            {
                problem = new TsvDiagnosis(TsvProblem.MissingColumn, 1, $"Column '{Shorten(column.Name)}' is missing");
                return false;
            }
        }

        var rows = new List<string[]>(lines.Count - 2);
        for (var i = 2; i < lines.Count; i++)
        {
            var cells = lines[i].Split(Tab);
            if (cells.Length != names.Length)
            {
                problem = new TsvDiagnosis(TsvProblem.WrongCellCount, i + 1,
                    $"The line has {cells.Length.ToString(CultureInfo.InvariantCulture)} cells, the header has {names.Length.ToString(CultureInfo.InvariantCulture)}");
                return false;
            }

            rows.Add(cells);
        }

        frame = new Frame(names.Select(n => byName[n]).ToList(), rows);
        problem = null;
        return true;
    }

    private static string InvalidValueDetail(string column, string cell, string reason) =>
        $"Column '{Shorten(column)}', value '{Shorten(cell)}': {reason}";

    // A cell value or model text for a Detail: at most MaxDetailValueLength characters, never cut inside a surrogate pair, control
    // characters and unpaired surrogates written as \uXXXX, "..." when it was cut.
    private static string Shorten(string value)
    {
        var length = Math.Min(value.Length, MaxDetailValueLength);
        if (length < value.Length && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        var sb = new StringBuilder(length + 3);
        for (var i = 0; i < length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(c).Append(value[++i]);
            }
            else if (char.IsControl(c) || char.IsSurrogate(c))
            {
                sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return length < value.Length ? sb.Append("...").ToString() : sb.ToString();
    }

    private static bool TryParseGroup(string cell, out uint rgb)
    {
        return ColourText.TryParse(cell, GroupColourDigits, GroupColourDigits, out rgb);
    }

    // The arms are boxed one by one: without the casts the switch would have the common type double and box every channel as one.
    private static object ChannelValue(FieldType type, int channel) => type switch
    {
        FieldType.I16 => (object)(short)channel,
        FieldType.I32 => (object)channel,
        FieldType.I64 => (object)(long)channel,
        FieldType.F32 => (object)(float)channel,
        _ => (object)(double)channel,
    };
}

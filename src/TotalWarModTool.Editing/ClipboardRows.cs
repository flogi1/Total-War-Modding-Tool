using System.Globalization;
using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Editing;

/// <summary>
/// Why a pasted text is not accepted. None: the rows are ready. Empty: no text, or a header without rows. NoKnownColumn: no header name is a
/// column of the target. DuplicateColumn: a column of the target is named twice in the header. WrongCellCount: a line has another number
/// of cells than the header. InvalidValue: a cell is not accepted by the target (<see cref="RowsDocument.Check"/>).
/// </summary>
public enum PasteProblem { None, Empty, NoKnownColumn, DuplicateColumn, WrongCellCount, InvalidValue }

/// <summary>
/// The outcome of <see cref="ClipboardRows.Read"/>. With Problem None: Rows are ready for <see cref="RowsDocument.InsertRows"/> (fresh arrays),
/// MissingColumns got their default and ExtraColumns were dropped. Otherwise Rows, MissingColumns and ExtraColumns are empty; Line is 1-based
/// (the header is line 1; 0 when the problem belongs to no line) and Detail is short technical English naming column and reason.
/// </summary>
public sealed record PasteResult(IReadOnlyList<object?[]> Rows, IReadOnlyList<string> MissingColumns, IReadOnlyList<string> ExtraColumns,
    PasteProblem Problem, int Line, string? Detail);

/// <summary>
/// Rows as text for the clipboard and back: a header line with the column names, then one line per row, the cells tab-separated and
/// escaped like the cells of the TSV dialect (<see cref="CellText.Escape"/>). This is the format of the app, not the RPFM dialect: no meta
/// line, and every column of the schema on its own (no colour group as one hex column). Quoted cells, as spreadsheet programs write
/// them, are not interpreted: the quotes would be part of the value.
/// </summary>
public static class ClipboardRows
{
    private const char Separator = '\t';
    private const int MaxDetailValueLength = 50;

    /// <summary>
    /// The header line with the column names, then one line per row. A cell is <see cref="CellText.Format"/> escaped with
    /// <see cref="CellText.Escape"/>; every line ends with a line feed. A row is an array of one cell per column, in the order of
    /// <paramref name="columns"/>. ArgumentException for a row of another length or a value the column type cannot format.
    /// </summary>
    public static string Write(IReadOnlyList<FieldDefinition> columns, IEnumerable<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        for (var c = 0; c < columns.Count; c++)
        {
            if (c > 0)
            {
                builder.Append(Separator);
            }

            builder.Append(columns[c].Name);
        }

        builder.Append('\n');
        var index = 0;
        foreach (var row in rows)
        {
            if (row is null || row.Length != columns.Count)
            {
                throw new ArgumentException(
                    $"Row {index.ToString(CultureInfo.InvariantCulture)} has {row?.Length.ToString(CultureInfo.InvariantCulture) ?? "no"} cells, " +
                    $"there are {columns.Count.ToString(CultureInfo.InvariantCulture)} columns", nameof(rows));
            }

            for (var c = 0; c < columns.Count; c++)
            {
                if (c > 0)
                {
                    builder.Append(Separator);
                }

                builder.Append(CellText.Escape(CellText.Format(columns[c].Type, row[c])));
            }

            builder.Append('\n');
            index++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads pasted text into rows for <paramref name="target"/>. Lines end with a line feed or a carriage return and a line feed; one empty
    /// last line is no row, an empty line in the middle is a row with one empty cell. The first line names the columns, matched to the
    /// columns of the target by name (ordinal); the order is free, missing columns get <see cref="CellText.DefaultValue"/>, unknown
    /// ones are dropped. One bad cell refuses the whole text. Reads the text once; changes nothing in the target.
    /// </summary>
    public static PasteResult Read(string text, RowsDocument target)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(target);

        var position = 0;
        if (!TryNextLine(text, ref position, out var header))
        {
            return Refuse(PasteProblem.Empty, 0, "The text is empty");
        }

        // The header columns are mapped once: map[h] is the column of the target for header cell h, or -1 for an unknown name.
        var columns = target.Columns;
        var byName = new Dictionary<string, int>(columns.Count, StringComparer.Ordinal);
        for (var c = 0; c < columns.Count; c++)
        {
            byName.TryAdd(columns[c].Name, c);
        }

        var names = header.Split(Separator);
        var map = new int[names.Length];
        var present = new bool[columns.Count];
        var extra = new List<string>();
        var known = 0;
        for (var h = 0; h < names.Length; h++)
        {
            if (!byName.TryGetValue(names[h], out var column))
            {
                map[h] = -1;
                extra.Add(names[h]);
                continue;
            }

            if (present[column])
            {
                return Refuse(PasteProblem.DuplicateColumn, 1, $"The column '{Shorten(names[h])}' occurs twice in the header");
            }

            present[column] = true;
            map[h] = column;
            known++;
        }

        if (known == 0)
        {
            return Refuse(PasteProblem.NoKnownColumn, 1, "No name in the first line is a column of this table");
        }

        var missing = new List<string>();
        for (var c = 0; c < columns.Count; c++)
        {
            if (!present[c])
            {
                missing.Add(columns[c].Name);
            }
        }

        var rows = new List<object?[]>();
        var line = 1;
        while (TryNextLine(text, ref position, out var content))
        {
            line++;
            var cells = content.Split(Separator);
            if (cells.Length != map.Length)
            {
                return Refuse(PasteProblem.WrongCellCount, line,
                    $"Line {line.ToString(CultureInfo.InvariantCulture)} has {cells.Length.ToString(CultureInfo.InvariantCulture)} cells, " +
                    $"the header has {map.Length.ToString(CultureInfo.InvariantCulture)}");
            }

            var row = target.NewRow();
            for (var h = 0; h < map.Length; h++)
            {
                var column = map[h];
                if (column < 0)
                {
                    continue;
                }

                var check = target.Check(column, CellText.Unescape(cells[h]));
                if (check.Problem != CellProblem.None)
                {
                    return Refuse(PasteProblem.InvalidValue, line,
                        $"Line {line.ToString(CultureInfo.InvariantCulture)}, column '{Shorten(columns[column].Name)}', " +
                        $"value '{Shorten(cells[h])}': {check.Detail}");
                }

                row[column] = check.Value;
            }

            rows.Add(row);
        }

        if (rows.Count == 0)
        {
            return Refuse(PasteProblem.Empty, 0, "The text has a header line but no rows");
        }

        return new PasteResult(rows, missing, extra, PasteProblem.None, 0, null);
    }

    private static PasteResult Refuse(PasteProblem problem, int line, string detail) =>
        new([], [], [], problem, line, detail);

    // The next line without its line end ("\n" or "\r\n"; a "\r" at the very end of the text belongs to the end as well). False when
    // nothing is left, which includes one empty last line.
    private static bool TryNextLine(string text, ref int position, out string line)
    {
        line = string.Empty;
        if (position >= text.Length)
        {
            return false;
        }

        var start = position;
        var feed = text.IndexOf('\n', start);
        var end = feed < 0 ? text.Length : feed;
        position = feed < 0 ? text.Length : feed + 1;
        if (end > start && text[end - 1] == '\r')
        {
            end--;
        }

        if (feed < 0 && end == start)
        {
            return false;
        }

        line = text.Substring(start, end - start);
        return true;
    }

    // A value or name for a Detail: at most MaxDetailValueLength characters, never cut inside a surrogate pair, control characters and
    // unpaired surrogates written as \uXXXX, "..." when it was cut. The same rule as the details of TableTsv.
    private static string Shorten(string value)
    {
        var length = Math.Min(value.Length, MaxDetailValueLength);
        if (length < value.Length && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        var builder = new StringBuilder(length + 3);
        for (var i = 0; i < length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(c).Append(value[++i]);
            }
            else if (char.IsControl(c) || char.IsSurrogate(c))
            {
                builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(c);
            }
        }

        return length < value.Length ? builder.Append("...").ToString() : builder.ToString();
    }
}

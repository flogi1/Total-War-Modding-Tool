using System.Globalization;
using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Editing;

/// <summary>
/// Why a pasted text is not accepted. None: the rows are ready. Empty: no text, or a header without rows. NoKnownColumn: no header name is a
/// column of the target. DuplicateColumn: a column of the target is named twice in the header, or the target itself has two columns of
/// one name. WrongCellCount: a line has another number of cells than the header. InvalidValue: a cell is not accepted by the target
/// (<see cref="RowsDocument.Check"/>).
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
/// them, are not interpreted: the quotes would be part of the value. The rule of both directions: what <see cref="Write"/> returns,
/// <see cref="Read"/> gives back as the same values.
/// </summary>
public static class ClipboardRows
{
    private const char Separator = '\t';
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// The header line with the column names, then one line per row. A cell is <see cref="CellText.Format"/> escaped with
    /// <see cref="CellText.Escape"/>; every line ends with a line feed. A row is an array of one cell per column, in the order of
    /// <paramref name="columns"/>. Text is null and Detail set (row among the given rows, 1-based, column, reason) when a cell cannot be
    /// carried: <see cref="CellText.TryParse"/> would refuse it or return another value (a carriage return, a literal escape sequence, a
    /// lone surrogate, a NUL, a NaN with a payload); also when two columns have the same name. ArgumentException for a row of another
    /// length or a value of the wrong type for its column.
    /// </summary>
    public static TsvWriteResult Write(IReadOnlyList<FieldDefinition> columns, IEnumerable<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        if (FirstDuplicateName(columns) is { } duplicate)
        {
            return new TsvWriteResult(null, DuplicateInTableDetail(duplicate));
        }

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
        var number = 0;
        foreach (var row in rows)
        {
            number++;
            if (row is null || row.Length != columns.Count)
            {
                throw new ArgumentException(
                    $"Row {number.ToString(CultureInfo.InvariantCulture)} has {row?.Length.ToString(CultureInfo.InvariantCulture) ?? "no"} cells, " +
                    $"there are {columns.Count.ToString(CultureInfo.InvariantCulture)} columns", nameof(rows));
            }

            for (var c = 0; c < columns.Count; c++)
            {
                var type = columns[c].Type;
                var cell = CellText.Format(type, row[c]);
                string? refusal = null;
                if (!CellText.TryParse(type, cell, out var parsed, out var reason))
                {
                    refusal = reason;
                }
                else if (!SameBits(row[c], parsed))
                {
                    refusal = "the value would not come back as the same number, a NaN with a payload";
                }

                if (refusal is not null)
                {
                    return new TsvWriteResult(null,
                        $"Row {number.ToString(CultureInfo.InvariantCulture)}, column '{CellText.ShortenForDetail(columns[c].Name)}', " +
                        $"value '{CellText.ShortenForDetail(cell)}': {refusal}");
                }

                if (c > 0)
                {
                    builder.Append(Separator);
                }

                builder.Append(CellText.Escape(cell));
            }

            builder.Append('\n');
        }

        return new TsvWriteResult(builder.ToString(), null);
    }

    /// <summary>
    /// Reads pasted text into rows for <paramref name="target"/>. A leading byte order mark is ignored. Lines end with a line feed or a
    /// carriage return and a line feed; one empty last line is no row, an empty line in the middle is a row with one empty cell. The
    /// first line names the columns, matched to the columns of the target by name (ordinal); the order is free, missing columns get
    /// <see cref="CellText.DefaultValue"/>, unknown ones are dropped. One bad cell refuses the whole text. Reads the text once; changes
    /// nothing in the target.
    /// </summary>
    public static PasteResult Read(string text, RowsDocument target)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(target);

        var columns = target.Columns;
        if (FirstDuplicateName(columns) is { } own)
        {
            return Refuse(PasteProblem.DuplicateColumn, 0, DuplicateInTableDetail(own));
        }

        var position = text.Length > 0 && text[0] == ByteOrderMark ? 1 : 0;
        if (!TryNextLine(text, ref position, out var header))
        {
            return Refuse(PasteProblem.Empty, 0, "The text is empty");
        }

        // The header columns are mapped once: map[h] is the column of the target for header cell h, or -1 for an unknown name.
        var byName = new Dictionary<string, int>(columns.Count, StringComparer.Ordinal);
        for (var c = 0; c < columns.Count; c++)
        {
            byName[columns[c].Name] = c;
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
                extra.Add(CellText.ShortenForDetail(names[h]));
                continue;
            }

            if (present[column])
            {
                return Refuse(PasteProblem.DuplicateColumn, 1,
                    $"The column '{CellText.ShortenForDetail(names[h])}' occurs twice in the first line");
            }

            present[column] = true;
            map[h] = column;
            known++;
        }

        if (known == 0)
        {
            return Refuse(PasteProblem.NoKnownColumn, 1,
                $"The first line must contain the column names of the table, but '{CellText.ShortenForDetail(header)}' contains none of them");
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
                    $"Line {line.ToString(CultureInfo.InvariantCulture)} has {Cells(cells.Length)}, expected {map.Length.ToString(CultureInfo.InvariantCulture)}");
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
                        $"Line {line.ToString(CultureInfo.InvariantCulture)}, column '{CellText.ShortenForDetail(columns[column].Name)}', " +
                        $"value '{CellText.ShortenForDetail(cells[h])}': {check.Detail}");
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

    private static string Cells(int count) =>
        count == 1 ? "1 cell" : $"{count.ToString(CultureInfo.InvariantCulture)} cells";

    private static string DuplicateInTableDetail(string name) =>
        $"The table itself has two columns named '{CellText.ShortenForDetail(name)}'";

    private static string? FirstDuplicateName(IReadOnlyList<FieldDefinition> columns)
    {
        var seen = new HashSet<string>(columns.Count, StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (!seen.Add(column.Name))
            {
                return column.Name;
            }
        }

        return null;
    }

    // Only floats can differ: a value and what TryParse reads from its text must have the same bits.
    private static bool SameBits(object? value, object? parsed) => (value, parsed) switch
    {
        (float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b),
        (double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b),
        _ => true,
    };

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
}

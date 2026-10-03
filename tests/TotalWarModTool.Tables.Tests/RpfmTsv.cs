using System.Globalization;

namespace TotalWarModTool.Tables.Tests;

/// <summary>Reads a TSV export of rpfm_cli and compares its cells with decoded values. BCL only.</summary>
internal sealed class RpfmTsv
{
    // RPFM writes floats with 4 decimals.
    private const double FloatTolerance = 0.00006;

    private RpfmTsv(IReadOnlyList<string> columns, IReadOnlyList<string[]> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<string[]> Rows { get; }

    /// <summary>Line 1 holds the column names, line 2 is a meta line and skipped, empty lines at the end do not count.</summary>
    public static RpfmTsv Read(string path)
    {
        var lines = File.ReadAllText(path).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var columns = lines.Count > 0 ? lines[0].Split('\t') : [];
        var rows = lines.Skip(2).Select(l => l.Split('\t')).ToList();
        return new RpfmTsv(columns, rows);
    }

    public static bool CellEquals(object? mine, string tsv, bool ignoreCase = false)
    {
        switch (mine)
        {
            case bool b:
                return string.Equals(tsv, b ? "true" : "false", StringComparison.OrdinalIgnoreCase);
            case short or int or long:
                return string.Equals(Convert.ToString(mine, CultureInfo.InvariantCulture), tsv, StringComparison.Ordinal);
            case float f:
                return double.TryParse(tsv, NumberStyles.Float, CultureInfo.InvariantCulture, out var tf) && Math.Abs(f - tf) <= FloatTolerance;
            case double d:
                return double.TryParse(tsv, NumberStyles.Float, CultureInfo.InvariantCulture, out var td) && Math.Abs(d - td) <= FloatTolerance;
            case string s:
                // rpfm_cli writes a real tab as backslash backslash t and a real line break as backslash backslash n.
                var unescaped = tsv.Replace("\\\\t", "\t").Replace("\\\\n", "\n");
                var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(s, unescaped, comparison) || string.Equals(s, tsv, comparison);
            default:
                return false;
        }
    }
}

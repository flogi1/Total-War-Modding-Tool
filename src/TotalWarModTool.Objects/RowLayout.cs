using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

/// <summary>Maps a row between two definitions of one table by field name, as the store does when it merges layouts.</summary>
public static class RowLayout
{
    /// <summary>For every target field the index of the field of the same name (a changed type is converted in <see cref="Remap"/>) in the source definition, -1 when there is none. Null when a target field has an unsupported type.</summary>
    public static int[]? MapFields(TableDefinition target, TableDefinition source)
    {
        var mapping = new int[target.Fields.Count];
        for (var i = 0; i < mapping.Length; i++)
        {
            var field = target.Fields[i];
            if (field.Type == FieldType.Unsupported)
            {
                return null;
            }

            mapping[i] = -1;
            for (var j = 0; j < source.Fields.Count; j++)
            {
                if (string.Equals(source.Fields[j].Name, field.Name, StringComparison.OrdinalIgnoreCase))
                {
                    mapping[i] = j;
                    break;
                }
            }
        }

        return mapping;
    }

    public static bool IsIdentity(int[] mapping, TableDefinition target, TableDefinition source)
    {
        if (target.Fields.Count != source.Fields.Count)
        {
            return false;
        }

        for (var i = 0; i < mapping.Length; i++)
        {
            if (mapping[i] != i || target.Fields[i].Type != source.Fields[i].Type)
            {
                return false;
            }
        }

        return true;
    }

    public static object?[] Remap(TableDefinition target, TableDefinition source, int[] mapping, object?[] row)
    {
        var cells = new object?[mapping.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            var targetType = target.Fields[i].Type;
            if (mapping[i] < 0)
            {
                cells[i] = CellText.DefaultValue(targetType);
                continue;
            }

            var sourceType = source.Fields[mapping[i]].Type;
            cells[i] = sourceType == targetType ? row[mapping[i]] : Convert(sourceType, targetType, row[mapping[i]]);
        }

        return cells;
    }

    /// <summary>A field whose type changed between versions: through its text, the default value when the text does not fit.</summary>
    private static object? Convert(FieldType sourceType, FieldType targetType, object? value)
    {
        try
        {
            if (CellText.TryParse(targetType, CellText.Format(sourceType, value), out var converted, out _))
            {
                return converted;
            }
        }
        catch (ArgumentException)
        {
            // The value does not have the type its definition says; the default value is the safe answer.
        }

        return CellText.DefaultValue(targetType);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables;

/// <summary>
/// The rules for one cell of the TSV dialect, in one place: <see cref="TableTsv"/> reads and writes cells with them, and an editing
/// document validates and shows a cell with them, so that what the document accepts is what <see cref="TableTsv"/> writes and reads
/// back as the same value. The cell types are those of <see cref="DecodedTable"/>: Boolean bool, I16 short, I32 int, I64 long, F32 float,
/// F64 double, ColourRgb string of at least six upper-case hex digits, the three text types string.
/// </summary>
public static class CellText
{
    private const string TabEscape = "\\\\t";
    private const string LineFeedEscape = "\\\\n";

    /// <summary>The outcome of <see cref="TryFormat"/>.</summary>
    internal enum FormatResult { Ok, WrongType, NotAColour }

    /// <summary>
    /// The text of a cell as TableTsv writes it, before the escaping of tabs and line breaks: "true"/"false", integers and
    /// floats in the invariant culture (floats in the shortest exact form), a colour as at least 6 upper-case hex digits, text as it is.
    /// Throws ArgumentException when the value does not have the type of the field (see DecodedTable for the types), is not a
    /// colour, or the type is <see cref="FieldType.Unsupported"/>.
    /// </summary>
    public static string Format(FieldType type, object? value)
    {
        return TryFormat(type, value, out var text) switch
        {
            FormatResult.Ok => text,
            FormatResult.NotAColour => throw new ArgumentException("The value is not a colour of 1 to " + ColourText.MaxDigits.ToString(CultureInfo.InvariantCulture) + " hex digits", nameof(value)),
            _ => throw new ArgumentException(
                value is null ? "The value is null, which does not fit the field type " + type : "The value is a " + value.GetType().Name + ", which does not fit the field type " + type,
                nameof(value)),
        };
    }

    /// <summary>
    /// The value for a cell text, with the rules of TableTsv.Read for one unescaped cell. False with a short English reason.
    /// A colour is normalised to the form Format writes. Text is refused when it contains a lone surrogate, a NUL,
    /// a carriage return, more than the u16 length limit, or the literal two-character sequences the dialect uses as escapes
    /// (backslash backslash t, backslash backslash n).
    /// </summary>
    public static bool TryParse(FieldType type, string text, out object? value, [NotNullWhen(false)] out string? reason)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsText(type))
        {
            return TryParseScalar(type, text, out value, out reason);
        }

        value = null;
        reason = TextRefusal(text, IsUtf8(type));
        if (reason is null && text.Contains('\0'))
        {
            reason = "the text contains a NUL character";
        }

        if (reason is not null)
        {
            return false;
        }

        value = text;
        return true;
    }

    /// <summary>The default value of a new cell: false, 0, 0.0, "000000", "". Throws ArgumentException for <see cref="FieldType.Unsupported"/>.</summary>
    public static object DefaultValue(FieldType type) => type switch
    {
        FieldType.Boolean => false,
        FieldType.I16 => (short)0,
        FieldType.I32 => 0,
        FieldType.I64 => 0L,
        FieldType.F32 => 0f,
        FieldType.F64 => 0.0,
        FieldType.ColourRgb => "000000",
        FieldType.StringU8 or FieldType.StringU16 or FieldType.OptionalStringU8 => string.Empty,
        _ => throw new ArgumentException("The field type " + type + " has no default value", nameof(type)),
    };

    /// <summary>
    /// The indexes of the fields that TableTsv writes as part of a colour group (r/g/b triple). Their values must be 0 to 255.
    /// Decided by the very code that decides the columns for TableTsv.Write.
    /// </summary>
    public static IReadOnlySet<int> ColourChannelFields(TableDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var indexes = new HashSet<int>();
        foreach (var column in TableTsv.ColumnsOf(definition))
        {
            if (column.Group is { } group)
            {
                indexes.Add(group.Red);
                indexes.Add(group.Green);
                indexes.Add(group.Blue);
            }
        }

        return indexes;
    }

    /// <summary>
    /// Escapes a tab and a line feed the way the dialect does (two backslashes and a t or an n); <see cref="Unescape"/> is the exact
    /// inverse for text Escape produced. A carriage return is left as it is: the dialect has no escape for it, and
    /// <see cref="TryParse"/> and TableTsv.Write refuse text that contains one.
    /// </summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\t", TabEscape, StringComparison.Ordinal).Replace("\n", LineFeedEscape, StringComparison.Ordinal);
    }

    /// <summary>Turns the escape sequences of the dialect back into a tab and a line feed.</summary>
    public static string Unescape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace(TabEscape, "\t", StringComparison.Ordinal).Replace(LineFeedEscape, "\n", StringComparison.Ordinal);
    }

    // ----- For TableTsv -----

    internal static bool IsNumeric(FieldType type) => type is FieldType.I16 or FieldType.I32 or FieldType.I64 or FieldType.F32 or FieldType.F64;

    internal static bool IsText(FieldType type) => type is FieldType.StringU8 or FieldType.StringU16 or FieldType.OptionalStringU8;

    // u8 strings are counted in UTF-8 bytes, u16 strings in UTF-16 units.
    internal static bool IsUtf8(FieldType type) => type is FieldType.StringU8 or FieldType.OptionalStringU8;

    /// <summary>
    /// The text of a value. A text value is returned as it is (the caller checks <see cref="TextRefusal"/> and escapes). Wrong type
    /// is a model error for the caller to report; NotAColour is a refusal because the text cannot be written without loss.
    /// </summary>
    internal static FormatResult TryFormat(FieldType type, object? value, out string text)
    {
        text = string.Empty;
        switch (type)
        {
            case FieldType.Boolean when value is bool v:
                text = v ? "true" : "false";
                return FormatResult.Ok;
            case FieldType.I16 when value is short v:
                text = v.ToString(CultureInfo.InvariantCulture);
                return FormatResult.Ok;
            case FieldType.I32 when value is int v:
                text = v.ToString(CultureInfo.InvariantCulture);
                return FormatResult.Ok;
            case FieldType.I64 when value is long v:
                text = v.ToString(CultureInfo.InvariantCulture);
                return FormatResult.Ok;
            case FieldType.F32 when value is float v:
                text = v.ToString("R", CultureInfo.InvariantCulture);
                return FormatResult.Ok;
            case FieldType.F64 when value is double v:
                text = v.ToString("R", CultureInfo.InvariantCulture);
                return FormatResult.Ok;
            case FieldType.ColourRgb when value is string v:
                // Written as Read returns it: uppercase, at least six digits.
                if (!ColourText.TryParse(v, out var colour))
                {
                    return FormatResult.NotAColour;
                }

                text = colour.ToString("X6", CultureInfo.InvariantCulture);
                return FormatResult.Ok;
            case FieldType.StringU8 or FieldType.StringU16 or FieldType.OptionalStringU8 when value is string v:
                text = v;
                return FormatResult.Ok;
            default:
                return FormatResult.WrongType;
        }
    }

    /// <summary>Why a text cannot be written as a cell of the dialect without loss, or null: the one rule for Write and <see cref="TryParse"/>.</summary>
    internal static string? TextRefusal(string value, bool utf8)
    {
        if (value.Contains('\r'))
        {
            return "the text contains a carriage return, which the TSV dialect cannot carry";
        }

        // These would be read back as a tab or a line break.
        if (value.Contains(TabEscape, StringComparison.Ordinal) || value.Contains(LineFeedEscape, StringComparison.Ordinal))
        {
            return "the text contains a literal backslash backslash t or n, which the TSV dialect cannot tell from an escaped tab or line break";
        }

        return ByteWriter.EncodingProblem(value, utf8);
    }

    /// <summary>The reason of a colour text that is not 1 to 8 hex digits.</summary>
    internal static string NotAColourReason() =>
        $"not a colour of 1 to {ColourText.MaxDigits.ToString(CultureInfo.InvariantCulture)} hex digits";

    /// <summary>
    /// One cell as TableTsv.Read sees it in the file: a text cell is unescaped first. On failure the reason names what is wrong;
    /// otherwise it is null. Never throws for the cell. Unlike <see cref="TryParse"/> it accepts a NUL, as Read always did.
    /// </summary>
    internal static bool TryParseTsvCell(FieldType type, string cell, out object? value, [NotNullWhen(false)] out string? reason)
    {
        if (!IsText(type))
        {
            return TryParseScalar(type, cell, out value, out reason);
        }

        var text = Unescape(cell);
        // Write refuses a carriage return, so Read does not accept one either.
        reason = text.Contains('\r') ? "the text contains a carriage return" : ByteWriter.EncodingProblem(text, IsUtf8(type));
        value = text;
        return reason is null;
    }

    // ----- Parsing -----

    private static bool TryParseScalar(FieldType type, string cell, out object? value, [NotNullWhen(false)] out string? reason)
    {
        value = null;
        reason = null;
        switch (type)
        {
            case FieldType.Boolean:
                if (cell.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                    return true;
                }

                if (cell.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                    return true;
                }

                reason = "not true or false";
                return false;
            case FieldType.I16:
                if (short.TryParse(cell, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i16))
                {
                    value = i16;
                    return true;
                }

                reason = "not a 16-bit integer";
                return false;
            case FieldType.I32:
                if (int.TryParse(cell, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i32))
                {
                    value = i32;
                    return true;
                }

                reason = "not a 32-bit integer";
                return false;
            case FieldType.I64:
                if (long.TryParse(cell, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i64))
                {
                    value = i64;
                    return true;
                }

                reason = "not a 64-bit integer";
                return false;
            case FieldType.F32:
                if (!float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var f32))
                {
                    reason = "not a number";
                    return false;
                }

                if (!IsFiniteOrNamed(f32, cell))
                {
                    reason = "the number is too large for a 32-bit float";
                    return false;
                }

                value = f32;
                return true;
            case FieldType.F64:
                if (!double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var f64))
                {
                    reason = "not a number";
                    return false;
                }

                if (!IsFiniteOrNamed(f64, cell))
                {
                    reason = "the number is too large for a 64-bit float";
                    return false;
                }

                value = f64;
                return true;
            case FieldType.ColourRgb:
                if (ColourText.TryParse(cell, out var colour))
                {
                    value = colour.ToString("X6", CultureInfo.InvariantCulture);
                    return true;
                }

                reason = NotAColourReason();
                return false;
            default:
                reason = "the field type is not supported";
                return false;
        }
    }

    // A parse that overflows gives an infinity. That is only accepted when the cell is one of the forms Write emits for it.
    private static bool IsFiniteOrNamed(double parsed, string cell) =>
        double.IsFinite(parsed)
        || cell.Equals(double.IsNaN(parsed) ? "NaN" : parsed > 0 ? "Infinity" : "-Infinity", StringComparison.OrdinalIgnoreCase);
}

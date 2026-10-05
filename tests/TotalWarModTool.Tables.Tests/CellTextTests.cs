using System.Globalization;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class CellTextTests
{
    private static FieldDefinition F(string name, FieldType type) => new(name, type, type.ToString(), false, null);

    private static GameSchema Schema(string table, int version, params FieldDefinition[] fields) =>
        new(new Dictionary<string, IReadOnlyList<TableDefinition>> { [table] = [new TableDefinition(version, fields)] });

    private static readonly FieldType[] SupportedTypes = Enum.GetValues<FieldType>().Where(t => t != FieldType.Unsupported).ToArray();

    [Theory]
    [InlineData(FieldType.Boolean, "true")]
    [InlineData(FieldType.Boolean, "false")]
    [InlineData(FieldType.I16, "-32768")]
    [InlineData(FieldType.I16, "32767")]
    [InlineData(FieldType.I32, "-2147483648")]
    [InlineData(FieldType.I64, "9223372036854775807")]
    [InlineData(FieldType.F32, "0.1")]
    [InlineData(FieldType.F32, "NaN")]
    [InlineData(FieldType.F64, "1E-05")]
    [InlineData(FieldType.F64, "0.30000000000000004")]
    [InlineData(FieldType.ColourRgb, "00FF7F")]
    [InlineData(FieldType.ColourRgb, "1FFFFFF")]
    [InlineData(FieldType.StringU8, "hello world")]
    [InlineData(FieldType.StringU8, "tab\there")]
    [InlineData(FieldType.StringU16, "line\nbreak")]
    [InlineData(FieldType.OptionalStringU8, "")]
    public void Format_and_TryParse_round_trip_every_field_type(FieldType type, string text)
    {
        Assert.True(CellText.TryParse(type, text, out var value, out var reason), reason);
        Assert.Equal(text, CellText.Format(type, value));
    }

    [Fact] public void Format_writes_what_TableTsv_writes()
    {
        var fields = new[]
        {
            F("flag", FieldType.Boolean), F("small", FieldType.I16), F("n", FieldType.I32), F("big", FieldType.I64), F("ratio", FieldType.F32),
            F("exact", FieldType.F64), F("tint", FieldType.ColourRgb), F("key", FieldType.StringU8), F("wide", FieldType.StringU16),
            F("maybe", FieldType.OptionalStringU8),
        };
        var definition = new TableDefinition(1, fields);
        object?[] row = [true, (short)-5, 70000, 5_000_000_000L, 0.1f, 1e-5, "4080ff", "a\tb\nc", "x", ""];
        var text = TableTsv.Write("t_tables", 1, "p", definition, [row]).Text!;
        var cells = text.Split('\n')[2].Split('\t');
        Assert.Equal(fields.Length, cells.Length);
        for (var i = 0; i < fields.Length; i++)
        {
            Assert.Equal(CellText.Format(fields[i].Type, row[i]), CellText.Unescape(cells[i]));
        }

        Assert.Equal("4080FF", cells[6]);
    }

    public static IEnumerable<object[]> ReadParityCases()
    {
        string[] numbers = ["0", "-0", "+5", "1", "-1", "32767", "-32768", "32768", "2147483648", "9223372036854775808", "1.5", "", " 1", "1 ", "abc", "--1", "0x10", "1,5"];
        string[] floats = ["0.1", "-0", "1e39", "-1e39", "1e309", "1E-45", "1e-50", "NaN", "nan", "Infinity", "-infinity", "inf", ".5", "5.", "1e5", "1,5", "", "abc", " 1"];
        string[] colours = ["ff7f", "00FF7F", "GG0000", "", "0", "123456789", "12345678", "1FFFFFF", "+1", "0x10", "-1", " 1"];
        string[] booleans = ["true", "TRUE", "False", "", "yes", "1", " true", "tru"];
        string[] texts = ["abc", "", "a b", "caf" + (char)0xE9, "back\\slash", "\\t", "\\\\", "x\\", "a" + (char)0xA0 + "b", new string('a', 65535), new string('a', 65536), new string((char)0xE9, 32768)];

        foreach (var t in new[] { FieldType.I16, FieldType.I32, FieldType.I64 })
        {
            foreach (var n in numbers)
            {
                yield return [t, n];
            }
        }

        foreach (var t in new[] { FieldType.F32, FieldType.F64 })
        {
            foreach (var n in floats.Concat(numbers))
            {
                yield return [t, n];
            }
        }

        foreach (var c in colours)
        {
            yield return [FieldType.ColourRgb, c];
        }

        foreach (var b in booleans)
        {
            yield return [FieldType.Boolean, b];
        }

        foreach (var t in new[] { FieldType.StringU8, FieldType.StringU16, FieldType.OptionalStringU8 })
        {
            foreach (var s in texts)
            {
                yield return [t, s];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ReadParityCases))]
    public void TryParse_accepts_what_TableTsv_reads(FieldType type, string cell)
    {
        // A second column that is always valid keeps an empty cell from being the only cell of the line.
        var schema = Schema("t_tables", 1, F("c", type), F("k", FieldType.Boolean));
        var read = TableTsv.Read("c\tk\n#t_tables;1;p\t\n" + cell + "\ttrue\n", schema);

        var accepted = CellText.TryParse(type, cell, out var value, out var reason);
        Assert.Equal(read.Problem is null, accepted);
        if (accepted)
        {
            Assert.Null(reason);
            Assert.Equal(read.Table!.Rows[0][0], value);

            // What TryParse accepts is written by TableTsv.Write and read back as the same value.
            var written = TableTsv.Write("t_tables", 1, "p", read.Table.Definition, [[value, true]]);
            Assert.Null(written.Detail);
            var back = TableTsv.Read(written.Text!, schema);
            Assert.Null(back.Problem);
            Assert.Equal(value, back.Table!.Rows[0][0]);
            Assert.Equal(true, back.Table.Rows[0][1]);
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(reason));
        }
    }

    public static IEnumerable<object[]> RefusedCases()
    {
        yield return [FieldType.I32, "1.5"];
        yield return [FieldType.I16, "32768"];
        yield return [FieldType.I64, "9223372036854775808"];
        yield return [FieldType.F64, "abc"];
        yield return [FieldType.F32, "1e39"];
        yield return [FieldType.F64, "1e309"];
        yield return [FieldType.Boolean, "yes"];
        yield return [FieldType.ColourRgb, "GG0000"];
        yield return [FieldType.ColourRgb, ""];
        yield return [FieldType.ColourRgb, "123456789"];
        yield return [FieldType.StringU8, "a\\\\tb"];
        yield return [FieldType.StringU16, "a\\\\nb"];
        yield return [FieldType.OptionalStringU8, "\\\\t"];
        yield return [FieldType.StringU8, "a" + (char)0xD800 + "b"];
        yield return [FieldType.StringU16, "a" + (char)0xDC00];
        yield return [FieldType.StringU8, "\0"];
        yield return [FieldType.StringU16, "a\0b"];
        yield return [FieldType.StringU16, "a\rb"];
        yield return [FieldType.StringU8, new string('a', 65536)];
        yield return [FieldType.Unsupported, "x"];
    }

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public void Every_refused_input_names_its_reason(FieldType type, string text)
    {
        Assert.False(CellText.TryParse(type, text, out var value, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Null(value);
    }

    [Fact] public void A_NUL_is_refused_by_TryParse_but_TableTsv_keeps_reading_it()
    {
        // TryParse is stricter than Read here; Read stays as it was.
        var schema = Schema("t_tables", 1, F("c", FieldType.StringU16), F("k", FieldType.Boolean));
        var read = TableTsv.Read("c\tk\n#t_tables;1;p\t\na\0b\ttrue\n", schema);
        Assert.Null(read.Problem);
        Assert.Equal("a\0b", read.Table!.Rows[0][0]);
        Assert.False(CellText.TryParse(FieldType.StringU16, "a\0b", out _, out _));
    }

    [Fact] public void Anything_TableTsv_reads_for_a_text_cell_is_accepted_by_TryParse_and_written_back()
    {
        // Read unescapes, so the text it returns never holds a literal backslash backslash t or n, even from nasty backslash runs.
        var schema = Schema("t_tables", 1, F("c", FieldType.StringU16), F("k", FieldType.Boolean));
        string[] cells = ["\\\\t", "\\\\\\t", "\\\\\\\\t", "\\\\\\n", "\\\\n\\\\t", "\\\\tn", "\\\\\\\\\\\\n", "\\", "\\\\", "x\\\\t\\\\ny"];
        foreach (var cell in cells)
        {
            var read = TableTsv.Read("c\tk\n#t_tables;1;p\t\n" + cell + "\ttrue\n", schema);
            Assert.Null(read.Problem);
            var text = (string)read.Table!.Rows[0][0]!;
            Assert.True(CellText.TryParse(FieldType.StringU16, text, out var value, out var reason), reason);
            Assert.Equal(text, value);
            var written = TableTsv.Write("t_tables", 1, "p", read.Table.Definition, read.Table.Rows).Text!;
            Assert.Equal(cell + "\ttrue", written.Split('\n')[2]);
            Assert.Equal(text, CellText.Unescape(CellText.Escape(text)));
        }
    }

    [Fact] public void A_colour_is_normalised()
    {
        Assert.True(CellText.TryParse(FieldType.ColourRgb, "ff7f", out var value, out _));
        Assert.Equal("00FF7F", value);
        Assert.True(CellText.TryParse(FieldType.ColourRgb, "ABCDEF12", out value, out _));
        Assert.Equal("ABCDEF12", value);
    }

    [Fact] public void DefaultValue_can_be_formatted_and_parsed_for_every_type()
    {
        foreach (var type in SupportedTypes)
        {
            var value = CellText.DefaultValue(type);
            var text = CellText.Format(type, value);
            Assert.True(CellText.TryParse(type, text, out var parsed, out var reason), reason);
            Assert.Equal(value, parsed);
        }

        Assert.Equal(false, CellText.DefaultValue(FieldType.Boolean));
        Assert.Equal((short)0, CellText.DefaultValue(FieldType.I16));
        Assert.Equal(0, CellText.DefaultValue(FieldType.I32));
        Assert.Equal(0L, CellText.DefaultValue(FieldType.I64));
        Assert.Equal(0f, CellText.DefaultValue(FieldType.F32));
        Assert.Equal(0.0, CellText.DefaultValue(FieldType.F64));
        Assert.Equal("000000", CellText.DefaultValue(FieldType.ColourRgb));
        Assert.Equal("", CellText.DefaultValue(FieldType.StringU8));
        Assert.Equal("", CellText.DefaultValue(FieldType.StringU16));
        Assert.Equal("", CellText.DefaultValue(FieldType.OptionalStringU8));
    }

    [Fact] public void An_unsupported_field_type_has_no_format_and_no_default()
    {
        Assert.Throws<ArgumentException>(() => CellText.Format(FieldType.Unsupported, "x"));
        Assert.Throws<ArgumentException>(() => CellText.DefaultValue(FieldType.Unsupported));
    }

    [Fact] public void Format_throws_for_a_value_of_the_wrong_type()
    {
        Assert.Throws<ArgumentException>(() => CellText.Format(FieldType.I32, "1"));
        Assert.Throws<ArgumentException>(() => CellText.Format(FieldType.I32, (short)1));
        Assert.Throws<ArgumentException>(() => CellText.Format(FieldType.Boolean, null));
        Assert.Throws<ArgumentException>(() => CellText.Format(FieldType.F32, 1.0));
        Assert.Throws<ArgumentException>(() => CellText.Format(FieldType.StringU8, 5));
        Assert.Throws<ArgumentException>(() => CellText.Format(FieldType.ColourRgb, "not a colour"));
    }

    [Fact] public void Format_uses_the_invariant_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("0.1", CellText.Format(FieldType.F64, 0.1));
            Assert.Equal("-1", CellText.Format(FieldType.I32, -1));
            Assert.True(CellText.TryParse(FieldType.F64, "1.5", out var value, out _));
            Assert.Equal(1.5, value);
            Assert.False(CellText.TryParse(FieldType.F64, "1,5", out _, out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact] public void ColourChannelFields_finds_the_groups_TableTsv_writes()
    {
        var definition = new TableDefinition(1,
        [
            F("key", FieldType.StringU8), F("x_r", FieldType.I32), F("x_g", FieldType.I32), F("x_b", FieldType.I32),
            F("r", FieldType.F64), F("g", FieldType.F64), F("b", FieldType.F64), F("y_r", FieldType.I32),
        ]);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, CellText.ColourChannelFields(definition).Order().ToArray());

        var header = TableTsv.Write("t_tables", 1, "p", definition, []).Text!.Split('\n')[0].Split('\t');
        Assert.Equal(new[] { "key", "y_r", "x_hex", "unnamed colour group_1" }, header);
    }

    private static object Channel(FieldType type, double d) => type switch
    {
        FieldType.I16 => (short)d,
        FieldType.I32 => (int)d,
        FieldType.I64 => (long)d,
        FieldType.F32 => (object)(float)d,
        _ => (object)d,
    };

    [Fact] public void IsColourChannelValue_is_true_exactly_when_TableTsv_writes_the_value_in_a_colour_group()
    {
        foreach (var type in new[] { FieldType.I16, FieldType.I32, FieldType.I64, FieldType.F32, FieldType.F64 })
        {
            var isFloat = type is FieldType.F32 or FieldType.F64;
            var definition = new TableDefinition(1, [F("r", type), F("g", type), F("b", type)]);
            var values = new List<double> { 0, 255, 256, -1, 1, 254 };
            if (isFloat)
            {
                values.AddRange([1.5, -0.0, 255.5, 0.5]);
            }

            foreach (var d in values)
            {
                var value = Channel(type, d);
                foreach (var position in new[] { 0, 1, 2 })
                {
                    var row = new[] { Channel(type, 0), Channel(type, 0), Channel(type, 0) };
                    row[position] = value;
                    var written = TableTsv.Write("t_tables", 1, "p", definition, [row]).Text is not null;
                    Assert.True(CellText.ColourChannelFields(definition).Contains(position));
                    Assert.Equal(written, CellText.IsColourChannelValue(value));
                }
            }
        }
    }

    [Fact] public void IsColourChannelValue_refuses_values_that_are_no_numbers_of_the_group_types()
    {
        Assert.False(CellText.IsColourChannelValue(null));
        Assert.False(CellText.IsColourChannelValue("12"));
        Assert.False(CellText.IsColourChannelValue(true));
        Assert.False(CellText.IsColourChannelValue(double.NaN));
        Assert.False(CellText.IsColourChannelValue(double.PositiveInfinity));
        Assert.False(CellText.IsColourChannelValue(float.NaN));
    }

    [Fact] public void ColourChannelFields_is_empty_without_groups()
    {
        var definition = new TableDefinition(1, [F("a", FieldType.I32), F("b", FieldType.I32), F("c", FieldType.F32)]);
        Assert.Empty(CellText.ColourChannelFields(definition));
        Assert.Empty(CellText.ColourChannelFields(new TableDefinition(1, [])));
    }

    [Fact] public void Escape_and_Unescape_are_inverse_and_match_TableTsv()
    {
        string[] texts = ["", "plain", "a\tb", "a\nb", "\t\n", "back\\slash", "x\\\ty", "x\\\ny", "\\", "\\\\", "trail\\", "wide " + char.ConvertFromUtf32(0x1F600)];
        Assert.Equal("a\\\\tb\\\\nc", CellText.Escape("a\tb\nc"));
        Assert.Equal("a\tb\nc", CellText.Unescape("a\\\\tb\\\\nc"));
        var definition = new TableDefinition(1, [F("c", FieldType.StringU16), F("k", FieldType.Boolean)]);
        foreach (var text in texts)
        {
            var escaped = CellText.Escape(text);
            Assert.DoesNotContain('\t', escaped);
            Assert.DoesNotContain('\n', escaped);
            Assert.Equal(text, CellText.Unescape(escaped));

            var written = TableTsv.Write("t_tables", 1, "p", definition, [[text, true]]).Text!;
            Assert.Equal(escaped, written.Split('\n')[2].Split('\t')[0]);
        }
    }

    [Fact] public void Escape_leaves_a_carriage_return_alone_because_the_dialect_cannot_carry_one()
    {
        Assert.Equal("a\rb", CellText.Escape("a\rb"));
        Assert.False(CellText.TryParse(FieldType.StringU16, "a\rb", out _, out _));
    }

    [Fact] public void ShortenForDetail_cuts_at_50_characters_and_never_inside_a_surrogate_pair()
    {
        Assert.Equal("", CellText.ShortenForDetail(""));
        Assert.Equal(new string('a', 50), CellText.ShortenForDetail(new string('a', 50)));
        Assert.Equal(new string('a', 50) + "...", CellText.ShortenForDetail(new string('a', 51)));

        var pair = new string([(char)0xD83D, (char)0xDE00]);
        var cutInside = new string('a', 49) + pair;
        Assert.Equal(new string('a', 49) + "...", CellText.ShortenForDetail(cutInside));
        Assert.Equal(new string('a', 48) + pair, CellText.ShortenForDetail(new string('a', 48) + pair));
        Assert.Throws<ArgumentNullException>(() => CellText.ShortenForDetail(null!));
    }

    [Fact] public void ShortenForDetail_writes_control_characters_and_lone_surrogates_as_escapes()
    {
        Assert.Equal("a\\u0009b\\u000A\\u000D\\u0000", CellText.ShortenForDetail("a\tb\n\r\0"));
        Assert.Equal("x\\uD800y\\uDC00", CellText.ShortenForDetail("x" + (char)0xD800 + "y" + (char)0xDC00));
    }
}

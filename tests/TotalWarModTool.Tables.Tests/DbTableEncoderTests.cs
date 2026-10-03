using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class DbTableEncoderTests
{
    private const string TestGuid = "46e87609-af6e-4857-bea4-f29b4fbd55fd";
    private static FieldDefinition F(string name, FieldType type) => new(name, type, type.ToString(), false, null);
    private static GameSchema Schema(int version, params FieldDefinition[] fields) =>
        new(new Dictionary<string, IReadOnlyList<TableDefinition>> { ["t_tables"] = [new TableDefinition(version, fields)] });

    private static readonly FieldDefinition[] AllTypes =
    [
        F("key", FieldType.StringU8), F("flag", FieldType.Boolean), F("small", FieldType.I16), F("count", FieldType.I32),
        F("big", FieldType.I64), F("ratio", FieldType.F32), F("exact", FieldType.F64), F("tint", FieldType.ColourRgb),
        F("wide", FieldType.StringU16), F("maybe", FieldType.OptionalStringU8),
    ];

    [Fact] public void Encodes_every_supported_field_type_like_the_decoder_reads_it()
    {
        var data = new TableBytes().Guid(TestGuid).Version(3).Rows(2)
            .Str8("ünit_日本_😀").Bool(true).I16(short.MinValue).I32(int.MaxValue).I64(long.MinValue).F32(0.1f).F64(double.Epsilon).Colour(0x004080FF).Str16("wide 😀").OptStr8("yes")
            .Str8("").Bool(false).I16(7).I32(-1).I64(-9).F32(float.NaN).F64(double.NegativeInfinity).Colour(0x01FFFFFF).Str16("").OptStr8(null)
            .Build();
        var t = DbTableDecoder.Decode(data, "t_tables", Schema(3, AllTypes));
        Assert.Equal(TableDecodeStatus.Ok, t.Status);
        Assert.Equal(data, DbTableEncoder.Encode(t.Definition!, t.Version, TestGuid, t.Rows));
    }

    [Fact] public void Version_0_writes_no_version_marker_and_null_guid_no_guid_block()
    {
        var def = new TableDefinition(0, [F("n", FieldType.I32)]);
        Assert.Equal(new TableBytes().Rows(1).I32(5).Build(), DbTableEncoder.Encode(def, 0, null, [[5]]));
        Assert.Equal(new TableBytes().Guid("").Rows(0).Build(), DbTableEncoder.Encode(def, 0, "", []));
    }

    [Fact] public void Empty_optional_text_is_written_as_absent()
    {
        var def = new TableDefinition(1, [F("maybe", FieldType.OptionalStringU8)]);
        Assert.Equal(new TableBytes().Version(1).Rows(1).OptStr8(null).Build(), DbTableEncoder.Encode(def, 1, null, [[""]]));
    }

    [Fact] public void String_longer_than_65535_bytes_throws()
    {
        var def = new TableDefinition(1, [F("s", FieldType.StringU8)]);
        Assert.Equal(TableDecodeStatus.Ok, DbTableDecoder.Decode(DbTableEncoder.Encode(def, 1, null, [[new string('a', 65535)]]), "t_tables", Schema(1, def.Fields.ToArray())).Status);
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[new string('a', 65536)]]));
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[new string('ü', 32768)]]));   // 65536 bytes
        var wide = new TableDefinition(1, [F("s", FieldType.StringU16)]);
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(wide, 1, null, [[new string('a', 65536)]]));
    }

    [Fact] public void Model_errors_throw()
    {
        var def = new TableDefinition(1, [F("n", FieldType.I32), F("c", FieldType.ColourRgb)]);
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[5]]));                    // row too short
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [["5", "000000"]]));       // wrong CLR type
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[5, null]]));
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[5, "GG"]]));
        var unsupported = new TableDefinition(1, [new FieldDefinition("x", FieldType.Unsupported, "SequenceU32", false, null)]);
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(unsupported, 1, null, []));
    }

    // Built from char values and shared as a field: the compiler replaces a lone surrogate with U+FFFD in attribute
    // arguments such as InlineData, so they cannot go there; a plain string literal would keep it.
    private const char High = (char)0xD800;
    private const char Low = (char)0xDC00;
    private static readonly string[] Unpaired = ["a" + High + "b", "a" + High, "a" + Low + "b", "" + Low + High, "" + High + High + Low];

    [Fact] public void Unpaired_surrogates_throw_for_every_string_path()
    {
        foreach (var text in Unpaired)
        {
            foreach (var type in new[] { FieldType.StringU8, FieldType.StringU16, FieldType.OptionalStringU8 })
            {
                var def = new TableDefinition(1, [F("s", type)]);
                Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[text]]));
            }

            Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(new TableDefinition(1, []), 1, text, []));
        }
    }

    [Fact] public void A_valid_surrogate_pair_still_encodes()
    {
        foreach (var type in new[] { FieldType.StringU8, FieldType.StringU16, FieldType.OptionalStringU8 })
        {
            var def = new TableDefinition(1, [F("s", type)]);
            var data = DbTableEncoder.Encode(def, 1, "😀", [["😀"]]);
            Assert.Equal(TableDecodeStatus.Ok, DbTableDecoder.Decode(data, "t_tables", Schema(1, def.Fields.ToArray())).Status);
        }

        Assert.Equal("😀", DbTableDecoder.ReadGuid(DbTableEncoder.Encode(new TableDefinition(1, []), 1, "😀", [])));
    }

    [Fact] public void ReadGuid_reads_the_header_guid_or_null()
    {
        Assert.Equal(TestGuid, DbTableDecoder.ReadGuid(new TableBytes().Guid(TestGuid).Version(3).Rows(0).Build()));
        Assert.Equal("", DbTableDecoder.ReadGuid(new TableBytes().Guid("").Rows(0).Build()));
        Assert.Null(DbTableDecoder.ReadGuid(new TableBytes().Version(3).Rows(0).Build()));
        Assert.Null(DbTableDecoder.ReadGuid([0xFD, 0xFE, 0xFC, 0xFF, 0x24]));
        Assert.Null(DbTableDecoder.ReadGuid([]));
    }
}

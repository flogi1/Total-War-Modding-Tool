using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects.Tests;

public class EntryConversionTests
{
    private static string NewGuid() => "00000000-0000-0000-0000-000000000001";

    [Fact] public void A_table_becomes_tsv_and_builds_back_to_the_same_content()
    {
        var bytes = TestData.Units(("unit_a", 100, 0.1f), ("ünit_b", -5, 2.0000002E-06f));
        var file = EntryConversion.ToProjectFile("db/units_tables/my_mod", bytes, TestData.Schema);
        Assert.Equal((ProjectFileKind.Table, null), (file.Kind, file.Note));
        Assert.Equal("key\tcost\tspeed\n#units_tables;2;db/units_tables/my_mod\t\t\nunit_a\t100\t0.1\nünit_b\t-5\t2.0000002E-06\n", Encoding.UTF8.GetString(file.Bytes));
        var entry = EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/my_mod", file.Bytes, TestData.Schema, () => TestData.HeaderGuid);
        Assert.Equal(bytes, entry.Bytes);
        Assert.Equal(NewGuid(), DbTableDecoder.ReadGuid(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/my_mod", file.Bytes, TestData.Schema, NewGuid).Bytes!));
    }

    [Fact] public void A_loc_file_becomes_tsv_and_builds_back_to_the_same_bytes()
    {
        var bytes = TestData.Loc(("k1", "Text\twith tab"), ("k2", ""));
        var file = EntryConversion.ToProjectFile("text/db/my_mod.loc", bytes, null);
        Assert.Equal(ProjectFileKind.Loc, file.Kind);
        Assert.Equal(bytes, EntryConversion.ToPackEntry(ProjectFileKind.Loc, "text/db/my_mod.loc", file.Bytes, null, NewGuid).Bytes);
    }

    [Fact] public void Tables_that_cannot_be_text_stay_binary_with_a_note()
    {
        var units = TestData.Units(("a", 1, 1f));
        Assert.Equal((ProjectFileKind.Raw, "no schema"), Kind(EntryConversion.ToProjectFile("db/units_tables/x", units, null)));
        var unknown = EntryConversion.ToProjectFile("db/other_tables/x", units, TestData.Schema);
        Assert.Equal(ProjectFileKind.Raw, unknown.Kind);
        Assert.Contains("TableNotInSchema", unknown.Note);
        var broken = EntryConversion.ToProjectFile("db/units_tables/x", units[..^1], TestData.Schema);
        Assert.Equal((ProjectFileKind.Raw, units.Length - 1), (broken.Kind, broken.Bytes.Length));
        Assert.False(string.IsNullOrWhiteSpace(broken.Note));
        var colours = DbTableEncoder.Encode(TestData.Schema.Find("colours_tables", 1)!, 1, TestData.HeaderGuid, [[257, 0, 0]]);
        var refused = EntryConversion.ToProjectFile("db/colours_tables/x", colours, TestData.Schema);
        Assert.Equal((ProjectFileKind.Raw, colours), (refused.Kind, refused.Bytes));
        Assert.False(string.IsNullOrWhiteSpace(refused.Note));
        Assert.Equal(ProjectFileKind.Raw, EntryConversion.ToProjectFile("db/units;tables/x", units, TestData.Schema).Kind);

        static (ProjectFileKind, string?) Kind(ProjectFileContent c) => (c.Kind, c.Note);
    }

    [Fact] public void Other_entries_and_broken_loc_files_stay_as_they_are()
    {
        var lua = TestData.Utf8("print('x')");
        Assert.Equal((ProjectFileKind.Raw, lua, null), Parts(EntryConversion.ToProjectFile("script/a.lua", lua, TestData.Schema)));
        var notLoc = EntryConversion.ToProjectFile("text/a.loc", lua, null);
        Assert.Equal((ProjectFileKind.Raw, lua), (notLoc.Kind, notLoc.Bytes));
        Assert.False(string.IsNullOrWhiteSpace(notLoc.Note));

        static (ProjectFileKind, byte[], string?) Parts(ProjectFileContent c) => (c.Kind, c.Bytes, c.Note);
    }

    [Fact] public void ToPackEntry_reports_problems_with_their_line()
    {
        var ok = "key\tcost\tspeed\n#units_tables;2;whatever/the/meta/says\t\t\na\t1\t1\n";
        Assert.NotNull(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/x", TestData.Utf8(ok), TestData.Schema, NewGuid).Bytes);
        Assert.NotNull(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/Units_Tables/x", TestData.Utf8("\uFEFF" + ok.Replace("\n", "\r\n")), TestData.Schema, NewGuid).Bytes);

        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/x", TestData.Utf8(ok.Replace("a\t1", "a\tone")), TestData.Schema, NewGuid), 3);
        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/other_tables/x", TestData.Utf8(ok), TestData.Schema, NewGuid), 2);
        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/x", TestData.Utf8(ok), null, NewGuid), 0);
        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/x", [0x6B, 0xFF, 0xFE], TestData.Schema, NewGuid), 0);
        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Loc, "text/a.loc", TestData.Utf8("key\ttext\n#Loc;1;p\t\n"), null, NewGuid), 1);

        static void Problem(PackEntryContent content, int line)
        {
            Assert.Null(content.Bytes);
            Assert.Equal(line, content.Line);
            Assert.False(string.IsNullOrWhiteSpace(content.Detail));
        }
    }
}

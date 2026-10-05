using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects.Tests;

public class NewFilesTests
{
    private static FieldDefinition Key => new("key", FieldType.StringU8, "StringU8", true, null);

    private static FieldDefinition Unsupported => new("weird", FieldType.Unsupported, "SomethingNew", false, null);

    // newest_tables lists its versions out of order; unsupported_tables is unsupported in its newest version only; the others cannot be created.
    private static readonly GameSchema Schema = new(new Dictionary<string, IReadOnlyList<TableDefinition>>
    {
        ["newest_tables"] = [new TableDefinition(1, [Key]), new TableDefinition(3, [Key, new("cost", FieldType.I32, "I32", false, null)]), new TableDefinition(2, [Key])],
        ["Mixed_Case_tables"] = [new TableDefinition(1, [Key])],
        ["old_unsupported_tables"] = [new TableDefinition(1, [Unsupported]), new TableDefinition(2, [Key])],
        ["unsupported_tables"] = [new TableDefinition(1, [Key]), new TableDefinition(2, [Key, Unsupported])],
        ["no_fields_tables"] = [new TableDefinition(1, [])],
        ["no_definition_tables"] = [],
        ["duplicate_tables"] = [new TableDefinition(1, [Key, Key])],
        ["semi;colon_tables"] = [new TableDefinition(1, [Key])],
        ["slash/tables"] = [new TableDefinition(1, [Key])],
    });

    public static TheoryData<string> BadNames() =>
    [
        string.Empty, " a", "a ", "a.b", "a/b", "a\\b", "..", "NUL", "a;b", "a\tb", "a\nb", "a\rb", "a:b", "a|b", "a" + (char)0xD800, new string('a', 101),
    ];

    [Fact] public void CreatableTables_leaves_out_tables_with_an_unsupported_field_in_the_newest_version()
    {
        var tables = NewFiles.CreatableTables(Schema);
        Assert.Equal(["Mixed_Case_tables", "newest_tables", "old_unsupported_tables"], tables);
        Assert.Equal(["units_tables"], NewFiles.CreatableTables(new GameSchema(new Dictionary<string, IReadOnlyList<TableDefinition>> { ["units_tables"] = TestData.Schema.Tables["units_tables"] })));
        Assert.Empty(NewFiles.CreatableTables(new GameSchema(new Dictionary<string, IReadOnlyList<TableDefinition>>())));
    }

    [Fact] public void CreatableTables_only_lists_tables_that_Table_can_create()
    {
        foreach (var name in NewFiles.CreatableTables(Schema))
        {
            Assert.NotNull(NewFiles.Table(Schema, name, "mine").File);
        }

        foreach (var name in new[] { "unsupported_tables", "no_fields_tables", "no_definition_tables", "duplicate_tables", "semi;colon_tables", "slash/tables" })
        {
            Assert.DoesNotContain(name, NewFiles.CreatableTables(Schema));
            Assert.NotNull(NewFiles.Table(Schema, name, "mine").Reason);
        }
    }

    [Fact] public void Table_writes_an_empty_table_in_the_newest_version_that_reads_back()
    {
        var result = NewFiles.Table(Schema, "newest_tables", "my_mod");
        Assert.Null(result.Reason);
        var file = result.File!;
        Assert.Equal((@"db\newest_tables\my_mod.tsv", ProjectFileKind.Table, null), (file.RelativeFile, file.Kind, file.Note));
        Assert.NotEqual(0xEF, file.Content[0]);

        var text = new UTF8Encoding(false, true).GetString(file.Content);
        Assert.Equal("key\tcost\n#newest_tables;3;db/newest_tables/my_mod\t\n", text);
        var read = TableTsv.Read(text, Schema);
        Assert.NotNull(read.Table);
        Assert.Equal(("newest_tables", 3, "db/newest_tables/my_mod", 0), (read.Table.TableName, read.Table.Version, read.Table.PackPath, read.Table.Rows.Count));

        // An older version is the newest one only when it has the highest number, whatever the order of the list.
        Assert.Contains("#old_unsupported_tables;2;", new UTF8Encoding(false).GetString(NewFiles.Table(Schema, "old_unsupported_tables", "a").File!.Content));
    }

    [Fact] public void Table_takes_the_spelling_of_the_schema_key_and_refuses_another_spelling()
    {
        var file = NewFiles.Table(Schema, "Mixed_Case_tables", "a").File!;
        Assert.Equal(@"db\Mixed_Case_tables\a.tsv", file.RelativeFile);
        Assert.Contains("#Mixed_Case_tables;1;db/Mixed_Case_tables/a", Encoding.UTF8.GetString(file.Content));

        Assert.NotNull(NewFiles.Table(Schema, "mixed_case_tables", "a").Reason);
        Assert.NotNull(NewFiles.Table(Schema, "unknown_tables", "a").Reason);
        Assert.Null(NewFiles.Table(Schema, "unknown_tables", "a").File);
    }

    [Fact] public void Loc_writes_an_empty_loc_file_that_reads_back()
    {
        var result = NewFiles.Loc("my_mod");
        Assert.Null(result.Reason);
        var file = result.File!;
        Assert.Equal((@"text\db\my_mod.loc.tsv", ProjectFileKind.Loc, null), (file.RelativeFile, file.Kind, file.Note));
        Assert.Equal((ProjectFileKind.Loc, "text/db/my_mod.loc"), (ProjectLayout.FromRelativeFile(file.RelativeFile).Kind, ProjectLayout.FromRelativeFile(file.RelativeFile).PackPath));

        var read = TableTsv.ReadLoc(new UTF8Encoding(false, true).GetString(file.Content));
        Assert.NotNull(read.Loc);
        Assert.Equal(("text/db/my_mod.loc", 0), (read.Loc.PackPath, read.Loc.Rows.Count));
        Assert.Equal("text/db", NewFiles.LocFolder);
    }

    [Theory, MemberData(nameof(BadNames))]
    public void Every_bad_name_has_a_reason(string name)
    {
        Assert.False(string.IsNullOrWhiteSpace(NewFiles.NameProblem(name)));
        var table = NewFiles.Table(Schema, "newest_tables", name);
        Assert.Null(table.File);
        Assert.False(string.IsNullOrWhiteSpace(table.Reason));
        var loc = NewFiles.Loc(name);
        Assert.Null(loc.File);
        Assert.False(string.IsNullOrWhiteSpace(loc.Reason));
        Assert.Null(NewFiles.Adopt("db/units_tables/x", TestData.Units(("a", 1, 1f)), TestData.Schema, name).File);
    }

    [Fact] public void Good_names_have_no_problem()
    {
        foreach (var name in new[] { "a", "my mod", "ünit", "a-b_c(1)", new string('a', 100), "NULL1", "comfort" })
        {
            Assert.Null(NewFiles.NameProblem(name));
        }
    }

    [Fact] public void Adopt_of_a_table_gets_the_new_name_in_the_path_and_in_the_meta_line()
    {
        var result = NewFiles.Adopt("db/units_tables/data__", TestData.Units(("a", 1, 1f)), TestData.Schema, "my_mod");
        Assert.Null(result.Reason);
        var file = result.File!;
        Assert.Equal((@"db\units_tables\my_mod.tsv", ProjectFileKind.Table, null), (file.RelativeFile, file.Kind, file.Note));
        var text = Encoding.UTF8.GetString(file.Content);
        Assert.Equal("key\tcost\tspeed\n#units_tables;2;db/units_tables/my_mod\t\t\na\t1\t1\n", text);

        var kept = NewFiles.Adopt("db/units_tables/data__", TestData.Units(("a", 1, 1f)), TestData.Schema, null).File!;
        Assert.Equal(@"db\units_tables\data__.tsv", kept.RelativeFile);
        Assert.Contains("#units_tables;2;db/units_tables/data__", Encoding.UTF8.GetString(kept.Content));
    }

    [Fact] public void Adopt_of_a_loc_file_keeps_its_folder_and_gets_the_new_name()
    {
        var bytes = TestData.Loc(("k1", "text"));
        var file = NewFiles.Adopt("text/de/db/old.loc", bytes, null, "new").File!;
        Assert.Equal((@"text\de\db\new.loc.tsv", ProjectFileKind.Loc, null), (file.RelativeFile, file.Kind, file.Note));
        Assert.Contains("#Loc;1;text/de/db/new.loc", Encoding.UTF8.GetString(file.Content));

        Assert.Equal(@"text\de\db\old.loc.tsv", NewFiles.Adopt("text/de/db/old.loc", bytes, null, null).File!.RelativeFile);
        // The extension keeps the spelling of the entry.
        Assert.Equal(@"new.LOC.tsv", NewFiles.Adopt("old.LOC", bytes, null, "new").File!.RelativeFile);
    }

    [Fact] public void Adopt_of_another_file_keeps_its_path_and_bytes_and_refuses_a_new_name()
    {
        var lua = TestData.Utf8("print('x')");
        var file = NewFiles.Adopt("script/a.lua", lua, TestData.Schema, null).File!;
        Assert.Equal((@"script\a.lua", ProjectFileKind.Raw, null), (file.RelativeFile, file.Kind, file.Note));
        Assert.Equal(lua, file.Content);

        var renamed = NewFiles.Adopt("script/a.lua", lua, TestData.Schema, "b");
        Assert.Null(renamed.File);
        Assert.False(string.IsNullOrWhiteSpace(renamed.Reason));
    }

    [Fact] public void An_entry_the_dialect_cannot_carry_is_adopted_as_a_binary_file_with_a_note()
    {
        var colours = DbTableEncoder.Encode(TestData.Schema.Find("colours_tables", 1)!, 1, TestData.HeaderGuid, [[257, 0, 0]]);
        var refused = NewFiles.Adopt("db/colours_tables/x", colours, TestData.Schema, "y").File!;
        Assert.Equal((@"db\colours_tables\y", ProjectFileKind.Raw, colours), (refused.RelativeFile, refused.Kind, refused.Content));
        Assert.False(string.IsNullOrWhiteSpace(refused.Note));

        var units = TestData.Units(("a", 1, 1f));
        var noSchema = NewFiles.Adopt("db/units_tables/x", units, null, "y").File!;
        Assert.Equal((@"db\units_tables\y", ProjectFileKind.Raw, units, "no schema"), (noSchema.RelativeFile, noSchema.Kind, noSchema.Content, noSchema.Note));
    }

    [Fact] public void Adopt_refuses_an_unsafe_path_and_a_name_that_collides_with_the_text_form()
    {
        var units = TestData.Units(("a", 1, 1f));
        var lua = TestData.Utf8("x");

        Assert.False(string.IsNullOrWhiteSpace(NewFiles.Adopt("script/a:b.lua", lua, null, null).Reason));
        Assert.False(string.IsNullOrWhiteSpace(NewFiles.Adopt("script/../a.lua", lua, null, null).Reason));
        Assert.False(string.IsNullOrWhiteSpace(NewFiles.Adopt("db/units_tables/NUL", units, TestData.Schema, null).Reason));

        // In the pack the entry is already called like a text file of the project: kept, it would be taken for a table or loc file.
        Assert.False(string.IsNullOrWhiteSpace(NewFiles.Adopt("db/units_tables/x.tsv", units, TestData.Schema, null).Reason));
        Assert.False(string.IsNullOrWhiteSpace(NewFiles.Adopt("text/a.loc.tsv", lua, null, null).Reason));
        Assert.Null(NewFiles.Adopt("db/units_tables/x.tsv", units, TestData.Schema, null).File);

        // A new name gets it out of the collision.
        Assert.Equal(@"db\units_tables\x.tsv", NewFiles.Adopt("db/units_tables/x.tsv", units, TestData.Schema, "x").File!.RelativeFile);
    }

    [Fact] public void AdoptName_tells_whether_the_entry_gets_a_name_and_which()
    {
        Assert.Equal((true, "data__"), NewFiles.AdoptName("db/units_tables/data__"));
        Assert.Equal((true, "x"), NewFiles.AdoptName("text/db/x.loc"));
        Assert.Equal((true, "x"), NewFiles.AdoptName("x.LOC"));
        Assert.Equal((false, "a.lua"), NewFiles.AdoptName("script/a.lua"));
        Assert.Equal((false, "a"), NewFiles.AdoptName("a"));
    }

    [Fact] public void What_Adopt_writes_builds_back_to_the_entry()
    {
        var units = TestData.Units(("unit_a", 100, 0.1f), ("ünit_b", -5, 2.0000002E-06f));
        var table = NewFiles.Adopt("db/units_tables/old", units, TestData.Schema, "mine").File!;
        var (packPath, kind) = ProjectLayout.FromRelativeFile(table.RelativeFile);
        Assert.Equal(("db/units_tables/mine", ProjectFileKind.Table), (packPath, kind));
        // The GUID of the header is new on every build; the tests give the one of the entry back.
        Assert.Equal(units, EntryConversion.ToPackEntry(kind, packPath, table.Content, TestData.Schema, () => TestData.HeaderGuid).Bytes);

        var loc = TestData.Loc(("k1", "Text\twith tab"), ("k2", ""));
        var locFile = NewFiles.Adopt("text/db/old.loc", loc, null, "new").File!;
        var (locPath, locKind) = ProjectLayout.FromRelativeFile(locFile.RelativeFile);
        Assert.Equal(loc, EntryConversion.ToPackEntry(locKind, locPath, locFile.Content, null, () => TestData.HeaderGuid).Bytes);

        // Also what Table and Loc create builds to a valid, empty entry.
        var empty = NewFiles.Table(Schema, "newest_tables", "a").File!;
        var (emptyPath, emptyKind) = ProjectLayout.FromRelativeFile(empty.RelativeFile);
        Assert.NotNull(EntryConversion.ToPackEntry(emptyKind, emptyPath, empty.Content, Schema, () => TestData.HeaderGuid).Bytes);
    }
}

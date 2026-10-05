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
        "~$x", "~$", "a" + (char)0x202E + "b", "a" + (char)0x200B, "a" + (char)0x85 + "b", (char)0x9F + "a", "a" + (char)0xDC00 + "b", "a" + (char)0x00 + "b",
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
            Assert.Equal(NameIssue.None, NewFiles.NameIssueOf(name));
        }
    }

    // Every row of BadNames with the kind of its problem.
    public static TheoryData<string, NameIssue> BadNameIssues() => new()
    {
        { string.Empty, NameIssue.Empty },
        { " a", NameIssue.LeadingOrTrailingSpace },
        { "a ", NameIssue.LeadingOrTrailingSpace },
        { "a.b", NameIssue.Dot },
        { "a/b", NameIssue.Separator },
        { "a\\b", NameIssue.Separator },
        { "..", NameIssue.Dot },
        { "NUL", NameIssue.ReservedName },
        { "a;b", NameIssue.Semicolon },
        { "a\tb", NameIssue.TabOrLineBreak },
        { "a\nb", NameIssue.TabOrLineBreak },
        { "a\rb", NameIssue.TabOrLineBreak },
        { "a:b", NameIssue.InvalidCharacter },
        { "a|b", NameIssue.InvalidCharacter },
        { "a" + (char)0xD800, NameIssue.UnpairedSurrogate },
        { new string('a', 101), NameIssue.TooLong },
        { "~$x", NameIssue.Ignored },
        { "~$", NameIssue.Ignored },
        { "a" + (char)0x202E + "b", NameIssue.InvisibleCharacter },
        { "a" + (char)0x200B, NameIssue.InvisibleCharacter },
        { "a" + (char)0x85 + "b", NameIssue.InvisibleCharacter },
        { (char)0x9F + "a", NameIssue.InvisibleCharacter },
        { "a" + (char)0xDC00 + "b", NameIssue.UnpairedSurrogate },
        { "a" + (char)0x00 + "b", NameIssue.InvalidCharacter },
    };

    [Theory, MemberData(nameof(BadNameIssues))]
    public void Every_bad_name_has_the_kind_of_its_problem_and_NameProblem_agrees(string name, NameIssue expected)
    {
        Assert.Equal(expected, NewFiles.NameIssueOf(name));
        Assert.NotNull(NewFiles.NameProblem(name));
    }

    // The name part of a file that is neither a table nor a loc file, with the extension ".lua" it keeps.
    public static TheoryData<string, NameIssue> PlainNameIssues() => new()
    {
        { string.Empty, NameIssue.Empty },
        { "a/b", NameIssue.Separator },
        { "a\\b", NameIssue.Separator },
        { " a", NameIssue.LeadingOrTrailingSpace },
        { "a ", NameIssue.LeadingOrTrailingSpace },
        { "a" + (char)0xD800, NameIssue.UnpairedSurrogate },
        { "a|b", NameIssue.InvalidCharacter },
        { "a:b", NameIssue.InvalidCharacter },
        { "a\tb", NameIssue.InvalidCharacter },
        { new string('a', 252), NameIssue.TooLong },
        { "NUL", NameIssue.ReservedName },
        { "con", NameIssue.ReservedName },
        { "~$x", NameIssue.Ignored },
        { "a.b", NameIssue.None },
        { "my file", NameIssue.None },
        { new string('a', 251), NameIssue.None },
    };

    [Theory, MemberData(nameof(PlainNameIssues))]
    public void A_plain_file_name_has_the_kind_of_its_problem(string name, NameIssue expected)
    {
        Assert.Equal(expected, NewFiles.PlainNameIssueOf(name, ".lua"));
    }

    [Fact] public void A_plain_file_name_is_checked_with_its_extension()
    {
        Assert.Equal(NameIssue.Ignored, NewFiles.PlainNameIssueOf("x", ".bak"));
        Assert.Equal(NameIssue.None, NewFiles.PlainNameIssueOf("x", ".lua"));
        Assert.Equal(255 - 4, NewFiles.MaxPlainNameLength(".lua"));
    }

    [Fact] public void The_issue_table_covers_every_bad_name()
    {
        var mapped = ((IEnumerable<object[]>)BadNameIssues()).Select(row => (string)row[0]).ToList();
        Assert.Equal(((IEnumerable<object[]>)BadNames()).Select(row => (string)row[0]), mapped);
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

    [Fact] public void Adopt_refuses_a_file_the_project_ignores_and_a_path_that_is_not_one()
    {
        var lua = TestData.Utf8("x");
        foreach (var path in new[] { "script/a.bak", "script/a.tmp", "script/a~", "script/~$a.lua", "script/.~lock.a.lua#", "", "db/t/", "db//x" })
        {
            var result = NewFiles.Adopt(path, lua, null, null);
            Assert.Null(result.File);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }

        Assert.Null(NewFiles.Adopt("script/a.lua", lua, null, null).Reason);
    }

    [Fact] public void A_new_name_that_a_project_would_ignore_is_refused_everywhere()
    {
        Assert.Contains("ignores", NewFiles.NameProblem("~$x"));
        Assert.Contains("ignores", NewFiles.Table(Schema, "newest_tables", "~$x").Reason);
        Assert.Contains("ignores", NewFiles.Loc("~$x").Reason);
        Assert.Null(NewFiles.Adopt("db/units_tables/x", TestData.Units(("a", 1, 1f)), TestData.Schema, "~$x").File);
        Assert.Null(NewFiles.Adopt("text/db/x.loc", TestData.Loc(("k", "v")), null, "~$x").File);
    }

    [Fact] public void Every_file_that_is_offered_can_be_resolved_for_writing()
    {
        using var dir = new TempDir();
        var units = TestData.Units(("a", 1, 1f));
        var colours = DbTableEncoder.Encode(TestData.Schema.Find("colours_tables", 1)!, 1, TestData.HeaderGuid, [[257, 0, 0]]);
        var files = new[]
        {
            NewFiles.Table(Schema, "newest_tables", "mine").File!,
            NewFiles.Table(Schema, "newest_tables", new string('a', 100)).File!,
            NewFiles.Loc("mine").File!,
            NewFiles.Loc(new string('a', 100)).File!,
            NewFiles.Adopt("db/units_tables/old", units, TestData.Schema, "mine").File!,
            NewFiles.Adopt("db/colours_tables/old", colours, TestData.Schema, "mine").File!,
            NewFiles.Adopt("text/db/old.loc", TestData.Loc(("k", "v")), null, "mine").File!,
            NewFiles.Adopt("script/a.lua", TestData.Utf8("x"), null, null).File!,
        };
        foreach (var file in files)
        {
            var check = PackFolderPaths.ResolveForWrite(dir.Path, file.RelativeFile);
            Assert.Equal(PackPathProblem.None, check.Problem);
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(dir.Path));
    }

    [Fact] public void A_name_of_100_characters_works_for_every_listed_table_and_one_more_is_refused()
    {
        // db/<table>/<100 characters>.tsv is 259 characters for a table name of 151 characters.
        var longest = new string('t', 151);
        var tooLong = new string('t', 152);
        var schema = new GameSchema(new Dictionary<string, IReadOnlyList<TableDefinition>>
        {
            [longest] = [new TableDefinition(1, [Key])],
            [tooLong] = [new TableDefinition(1, [Key])],
        });

        Assert.Equal([longest], NewFiles.CreatableTables(schema));
        using var dir = new TempDir();
        var file = NewFiles.Table(schema, longest, new string('a', 100)).File!;
        Assert.Equal(PackPathProblem.None, PackFolderPaths.ResolveForWrite(dir.Path, file.RelativeFile).Problem);
        // Not listed, because it would not take every allowed name; with a short name it still works, a name of 100 characters does not.
        Assert.Null(NewFiles.Table(schema, tooLong, "a").Reason);
        Assert.NotNull(NewFiles.Table(schema, tooLong, new string('a', 100)).Reason);
        Assert.Null(NewFiles.NameProblem(new string('a', 100)));
        Assert.NotNull(NewFiles.NameProblem(new string('a', 101)));
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

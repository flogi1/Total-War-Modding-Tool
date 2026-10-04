namespace TotalWarModTool.Projects.Tests;

public class ProjectLayoutTests
{
    [Theory]
    [InlineData("db/units_tables/my_mod", ProjectFileKind.Table, @"db\units_tables\my_mod.tsv")]
    [InlineData("text/db/my_mod.loc", ProjectFileKind.Loc, @"text\db\my_mod.loc.tsv")]
    [InlineData("script/a.lua", ProjectFileKind.Raw, @"script\a.lua")]
    [InlineData("db/units_tables/binary", ProjectFileKind.Raw, @"db\units_tables\binary")]
    public void ToRelativeFile_and_back(string packPath, ProjectFileKind kind, string file)
    {
        Assert.Equal(file, ProjectLayout.ToRelativeFile(packPath, kind));
        Assert.Equal((packPath, kind), ProjectLayout.FromRelativeFile(file));
    }

    [Theory]
    [InlineData("DB/Units_Tables/X.TSV", "DB/Units_Tables/X", ProjectFileKind.Table)]
    [InlineData("db/units_tables/sub/x.tsv", "db/units_tables/sub/x.tsv", ProjectFileKind.Raw)]
    [InlineData("db/x.tsv", "db/x.tsv", ProjectFileKind.Raw)]
    [InlineData("db/units_tables/.tsv", "db/units_tables/.tsv", ProjectFileKind.Raw)]
    [InlineData("notes/table.tsv", "notes/table.tsv", ProjectFileKind.Raw)]
    [InlineData("text/X.LOC.TSV", "text/X.LOC", ProjectFileKind.Loc)]
    [InlineData("text/.loc.tsv", "text/.loc.tsv", ProjectFileKind.Raw)]
    public void FromRelativeFile_recognises_text_forms_only_where_the_rule_says(string file, string packPath, ProjectFileKind kind) =>
        Assert.Equal((packPath, kind), ProjectLayout.FromRelativeFile(file));

    [Fact] public void UnsafePathReason_rejects_the_superscript_device_names()
    {
        foreach (var name in new[] { "COM" + (char)0xB9, "COM" + (char)0xB2, "COM" + (char)0xB3, "lpt" + (char)0xB9 + ".txt", "LPT" + (char)0xB2, "lpt" + (char)0xB3 + ".txt" })
        {
            Assert.False(string.IsNullOrWhiteSpace(ProjectLayout.UnsafePathReason("a/" + name)), name);
        }
    }

    [Fact] public void FromRelativeFile_takes_a_table_only_where_DbTablePath_sees_one()
    {
        Assert.Equal(("db//x.tsv", ProjectFileKind.Raw), ProjectLayout.FromRelativeFile("db//x.tsv"));
        Assert.Equal(("db/units_tables/x", ProjectFileKind.Table), ProjectLayout.FromRelativeFile(@"db\units_tables\x.tsv"));
    }

    [Fact] public void LooksLikeTextForm_follows_FromRelativeFile()
    {
        Assert.True(ProjectLayout.LooksLikeTextForm("db/units_tables/x.tsv"));
        Assert.True(ProjectLayout.LooksLikeTextForm("text/a.loc.tsv"));
        Assert.False(ProjectLayout.LooksLikeTextForm("db/units_tables/x"));
    }

    [Theory]
    [InlineData("")] [InlineData("../x")] [InlineData("a/../../x")] [InlineData("a//b")] [InlineData("/a")] [InlineData("a/")] [InlineData("./a")]
    [InlineData(@"a\b")] [InlineData("C:/x")] [InlineData("a/b:c")] [InlineData("a/b?")] [InlineData("a/b\tc")] [InlineData("a/con")] [InlineData("a/NUL.txt")]
    [InlineData("a/lpt9.x.y")] [InlineData("a/b.")] [InlineData("a/b ")] [InlineData("a /b")]
    [InlineData("a/CONIN$")] [InlineData("a/conout$.x")]
    public void UnsafePathReason_rejects(string packPath) => Assert.False(string.IsNullOrWhiteSpace(ProjectLayout.UnsafePathReason(packPath)));

    [Fact] public void UnsafePathReason_accepts_ordinary_paths_and_limits_segment_length()
    {
        Assert.Null(ProjectLayout.UnsafePathReason("db/units_tables/!my mod_ü日本"));
        Assert.Null(ProjectLayout.UnsafePathReason("a/console.txt"));
        Assert.Null(ProjectLayout.UnsafePathReason("a/" + new string('x', 255)));
        Assert.NotNull(ProjectLayout.UnsafePathReason("a/" + new string('x', 256)));
        Assert.NotNull(ProjectLayout.UnsafePathReason("a/" + (char)0xD800));
    }
}

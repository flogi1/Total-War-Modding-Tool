namespace TotalWarModTool.Localization.Tests;

public class LanguageCatalogTests
{
    [Fact] public void Find_lists_english_first_then_sorted_and_skips_underscore_files()
    {
        using var dir = new TempDir();
        foreach (var f in new[] { "pl.json", "DE.json", "_glossary.json", "notes.txt" }) File.WriteAllText(dir.File(f), "{}");
        Assert.Equal(["en", "de", "pl"], LanguageCatalog.Find(dir.Path).Select(o => o.Code));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("de", "Deutsch")]
    [InlineData("xx-nope", "xx-nope")]
    public void DisplayName_is_native_name(string code, string expected) => Assert.Equal(expected, LanguageCatalog.DisplayName(code));
}
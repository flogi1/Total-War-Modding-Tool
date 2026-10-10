namespace TotalWarModTool.App.Tests;

public class AppOutputTests
{
    // The content of the app project flows into the output folder of this test project.
    [Theory]
    [InlineData(@"profiles\warhammer_3.json")]
    [InlineData(@"profiles\warhammer_3\objects\building_levels_tables.json")]
    [InlineData(@"schemas\schema_wh3.ron")]
    [InlineData(@"schemas\LICENSE")]
    [InlineData(@"schemas\README.md")]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    [InlineData("LICENSE")]
    [InlineData(@"locales\en.json")]
    public void Shipped_file_is_in_the_output_folder(string relativePath) =>
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, relativePath)), relativePath);
}

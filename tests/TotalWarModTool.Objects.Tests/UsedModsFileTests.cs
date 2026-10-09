namespace TotalWarModTool.Objects.Tests;

public class UsedModsFileTests
{
    [Fact]
    public void Parse_reads_directories_and_mods_in_file_order()
    {
        const string text = "add_working_directory \"D:\\SteamLibrary\\steamapps\\workshop\\content\\1142710\\3807929920\";\r\n"
            + "add_working_directory \"D:\\SteamLibrary\\steamapps\\workshop\\content\\1142710\\2859802328\";\r\n"
            + "mod \"end_times_mp_narrative_fix.pack\";\r\n"
            + "mod \"QueBatAutResv1.pack\";\r\n";

        var result = UsedModsFile.Parse(text);

        Assert.Equal(
            [@"D:\SteamLibrary\steamapps\workshop\content\1142710\3807929920", @"D:\SteamLibrary\steamapps\workshop\content\1142710\2859802328"],
            result.WorkingDirectories);
        Assert.Equal(["end_times_mp_narrative_fix.pack", "QueBatAutResv1.pack"], result.ModNames);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void Parse_ignores_unknown_lines_blank_lines_and_missing_semicolons(string newline)
    {
        var text = string.Join(newline, "", "# x", "mod \"a.pack\"", "mod a.pack", "unknown \"b.pack\";", "");

        var result = UsedModsFile.Parse(text);

        Assert.Equal(["a.pack"], result.ModNames);
        Assert.Empty(result.WorkingDirectories);
    }
}

using System.Text;

namespace TotalWarModTool.Localization.Tests;

public class LocaleFileReaderTests
{
    [Fact] public void Read_reports_error_for_non_utf8_file()
    {
        using var dir = new TempDir();
        // "Für" saved as ANSI (Windows-1252 / Latin-1): the ü is the single byte 0xFC, which is not valid UTF-8.
        File.WriteAllBytes(dir.File("de.json"), Encoding.Latin1.GetBytes("{\n\"a\":\"Für\"}"));

        var file = LocaleFileReader.Read(dir.File("de.json"));

        Assert.Empty(file.Texts);
        Assert.NotNull(file.Error);
        Assert.Equal((2L, 7L), (file.Error.Line, file.Error.Position));
        Assert.Contains("UTF-8", file.Error.Message);
    }

    [Theory]
    [InlineData("{\"a\":\"x\\ud800y\"}")]      // lone high surrogate escape in a text
    [InlineData("{\"x\\udc00y\":\"b\"}")]      // lone low surrogate escape in a key
    public void Read_reports_error_for_unreadable_json_string(string json)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("de.json"), json);

        var file = LocaleFileReader.Read(dir.File("de.json"));

        Assert.Empty(file.Texts);
        Assert.NotNull(file.Error);
        Assert.Equal((1L, 1L), (file.Error.Line, file.Error.Position));
        Assert.False(string.IsNullOrWhiteSpace(file.Error.Message));
    }

    [Fact] public void Read_accepts_utf8_file_with_bom()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(dir.File("de.json"), [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("{\"a\":\"Für\"}")]);

        var file = LocaleFileReader.Read(dir.File("de.json"));

        Assert.Null(file.Error);
        Assert.Equal("Für", file.Texts["a"]);
    }
}

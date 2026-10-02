namespace TotalWarModTool.Localization.Tests;

public class LocaleCheckTests
{
    private sealed class SampleTexts(UiText ui) : TextsBase(ui)
    {
        public string Title => T("sample.title", "Title");
        public string Count(int done, int total) => F("sample.count", "{0} / {1}", done, total);
    }

    private static IReadOnlyList<string> Check(string en, string de)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), en); File.WriteAllText(dir.File("de.json"), de);
        return LocaleCheck.FindProblems([new SampleTexts(new UiText(dir.Path))], dir.Path, ["en", "de"]);
    }

    private const string GoodEn = "{\"sample.title\":\"Title\",\"sample.count\":\"{0} / {1}\"}";
    private const string GoodDe = "{\"sample.title\":\"Titel\",\"sample.count\":\"{0} / {1}\",\"_comment\":\"ok\"}";

    [Fact] public void Complete_files_have_no_problems() => Assert.Empty(Check(GoodEn, GoodDe));

    [Fact] public void Reports_every_problem_kind()
    {
        var problems = Check(
            "{\"sample.title\":\"Heading\",\"sample.count\":\"{0} / {1}\",\"old.key\":\"x\"}",
            "{\"sample.title\":\"\",\"sample.count\":\"{0} von {2}\"}");
        Assert.Equal(
            ["fallback-differs:sample.title", "missing:de:sample.title", "placeholders:de:sample.count", "unused:en:old.key"],
            problems.Order());
    }

    [Fact] public void Reports_invalid_json() => Assert.Contains("json:de:1:3", Check(GoodEn, "{ x"));

    [Fact] public void Reports_non_utf8_file_as_json_problem()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), GoodEn);
        File.WriteAllBytes(dir.File("de.json"), System.Text.Encoding.Latin1.GetBytes("{\"sample.title\":\"Tür\"}"));   // ANSI: the ue is the single byte 0xFC
        var problems = LocaleCheck.FindProblems([new SampleTexts(new UiText(dir.Path))], dir.Path, ["en", "de"]);
        Assert.Equal(["json:de:1:19"], problems);
    }
}
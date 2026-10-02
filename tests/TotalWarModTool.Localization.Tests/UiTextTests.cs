namespace TotalWarModTool.Localization.Tests;

public class UiTextTests
{
    private const string En = "{\"a\":\"A-en\",\"b\":\"B-en\",\"n\":\"{0} of {1}\",\"_note\":\"comment\"}";
    private const string De = "{\"a\":\"A-de\",\"b\":\"\",\"n\":\"{0} von {5}\"}";

    private static string Locales(TempDir dir, params (string Code, string Json)[] files)
    {
        foreach (var (code, json) in files)
        {
            File.WriteAllText(dir.File(code + ".json"), json);
        }

        return dir.Path;
    }

    [Fact] public void Before_Load_returns_inline_fallback_and_language_en()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En)));
        Assert.Equal(("en", "Fallback"), (ui.Language, ui.Get("a", "Fallback")));
    }

    [Fact] public void Chain_is_language_then_english_then_inline()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", De)));
        ui.Load(" DE ");
        Assert.Equal("de", ui.Language);
        Assert.Equal("A-de", ui.Get("a", "x"));          // from de
        Assert.Equal("B-en", ui.Get("b", "x"));          // empty in de -> en
        Assert.Equal("x", ui.Get("missing", "x"));       // nowhere -> inline
        Assert.Equal("x", ui.Get("_note", "x"));         // comment keys are not texts
    }

    [Fact] public void Format_falls_back_when_pattern_is_broken()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", De)));
        ui.Load("de");
        Assert.Equal("3 of 9", ui.Format("n", "{0}/{1}", 3, 9));     // de pattern needs {5} -> en
    }

    [Fact] public void Load_of_missing_language_uses_english()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En)));
        ui.Load("fr");
        Assert.Equal("A-en", ui.Get("a", "x"));
        Assert.Null(ui.LoadError);
    }

    [Fact] public void Load_of_invalid_json_reports_error_and_uses_english()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", "{\n \"a\": \"A-de\",\n oops")));
        ui.Load("de");
        Assert.Equal("A-en", ui.Get("a", "x"));
        Assert.Equal(3, ui.LoadError!.Line);
    }

    [Fact] public void Load_of_non_utf8_file_reports_error_and_uses_english()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En)));
        File.WriteAllBytes(dir.File("de.json"), System.Text.Encoding.Latin1.GetBytes("{\"a\":\"Für\"}"));   // ANSI: the ue is the single byte 0xFC
        ui.Load("de");
        Assert.Equal("de", ui.Language);
        Assert.Equal("A-en", ui.Get("a", "x"));
        Assert.NotNull(ui.LoadError);
    }

    [Fact] public void Load_of_file_with_unreadable_json_string_reports_error_and_uses_english()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", "{\"a\":\"x\\ud800y\"}")));   // lone surrogate escape
        ui.Load("de");
        Assert.Equal("A-en", ui.Get("a", "x"));
        Assert.NotNull(ui.LoadError);
    }

    [Fact] public void Load_raises_Changed_and_null_means_english()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", De)));
        var raised = 0; ui.Changed += (_, _) => raised++;
        ui.Load("de"); ui.Load(null);
        Assert.Equal((2, "en"), (raised, ui.Language));
    }
}
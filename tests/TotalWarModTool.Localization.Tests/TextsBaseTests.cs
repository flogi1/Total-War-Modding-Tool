namespace TotalWarModTool.Localization.Tests;

public class TextsBaseTests
{
    private sealed class SampleTexts(UiText ui) : TextsBase(ui)
    {
        public string Title => T("sample.title", "Title");
        public string Count(int done, int total) => F("sample.count", "{0} / {1}", done, total);
    }

    [Fact] public void Reads_texts_and_records_requested_keys()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), "{\"sample.title\":\"Title\"}");
        var texts = new SampleTexts(new UiText(dir.Path));
        Assert.Equal(("Title", "3 / 9"), (texts.Title, texts.Count(3, 9)));
        Assert.Equal(new Dictionary<string, string> { ["sample.title"] = "Title", ["sample.count"] = "{0} / {1}" }, texts.RequestedKeys);
    }

    [Fact] public void Raises_PropertyChanged_for_all_properties_when_language_changes()
    {
        using var dir = new TempDir();
        var ui = new UiText(dir.Path);
        var texts = new SampleTexts(ui);
        var names = new List<string?>(); texts.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        ui.Load("de");
        Assert.Equal([""], names);
    }
}
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

    private sealed class ManyTexts(UiText ui) : TextsBase(ui)
    {
        public string Text(string key) => T(key, "fallback of " + key);
        public string Formatted(string key) => F(key, "{0} of " + key, 1);
    }

    // Later plans read texts from Task.Run work while the UI thread reads the same instance. Writers insert distinct and identical keys at the
    // same time, a reader keeps walking RequestedKeys: nothing may throw and no key may be lost.
    [Fact] public async Task Concurrent_reads_record_every_key_and_RequestedKeys_stays_readable()
    {
        const int writers = 8, keysPerWriter = 1000, sharedKeys = 50;
        var texts = new ManyTexts(new UiText(@"X:\none"));
        using var start = new ManualResetEventSlim();
        var writersDone = false;

        var writing = Enumerable.Range(0, writers).Select(writer => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < keysPerWriter; i++)
            {
                texts.Text($"own.{writer}.{i}");
                texts.Formatted($"formatted.{writer}.{i}");
                texts.Text($"shared.{i % sharedKeys}");
            }
        })).ToArray();

        var reading = Task.Run(() =>
        {
            start.Wait();
            while (!Volatile.Read(ref writersDone))
            {
                foreach (var _ in texts.RequestedKeys)
                {
                }
            }
        });

        start.Set();
        var everything = Task.WhenAll(writing);
        var finished = await Task.WhenAny(everything, Task.Delay(TimeSpan.FromSeconds(30))) == everything;
        Volatile.Write(ref writersDone, true);
        Assert.True(finished, "The writers did not finish within 30 seconds.");
        await everything;                   // rethrows what a writer threw
        await reading.WaitAsync(TimeSpan.FromSeconds(30));      // rethrows what the reader threw

        var keys = texts.RequestedKeys;
        Assert.Equal(writers * keysPerWriter * 2 + sharedKeys, keys.Count);
        for (var writer = 0; writer < writers; writer++)
        {
            for (var i = 0; i < keysPerWriter; i++)
            {
                Assert.Equal($"fallback of own.{writer}.{i}", keys[$"own.{writer}.{i}"]);
                Assert.Equal($"{{0}} of formatted.{writer}.{i}", keys[$"formatted.{writer}.{i}"]);
            }
        }

        for (var i = 0; i < sharedKeys; i++)
        {
            Assert.Equal($"fallback of shared.{i}", keys[$"shared.{i}"]);
        }
    }
}
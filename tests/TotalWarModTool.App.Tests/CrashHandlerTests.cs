namespace TotalWarModTool.App.Tests;

public class CrashHandlerTests
{
    private static CrashHandler Create(FakeAppLog log, Action<string> show) =>
        new(log, new FakeTimeProvider(DateTimeOffset.UnixEpoch), "0.1.0", null, show);

    [Fact] public void Handle_logs_and_shows_dialog_with_details()
    {
        var log = new FakeAppLog(); var shown = new List<string>();
        Create(log, shown.Add).Handle(new IOException("disk"), CrashHandler.SourceTask);
        Assert.Single(log.Errors);
        Assert.Contains("Source: task", Assert.Single(shown)); Assert.Contains("System.IO.IOException: disk", shown[0]);
    }

    [Fact] public void Handle_while_dialog_open_only_logs()
    {
        var log = new FakeAppLog(); var shows = 0; CrashHandler? handler = null;
        handler = Create(log, _ => { shows++; handler!.Handle(new Exception("second"), CrashHandler.SourceUi); });
        handler.Handle(new Exception("first"), CrashHandler.SourceUi);
        Assert.Equal((1, 2), (shows, log.Errors.Count));
        handler.Handle(new Exception("third"), CrashHandler.SourceUi);      // dialog closed again -> shown again
        Assert.Equal(2, shows);
    }

    [Fact] public void Handle_survives_a_failing_dialog()
    {
        var log = new FakeAppLog();
        Create(log, _ => throw new InvalidOperationException("no ui")).Handle(new Exception("x"), CrashHandler.SourceAppDomain);
        Assert.Equal(2, log.Errors.Count);        // the crash itself and the dialog failure
    }
}

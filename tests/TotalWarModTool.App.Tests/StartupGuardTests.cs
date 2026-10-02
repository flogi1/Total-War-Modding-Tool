namespace TotalWarModTool.App.Tests;

public class StartupGuardTests
{
    private static CrashHandler Create(FakeAppLog log, List<(string Details, bool Fatal)> dialogs) =>
        new(log, new FakeTimeProvider(DateTimeOffset.UnixEpoch), "0.1.0", null, (details, fatal) => dialogs.Add((details, fatal)));

    // A start-up that fails before the main window exists would leave a process without any window. The failure is shown as fatal and
    // the app is asked to shut down with exit code 1.
    [Fact] public void Failing_composition_is_handled_as_fatal_and_requests_shutdown_with_exit_code_1()
    {
        var log = new FakeAppLog(); var dialogs = new List<(string Details, bool Fatal)>(); var exitCodes = new List<int>();

        StartupGuard.Run(() => throw new InvalidOperationException("window failed"), Create(log, dialogs), exitCodes.Add);

        var (details, fatal) = Assert.Single(dialogs);
        Assert.True(fatal);
        Assert.Contains("Source: startup", details); Assert.Contains("window failed", details);
        Assert.Equal("Unhandled exception (source: startup).", Assert.Single(log.Errors));
        Assert.Equal([1], exitCodes);
    }

    // The dialog is modal: the handler returns when the user closed it. Only then may the shutdown be requested, so the user can read
    // that the app closes, and the exit code is the one of the failed start-up.
    [Fact] public void Shutdown_is_requested_after_the_handler_has_returned_with_exit_code_1()
    {
        var events = new List<string>();
        var handler = new CrashHandler(new FakeAppLog(), new FakeTimeProvider(DateTimeOffset.UnixEpoch), "0.1.0", null,
            (_, _) => { events.Add("dialog shown"); events.Add("dialog closed"); });

        StartupGuard.Run(() => throw new InvalidOperationException("window failed"), handler, code => events.Add($"shutdown {code}"));

        Assert.Equal(["dialog shown", "dialog closed", "shutdown 1"], events);
    }

    [Fact] public void Successful_composition_neither_shows_a_dialog_nor_requests_shutdown()
    {
        var log = new FakeAppLog(); var dialogs = new List<(string Details, bool Fatal)>(); var exitCodes = new List<int>();
        var ran = 0;

        StartupGuard.Run(() => ran++, Create(log, dialogs), exitCodes.Add);

        Assert.Equal(1, ran);
        Assert.Empty(dialogs); Assert.Empty(exitCodes); Assert.Empty(log.Errors);
    }
}

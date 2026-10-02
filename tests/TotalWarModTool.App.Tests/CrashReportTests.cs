namespace TotalWarModTool.App.Tests;

public class CrashReportTests
{
    [Fact] public void Build_contains_version_source_time_and_redacted_exception()
    {
        var ex = new InvalidOperationException(@"cannot read C:\Users\alice\x.json");
        var text = CrashReport.Build(ex, "ui", "0.1.0", new DateTimeOffset(2026, 10, 2, 14, 3, 7, TimeSpan.Zero), @"C:\Users\alice");
        var lines = text.Split(Environment.NewLine);
        Assert.Equal("Total War Modding Tool 0.1.0", lines[0]);
        Assert.Equal("2026-10-02T14:03:07.0000000+00:00", lines[1]);
        Assert.Equal("Source: ui", lines[2]);
        Assert.Contains(@"System.InvalidOperationException: cannot read %USERPROFILE%\x.json", text);
        Assert.DoesNotContain("alice", text);
    }
}

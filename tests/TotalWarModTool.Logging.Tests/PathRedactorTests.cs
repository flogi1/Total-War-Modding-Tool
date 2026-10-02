namespace TotalWarModTool.Logging.Tests;

public class PathRedactorTests
{
    [Theory]
    [InlineData(@"open C:\Users\alice\AppData\x.json", @"C:\Users\alice", @"open %USERPROFILE%\AppData\x.json")]
    [InlineData(@"open c:\users\ALICE\x", @"C:\Users\alice", @"open %USERPROFILE%\x")]
    [InlineData(@"open D:\Games\data", @"C:\Users\alice", @"open D:\Games\data")]
    [InlineData(@"open C:\Users\alice\x", null, @"open C:\Users\alice\x")]
    public void Redact_replaces_profile_path(string text, string? profile, string expected) =>
        Assert.Equal(expected, PathRedactor.Redact(text, profile));
}

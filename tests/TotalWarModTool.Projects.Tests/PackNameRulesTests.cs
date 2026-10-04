using TotalWarModTool.Projects;

namespace TotalWarModTool.Projects.Tests;

public class PackNameRulesTests
{
    [Theory]
    [InlineData("my_mod.pack")] [InlineData("!!My Mod (v2).PACK")] [InlineData("ünïcode_日本.pack")]
    public void Accepts_a_usable_name(string name) => Assert.Null(PackNameRules.Check(name));

    [Theory]
    [InlineData("")] [InlineData(".pack")] [InlineData("mod")] [InlineData("mod.pack.txt")] [InlineData("a/b.pack")] [InlineData(@"a\b.pack")]
    [InlineData("a:b.pack")] [InlineData("a*b.pack")] [InlineData(" mod.pack")] [InlineData("con.pack")] [InlineData("COM1.x.pack")] [InlineData("a\tb.pack")]
    public void Rejects_an_unusable_name(string name) => Assert.False(string.IsNullOrWhiteSpace(PackNameRules.Check(name)));

    [Fact]
    public void Rejects_a_too_long_name_and_a_lone_surrogate()
    {
        Assert.Null(PackNameRules.Check(new string('a', 95) + ".pack"));
        Assert.NotNull(PackNameRules.Check(new string('a', 96) + ".pack"));
        Assert.NotNull(PackNameRules.Check("a" + (char)0xD800 + ".pack"));
    }
}

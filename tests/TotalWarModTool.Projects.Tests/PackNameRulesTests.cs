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

    [Fact] public void Rejects_the_superscript_and_console_device_names()
    {
        Assert.False(string.IsNullOrWhiteSpace(PackNameRules.Check("COM" + (char)0xB2 + ".pack")));
        Assert.False(string.IsNullOrWhiteSpace(PackNameRules.Check("lpt" + (char)0xB3 + ".x.pack")));
        Assert.False(string.IsNullOrWhiteSpace(PackNameRules.Check("CONIN$.pack")));
    }

    [Fact]
    public void Rejects_a_too_long_name_and_a_lone_surrogate()
    {
        Assert.Null(PackNameRules.Check(new string('a', 95) + ".pack"));
        Assert.NotNull(PackNameRules.Check(new string('a', 96) + ".pack"));
        Assert.NotNull(PackNameRules.Check("a" + (char)0xD800 + ".pack"));
    }

    [Fact] public void Check_with_vanilla_names_adds_the_vanilla_rule_and_ignores_case_whatever_the_set_does()
    {
        var vanilla = new HashSet<string>(StringComparer.Ordinal) { "data.pack" };
        Assert.Null(PackNameRules.Check("my_mod.pack", vanilla));
        Assert.Null(PackNameRules.Check("my_mod.pack", new HashSet<string>()));
        Assert.False(string.IsNullOrWhiteSpace(PackNameRules.Check("data.pack", vanilla)));
        Assert.False(string.IsNullOrWhiteSpace(PackNameRules.Check("DATA.Pack", vanilla)));
        Assert.Contains("vanilla", PackNameRules.Check("DATA.Pack", vanilla));
        // The plain rules come first and give their own reason.
        Assert.Equal(PackNameRules.Check("nope"), PackNameRules.Check("nope", vanilla));
        Assert.Equal(PackNameRules.Check(""), PackNameRules.Check("", vanilla));
        Assert.Throws<ArgumentNullException>(() => PackNameRules.Check("a.pack", null!));
    }
}

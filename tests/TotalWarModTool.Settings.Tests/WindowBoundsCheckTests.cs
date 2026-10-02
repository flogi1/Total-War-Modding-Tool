namespace TotalWarModTool.Settings.Tests;

public class WindowBoundsCheckTests
{
    // virtual screen: left 0, top 0, 1920 x 1080; minimum window 400 x 300
    [Theory]
    [InlineData(100, 100, 1200, 800, true)]      // fully visible
    [InlineData(-200, 100, 1200, 800, true)]     // partly left, the title bar grip still reaches into the screen
    [InlineData(1800, 100, 1200, 800, true)]     // partly right, starts inside the screen
    [InlineData(2500, 100, 1200, 800, false)]    // on a monitor that is gone
    [InlineData(-1300, 100, 1200, 800, false)]   // completely left of the screen
    [InlineData(100, -10, 1200, 800, false)]     // title bar above the screen
    [InlineData(100, 1060, 1200, 800, false)]    // title bar below the screen
    [InlineData(100, 100, 300, 800, false)]      // narrower than the minimum
    [InlineData(100, 100, double.NaN, 800, false)]
    public void IsUsable(double left, double top, double width, double height, bool expected) =>
        Assert.Equal(expected, WindowBoundsCheck.IsUsable(new(left, top, width, height, false), 0, 0, 1920, 1080, 400, 300));

    [Fact] public void Null_is_not_usable() => Assert.False(WindowBoundsCheck.IsUsable(null, 0, 0, 1920, 1080, 400, 300));
}
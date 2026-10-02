namespace TotalWarModTool.Settings;

/// <summary>Checks a saved window placement: large enough and with a visible title bar on the virtual screen (all monitors).</summary>
public static class WindowBoundsCheck
{
    private const double TitleBarHeight = 40;
    private const double TitleBarGrip = 300;

    public static bool IsUsable(WindowPlacement? saved, double screenLeft, double screenTop, double screenWidth, double screenHeight, double minWidth, double minHeight)
    {
        if (saved is null
            || !double.IsFinite(saved.Left) || !double.IsFinite(saved.Top) || !double.IsFinite(saved.Width) || !double.IsFinite(saved.Height)
            || saved.Width < minWidth || saved.Height < minHeight)
        {
            return false;
        }

        // Only the left part of the title bar has to be reachable, so a window may hang over the right edge of the screen.
        var gripWidth = Math.Min(saved.Width, TitleBarGrip);
        return saved.Left < screenLeft + screenWidth && saved.Left + gripWidth > screenLeft
            && saved.Top >= screenTop && saved.Top + TitleBarHeight <= screenTop + screenHeight;
    }
}
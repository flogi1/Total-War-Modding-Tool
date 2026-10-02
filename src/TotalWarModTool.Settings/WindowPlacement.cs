namespace TotalWarModTool.Settings;

/// <summary>Saved position and size of a window, in device-independent pixels. <paramref name="Maximized"/> tells whether it was maximized.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
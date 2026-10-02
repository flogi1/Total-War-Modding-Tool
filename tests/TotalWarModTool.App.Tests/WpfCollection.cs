namespace TotalWarModTool.App.Tests;

/// <summary>
/// Name of the xUnit collection for test classes that create windows or load XAML. xUnit runs the classes of different collections in
/// parallel, and each of them uses its own STA thread. When three such classes did their first WPF/XAML work at the same moment, the test host
/// stopped without any CPU use: none of the tests of those classes finished (seen in 2 of 6 runs without a debugger or blame collector). A shared
/// collection makes these classes run one after another; all other classes stay parallel. The app has one UI thread, so this concerns only the tests.
/// </summary>
internal static class WpfCollection
{
    public const string Name = "WPF windows and XAML";
}

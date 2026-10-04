using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace TotalWarModTool.App.Tests;

public class ProjectWindowsTests
{
    private static readonly AppTexts Texts = new(new TotalWarModTool.Localization.UiText(Path.Combine(Path.GetTempPath(), "no-such-locales-folder")));

    // The styles of Controls.xaml go into the window's own resources: the test host has no Application.
    private static void AddControlStyles(Window window) =>
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));

    private static void Show(Window window)
    {
        AddControlStyles(window);
        window.Show();
        Pump(window);
    }

    // Bindings are delivered by the dispatcher; a window nobody pumps needs this nudge.
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static T Find<T>(Window window, string name) where T : class => Assert.IsType<T>(window.FindName(name));

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in VisualDescendants<T>(child))
            {
                yield return inner;
            }
        }
    }

    private static void Click(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    [Fact] public void The_input_window_binds_fields_error_and_buttons() => Sta.Run(() =>
    {
        string? browsed = @"C:\browsed";
        var viewModel = new ProjectInputViewModel(Texts, new ProjectInput("Mod", @"C:\mods", "mod.pack"), ["Tables: 1", "Loc files: 2"],
            input => input.Name.Length == 0 ? "Enter a name" : null, () => browsed);
        var window = new ProjectInputWindow(viewModel, "Import source.pack");
        Show(window);
        var nameBox = Find<TextBox>(window, "NameBox");
        var folderBox = Find<TextBox>(window, "FolderBox");
        var packNameBox = Find<TextBox>(window, "PackNameBox");
        var error = Find<TextBlock>(window, "ErrorText");
        var info = Find<TextBlock>(window, "InfoText");
        var ok = Find<Button>(window, "OkButton");
        var cancel = Find<Button>(window, "CancelButton");
        var browse = Find<Button>(window, "BrowseButton");

        // What the window shows at the start.
        Assert.Equal(("Import source.pack", WindowStartupLocation.CenterOwner, false), (window.Title, window.WindowStartupLocation, window.ShowInTaskbar));
        Assert.Equal(("Mod", @"C:\mods", "mod.pack"), (nameBox.Text, folderBox.Text, packNameBox.Text));
        Assert.Equal(("Tables: 1\nLoc files: 2", true), (info.Text, info.IsVisible));
        Assert.False(error.IsVisible);
        Assert.Equal(("Name", "Folder", "Pack name"),
            (Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(nameBox)).Text, Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(folderBox)).Text,
                Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(packNameBox)).Text));
        Assert.Equal(("OK", "Cancel", "Browse"), ((string)ok.Content, (string)cancel.Content, (string)browse.Content));
        Assert.Equal((true, true, true), (ok.IsDefault, cancel.IsCancel, ok.IsEnabled));

        // Typing goes into the view model at once and the validation answers at once.
        nameBox.Text = "";
        Pump(window);

        Assert.Equal("", viewModel.Name);
        Assert.Equal(("Enter a name", true, false), (error.Text, error.IsVisible, ok.IsEnabled));

        nameBox.Text = "New Mod";
        Pump(window);

        Assert.Equal("New Mod", viewModel.Name);
        Assert.Equal((false, true), (error.IsVisible, ok.IsEnabled));
        Assert.Equal("new_mod.pack", packNameBox.Text);          // follows the name; the view model changed it

        packNameBox.Text = "mine.pack";
        folderBox.Text = @"C:\typed";
        Assert.Equal(("mine.pack", @"C:\typed"), (viewModel.PackName, viewModel.Folder));

        // Browse puts the chosen folder into the box.
        Click(browse);
        Pump(window);

        Assert.Equal(@"C:\browsed", folderBox.Text);
        Assert.Equal(new ProjectInput("New Mod", @"C:\browsed", "mine.pack"), viewModel.Result);
    });

    [Fact] public void The_input_window_hides_the_info_block_when_there_is_nothing_to_say() => Sta.Run(() =>
    {
        var viewModel = new ProjectInputViewModel(Texts, new ProjectInput("", "", ""), [], _ => null, () => null);
        var window = new ProjectInputWindow(viewModel, "New project");
        Show(window);

        Assert.False(Find<TextBlock>(window, "InfoText").IsVisible);
        Assert.True(Find<Button>(window, "OkButton").IsEnabled);
    });

    [Fact] public void The_report_window_shows_summary_and_sections() => Sta.Run(() =>
    {
        var report = new Report("Import report", "Imported 1 table.\nOne entry was left out.",
            [new ReportSection("Left out", ["a.txt: reason one", "b.txt: reason two"]), new ReportSection("Binary", ["t: no schema"])]);
        var window = new ReportWindow(report, Texts);
        Show(window);
        var summary = Find<TextBlock>(window, "SummaryText");
        var sections = Find<ItemsControl>(window, "SectionsList");
        var close = Find<Button>(window, "CloseButton");

        Assert.Equal(("Import report", WindowStartupLocation.CenterOwner, false), (window.Title, window.WindowStartupLocation, window.ShowInTaskbar));
        Assert.Equal("Imported 1 table.\nOne entry was left out.", summary.Text);
        Assert.Equal(2, sections.Items.Count);
        Assert.Equal(["Left out", "Binary"], VisualDescendants<TextBlock>(sections).Where(block => block.Name == "HeadingText").Select(block => block.Text));

        // The lines are selectable text: a read-only box, one per section.
        var boxes = VisualDescendants<TextBox>(sections).ToList();
        Assert.Equal(["a.txt: reason one\nb.txt: reason two", "t: no schema"], boxes.Select(box => box.Text));
        Assert.All(boxes, box => Assert.True(box.IsReadOnly));

        Assert.Equal(("Close", true), ((string)close.Content, close.IsCancel));
    });
}

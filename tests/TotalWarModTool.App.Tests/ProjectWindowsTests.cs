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

    // Shows the window; disposing the result closes it, so that a failing assertion does not leave the window open.
    private static IDisposable Show(Window window)
    {
        AddControlStyles(window);
        window.Show();
        Pump(window);
        return new WindowCloser(window);
    }

    private sealed class WindowCloser(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
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
        var viewModel = new ProjectInputViewModel(Texts, new ProjectInput("Mod", @"C:\mods", ""), ["Tables: 1", "Loc files: 2"],
            input => input.Name.Length == 0 ? "Enter a name" : null, () => browsed);
        var window = new ProjectInputWindow(viewModel, "Import source.pack");
        using var shown = Show(window);
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
        Assert.Equal(("Mod", @"C:\mods", ""), (nameBox.Text, folderBox.Text, packNameBox.Text));
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
        using var shown = Show(window);

        Assert.False(Find<TextBlock>(window, "InfoText").IsVisible);
        Assert.True(Find<Button>(window, "OkButton").IsEnabled);
    });

    // The automation click is posted to the dispatcher: this lets it run (a closed window has no layout to update, so Pump does not fit).
    private static void Drain(Window window) => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    // Shows the window without a disposable closer: the test clicks a button, which closes it. Whatever is still open at the end is closed.
    private static void ShowAndRun(Window window, Action<List<Button>> test)
    {
        var closed = false;
        window.Closed += (_, _) => closed = true;
        AddControlStyles(window);
        window.Show();
        Pump(window);
        try
        {
            test(VisualDescendants<Button>(window).ToList());
        }
        finally
        {
            if (!closed)
            {
                window.Close();
            }
        }
    }

    [Fact] public void The_choice_window_returns_the_clicked_button_and_cancel_for_escape() => Sta.Run(() =>
    {
        string[] files = ["db/x_tables/a.tsv", "db/x_tables/b.tsv"];
        string[] texts = ["Save", "Discard", "Cancel"];
        ChoiceWindow Make(IReadOnlyList<string>? lines = null) => new("Unsaved changes", "What should happen?", lines ?? files, texts, 0, 2);

        // The content, the default button (Enter) and the cancel button (Esc).
        var window = Make();
        ShowAndRun(window, buttons =>
        {
            Assert.Equal(("Unsaved changes", WindowStartupLocation.CenterOwner, false), (window.Title, window.WindowStartupLocation, window.ShowInTaskbar));
            Assert.Equal("What should happen?", Find<TextBlock>(window, "MessageText").Text);
            var list = Find<ItemsControl>(window, "LinesList");
            Assert.Equal(files, list.Items.Cast<string>());
            Assert.True(list.IsVisible);
            Assert.Equal(texts, buttons.Select(button => (string)button.Content));
            Assert.Equal([true, false, false], buttons.Select(button => button.IsDefault));
            Assert.Equal([false, false, true], buttons.Select(button => button.IsCancel));
            Assert.Equal(2, window.Result);                          // nothing clicked yet: the answer is the cancel button
        });

        // Every button returns its own index and closes the window.
        for (var index = 0; index < texts.Length; index++)
        {
            var clicked = Make();
            var expected = index;
            var wasClosed = false;
            clicked.Closed += (_, _) => wasClosed = true;
            ShowAndRun(clicked, buttons =>
            {
                Click(buttons[expected]);
                Drain(clicked);
                Assert.True(wasClosed);
                Assert.Equal(expected, clicked.Result);
            });
        }

        // The close box and Esc end the window without a click: the cancel button's answer, also after the window was closed.
        var closed = Make();
        ShowAndRun(closed, _ =>
        {
            closed.Close();
            Assert.Equal(2, closed.Result);
        });

        // Two buttons and no lines: the list is hidden.
        var two = new ChoiceWindow("Build", "Save all and build?", [], ["Save all and build", "Cancel"], 0, 1);
        ShowAndRun(two, buttons =>
        {
            Assert.False(Find<ItemsControl>(two, "LinesList").IsVisible);
            Assert.Equal([true, false], buttons.Select(button => button.IsDefault));
            Assert.Equal([false, true], buttons.Select(button => button.IsCancel));
            Click(buttons[0]);
            Drain(two);
            Assert.Equal(0, two.Result);
        });
    });

    [Fact] public void The_report_window_shows_summary_and_sections() => Sta.Run(() =>
    {
        var report = new Report("Import report", "Imported 1 table.\nOne entry was left out.",
            [new ReportSection("Left out", ["a.txt: reason one", "b.txt: reason two"]), new ReportSection("Binary", ["t: no schema"])]);
        var window = new ReportWindow(report, Texts);
        using var shown = Show(window);
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

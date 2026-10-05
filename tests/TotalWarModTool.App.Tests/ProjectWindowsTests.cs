using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
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

    // ---- the name window and the new table window --------------------------------------------------------------------------------

    private static string TableName(int index) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"t{index:D4}_tables");

    private static readonly string[] ManyTables = Enumerable.Range(0, 1500).Select(TableName).ToArray();

    // A tunnelling key event as the window would get it from the keyboard; the key is handled when a handler of the window took it.
    private static bool PressKey(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args);
        return args.Handled;
    }

    // Esc through the input pipeline of the window (not a Close call): the cancel button of the window answers it, as for a user.
    private static void PressEscape(Window window)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        InputManager.Current.ProcessInput(args);
        Drain(window);
    }

    [Fact] public void The_name_window_and_the_new_table_window_return_the_input_and_null_for_escape() => Sta.Run(() =>
    {
        // The name window: the content, the focus on the name with the suggestion selected, the validation and the answer.
        var nameModel = new NameInputViewModel(Texts, "File name", "mine", name => name == "taken" ? "A file with this name exists already" : null);
        var nameWindow = new NameInputWindow(nameModel, "Rename");
        ShowAndRun(nameWindow, _ =>
        {
            var nameBox = Find<TextBox>(nameWindow, "NameBox");
            var error = Find<TextBlock>(nameWindow, "ErrorText");
            var ok = Find<Button>(nameWindow, "OkButton");
            var cancel = Find<Button>(nameWindow, "CancelButton");

            Assert.Equal(("Rename", WindowStartupLocation.CenterOwner, false), (nameWindow.Title, nameWindow.WindowStartupLocation, nameWindow.ShowInTaskbar));
            Assert.Equal("File name", Find<TextBlock>(nameWindow, "NameLabel").Text);
            Assert.Equal("File name", Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(nameBox)).Text);
            Assert.Equal(("mine", 4), (nameBox.Text, nameBox.SelectionLength));                // the suggestion is selected, so typing replaces it
            Assert.Same(nameBox, FocusManager.GetFocusedElement(nameWindow));
            Assert.False(error.IsVisible);
            Assert.Equal(("OK", "Cancel"), ((string)ok.Content, (string)cancel.Content));
            Assert.Equal((true, true, true), (ok.IsDefault, cancel.IsCancel, ok.IsEnabled));

            nameBox.Text = "taken";
            Pump(nameWindow);

            Assert.Equal("taken", nameModel.Name);
            Assert.Equal(("A file with this name exists already", true, false), (error.Text, error.IsVisible, ok.IsEnabled));
            Assert.Null(nameWindow.Result);

            nameBox.Text = "free";
            Pump(nameWindow);

            Assert.Equal((false, true), (error.IsVisible, ok.IsEnabled));

            Click(ok);
            Drain(nameWindow);

            Assert.Equal("free", nameWindow.Result);
            Assert.False(nameWindow.IsVisible);                                                 // OK closes the window
        });

        // Cancel, the close box and Esc give no answer, whatever was typed.
        var cancelled = new NameInputWindow(new NameInputViewModel(Texts, "Name", "mine", _ => null), "Rename");
        ShowAndRun(cancelled, _ =>
        {
            Find<TextBox>(cancelled, "NameBox").Text = "typed";
            Click(Find<Button>(cancelled, "CancelButton"));
            Drain(cancelled);

            Assert.False(cancelled.IsVisible);
            Assert.Null(cancelled.Result);
        });

        var escaped = new NameInputWindow(new NameInputViewModel(Texts, "Name", "mine", _ => null), "Rename");
        ShowAndRun(escaped, _ =>
        {
            Find<TextBox>(escaped, "NameBox").Text = "typed";
            PressEscape(escaped);

            Assert.False(escaped.IsVisible);
            Assert.Null(escaped.Result);
        });

        // The new table window: a filter box over a list of 1,500 tables, then the name; the first table is selected.
        var tableModel = new NewTableViewModel(Texts, ManyTables, "mine", input => input.TableName == "t0151_tables" ? "No name here" : null);
        var tableWindow = new NewTableWindow(tableModel, "New table");
        ShowAndRun(tableWindow, _ =>
        {
            var filterBox = Find<TextBox>(tableWindow, "FilterBox");
            var list = Find<ListBox>(tableWindow, "TableList");
            var nameBox = Find<TextBox>(tableWindow, "NameBox");
            var error = Find<TextBlock>(tableWindow, "ErrorText");
            var ok = Find<Button>(tableWindow, "OkButton");
            var cancel = Find<Button>(tableWindow, "CancelButton");

            Assert.Equal(("New table", WindowStartupLocation.CenterOwner, false), (tableWindow.Title, tableWindow.WindowStartupLocation, tableWindow.ShowInTaskbar));
            Assert.Equal(("Filter tables", "Table", "Name"),
                (Find<TextBlock>(tableWindow, "FilterLabel").Text, Find<TextBlock>(tableWindow, "TableLabel").Text, Find<TextBlock>(tableWindow, "NameLabel").Text));
            Assert.Equal("Filter tables", Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(filterBox)).Text);
            Assert.Equal("Table", Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(list)).Text);
            Assert.Equal("Name", Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(nameBox)).Text);
            Assert.Same(filterBox, FocusManager.GetFocusedElement(tableWindow));
            Assert.Equal(("mine", 4), (nameBox.Text, nameBox.SelectionLength));
            Assert.Equal(1500, list.Items.Count);
            Assert.Equal(TableName(0), list.SelectedItem);
            Assert.Equal((true, true, true), (ok.IsDefault, cancel.IsCancel, ok.IsEnabled));
            Assert.False(error.IsVisible);

            // Typing in the filter box narrows the list at once, anywhere in the name; the first table of the rest is selected.
            filterBox.Text = "T01";
            Pump(tableWindow);

            Assert.Equal(100, list.Items.Count);
            Assert.Equal(TableName(100), list.SelectedItem);
            Assert.Equal(TableName(100), tableModel.SelectedTable);

            // A table that is still listed stays selected when the filter changes.
            list.SelectedItem = TableName(150);
            filterBox.Text = "t015";
            Pump(tableWindow);

            Assert.Equal(10, list.Items.Count);
            Assert.Equal(TableName(150), list.SelectedItem);
            Assert.Equal(TableName(150), tableModel.SelectedTable);

            // A Ctrl+click that would deselect the table is undone: a table stays chosen while the list shows tables.
            list.SelectedItem = null;
            Pump(tableWindow);

            Assert.Equal(TableName(150), list.SelectedItem);
            Assert.Equal(TableName(150), tableModel.SelectedTable);
            Assert.True(ok.IsEnabled);

            // The check follows the table: this one has no free name, and OK is blocked with the reason.
            list.SelectedItem = TableName(151);
            Pump(tableWindow);

            Assert.Equal(("No name here", true, false), (error.Text, error.IsVisible, ok.IsEnabled));

            list.SelectedItem = TableName(150);
            nameBox.Text = "other";
            Pump(tableWindow);

            Assert.Equal((false, true), (error.IsVisible, ok.IsEnabled));

            // Arrow down in the filter box and Enter in the list are taken by the window (moving the focus on), not by the default button.
            Assert.True(PressKey(filterBox, Key.Down));
            Assert.True(list.IsKeyboardFocusWithin);                                            // the list has the focus: on the chosen table
            Assert.Equal(TableName(150), list.SelectedItem);
            Assert.True(PressKey(list, Key.Enter));
            Assert.True(nameBox.IsKeyboardFocused);                                             // Enter went on to the name, with the name selected
            Assert.Equal(nameBox.Text.Length, nameBox.SelectionLength);
            Assert.Null(tableWindow.Result);

            Click(ok);
            Drain(tableWindow);

            Assert.Equal(new NewTableInput(TableName(150), "other"), tableWindow.Result);
            Assert.False(tableWindow.IsVisible);
        });

        var tableEscaped = new NewTableWindow(new NewTableViewModel(Texts, ManyTables, "mine", _ => null), "New table");
        ShowAndRun(tableEscaped, _ =>
        {
            PressEscape(tableEscaped);

            Assert.False(tableEscaped.IsVisible);
            Assert.Null(tableEscaped.Result);
        });

        var tableCancelled = new NewTableWindow(new NewTableViewModel(Texts, ManyTables, "mine", _ => null), "New table");
        ShowAndRun(tableCancelled, _ =>
        {
            Click(Find<Button>(tableCancelled, "CancelButton"));
            Drain(tableCancelled);

            Assert.False(tableCancelled.IsVisible);
            Assert.Null(tableCancelled.Result);
        });
    });

    [Fact] public void The_new_table_window_shows_only_the_rows_that_fit_in_the_list()
    {
        // 1,500 names must not become 1,500 controls: the list is virtualized.
        Sta.Run(() =>
        {
            var window = new NewTableWindow(new NewTableViewModel(Texts, ManyTables, "mine", _ => null), "New table");
            ShowAndRun(window, _ =>
            {
                var list = Find<ListBox>(window, "TableList");

                var realized = VisualDescendants<ListBoxItem>(list).Count();

                Assert.Equal(1500, list.Items.Count);
                Assert.InRange(realized, 1, 200);
            });
        });
    }

    [Fact] public void The_new_table_window_says_so_when_there_is_no_table_to_choose_from() => Sta.Run(() =>
    {
        var window = new NewTableWindow(new NewTableViewModel(Texts, [], "mine", _ => null), "New table");
        ShowAndRun(window, _ =>
        {
            var error = Find<TextBlock>(window, "ErrorText");

            Assert.Equal((Texts.FileNoSchema, true), (error.Text, error.IsVisible));
            Assert.False(Find<Button>(window, "OkButton").IsEnabled);
            Assert.Empty(Find<ListBox>(window, "TableList").Items);
            Assert.True(Find<Button>(window, "CancelButton").IsEnabled);
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

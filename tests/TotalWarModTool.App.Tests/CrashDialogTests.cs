using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TotalWarModTool.Localization;

namespace TotalWarModTool.App.Tests;

public class CrashDialogTests
{
    private static CrashDialog Create(string details, Action openLog) =>
        new(new AppTexts(new UiText(@"X:\none")), details, openLog);

    private static T Find<T>(CrashDialog dialog, string name) where T : class =>
        Assert.IsType<T>(dialog.FindName(name));

    // Not clicked with the mouse: the click event is what the button raises, and what the code-behind wiring listens to.
    private static void Click(CrashDialog dialog, string buttonName) =>
        Find<Button>(dialog, buttonName).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact] public void Shows_the_texts_and_the_details_read_only() => Sta.Run(() =>
    {
        var dialog = Create("line one" + Environment.NewLine + "line two", () => { });
        // Bindings to the DataContext are delivered by the dispatcher at DataBind priority; a window nobody pumps needs this nudge.
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        var details = Find<TextBox>(dialog, "DetailsBox");
        Assert.Equal(("line one" + Environment.NewLine + "line two", true), (details.Text, details.IsReadOnly));
        Assert.Equal("Unexpected error", dialog.Title);
        Assert.Equal(("Open log", "Copy details", "Close"),
            (Find<Button>(dialog, "OpenLogButton").Content, Find<Button>(dialog, "CopyDetailsButton").Content, Find<Button>(dialog, "CloseButton").Content));
    });

    [Fact] public void Open_log_calls_back_and_leaves_the_dialog_open() => Sta.Run(() =>
    {
        var opened = 0;
        var dialog = Create("x", () => opened++);
        dialog.Show();
        Click(dialog, "OpenLogButton");
        Assert.Equal((1, true), (opened, dialog.IsVisible));
    });

    [Fact] public void Close_closes_the_dialog() => Sta.Run(() =>
    {
        var dialog = Create("x", () => { });
        dialog.Show();
        Click(dialog, "CloseButton");
        Assert.False(dialog.IsVisible);
    });
}

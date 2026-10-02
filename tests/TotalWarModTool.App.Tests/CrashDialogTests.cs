using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TotalWarModTool.Localization;

namespace TotalWarModTool.App.Tests;

public class CrashDialogTests
{
    private static CrashDialog Create(string details, Action openLog, bool fatal = false) =>
        new(new AppTexts(new UiText(@"X:\none")), details, openLog, fatal);

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

    private static string MessageOf(CrashDialog dialog)
    {
        // Same nudge as above: the binding is only delivered when the dispatcher runs the DataBind items.
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        return Find<TextBlock>(dialog, "MessageBlock").Text;
    }

    [Fact] public void Shows_the_keep_working_message_by_default() => Sta.Run(() =>
        Assert.Equal("Total War Modding Tool ran into an unexpected error. Your settings are safe. You can keep working, but restarting the app is safer.",
            MessageOf(Create("x", () => { }))));

    [Fact] public void Shows_the_fatal_message_when_the_error_is_fatal() => Sta.Run(() =>
        Assert.Equal("Total War Modding Tool ran into an unexpected error and has to close. The log contains details.",
            MessageOf(Create("x", () => { }, fatal: true))));

    // The fatal text follows a language change like the normal one, because it is a binding to AppTexts and not a copied string.
    [Fact] public void Fatal_message_follows_a_language_change() => Sta.Run(() =>
    {
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var dialog = new CrashDialog(new AppTexts(ui), "x", () => { }, fatal: true);
        MessageOf(dialog);
        ui.Load("de");
        Assert.Equal("Total War Modding Tool ist auf einen unerwarteten Fehler gestoßen und muss beendet werden. Details stehen im Log.", MessageOf(dialog));
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

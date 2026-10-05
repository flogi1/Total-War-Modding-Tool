using System.Windows;
using System.Windows.Controls;

namespace TotalWarModTool.App;

/// <summary>
/// A modal question: a message, a list of lines and two or three buttons with their own texts. The answer is the index of the button that
/// was clicked; Esc and the close box answer with the cancel button. Shown by <see cref="EditDialogs"/>.
/// </summary>
public sealed partial class ChoiceWindow : Window
{
    /// <param name="lines">Shown under the message; none: the list is hidden.</param>
    /// <param name="buttons">The texts of the buttons, left to right.</param>
    /// <param name="defaultIndex">The button that Enter presses (the safe one).</param>
    /// <param name="cancelIndex">The button that Esc presses; also the answer when the window is closed without a click.</param>
    public ChoiceWindow(string title, string message, IReadOnlyList<string> lines, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentOutOfRangeException.ThrowIfLessThan(buttons.Count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(defaultIndex, buttons.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(cancelIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cancelIndex, buttons.Count);

        InitializeComponent();
        Title = title;
        Result = cancelIndex;
        MessageText.Text = message;
        LinesList.ItemsSource = lines;
        LinesList.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttons[i],
                MinWidth = 90,
                Padding = new Thickness(12, 3, 12, 3),
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = i == defaultIndex,
                IsCancel = i == cancelIndex,
            };
            button.Click += (_, _) =>
            {
                Result = index;
                Close();
            };
            ButtonsPanel.Children.Add(button);
        }
    }

    /// <summary>The index of the button that was clicked; the cancel button's index while none was.</summary>
    public int Result { get; private set; }
}

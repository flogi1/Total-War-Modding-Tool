using TotalWarModTool.Localization;

namespace TotalWarModTool.App.Tests;

public class ProgressWindowTests
{
    [Fact] public void Apply_shows_bar_and_count_when_total_is_known() => Sta.Run(() =>
    {
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => { });
        w.Apply(new(120, 4000, "db/units_tables/data__"));
        Assert.Equal((false, 4000d, 120d), (w.IsIndeterminate, w.BarMaximum, w.BarValue));
        Assert.Equal(("db/units_tables/data__", "120 / 4000"), (w.ItemText, w.CountText));
        w.Apply(new(5, null, null));
        Assert.Equal((true, "", ""), (w.IsIndeterminate, w.ItemText, w.CountText));
    });

    // CLAUDE.md: percent and bar when the total is known, otherwise an indeterminate bar and no percent.
    [Fact] public void Apply_shows_the_percent_only_when_the_total_is_known() => Sta.Run(() =>
    {
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => { });
        w.Apply(new(120, 4000, "x"));
        Assert.Equal("3 %", w.PercentText);
        w.Apply(new(5, null, null));
        Assert.Equal("", w.PercentText);
        w.Apply(new(4000, 4000, null));
        Assert.Equal("100 %", w.PercentText);
    });

    [Theory]
    [InlineData(0L, 4000L, "0 %")]
    [InlineData(39L, 4000L, "0 %")]
    [InlineData(2000L, 4000L, "50 %")]
    [InlineData(3999L, 4000L, "99 %")]
    [InlineData(5000L, 4000L, "100 %")]
    [InlineData(-5L, 4000L, "0 %")]
    public void Apply_rounds_down_and_keeps_the_percent_between_0_and_100(long done, long total, string expected) => Sta.Run(() =>
    {
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => { });
        w.Apply(new(done, total, null));
        Assert.Equal(expected, w.PercentText);
    });

    [Fact] public void Percent_stays_when_cancel_is_requested() => Sta.Run(() =>
    {
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => { });
        w.Apply(new(120, 4000, "x"));
        w.RequestCancel();
        Assert.Equal("3 %", w.PercentText);
    });

    // Like the item line, the percent is frozen by a cancel request; bar and count may still follow the work (see the test below).
    [Fact] public void Percent_stays_as_it_was_when_progress_arrives_after_the_cancel_request() => Sta.Run(() =>
    {
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => { });
        w.Apply(new(10, 100, "a"));
        w.RequestCancel();
        w.Apply(new(50, 100, "b"));
        Assert.Equal(("10 %", "Cancelling"), (w.PercentText, w.ItemText));
    });

    [Fact] public void RequestCancel_calls_back_once_and_disables_the_button() => Sta.Run(() =>
    {
        var calls = 0;
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => calls++);
        w.RequestCancel(); w.RequestCancel();
        Assert.Equal((1, false, "Cancelling"), (calls, w.CancelEnabled, w.ItemText));
    });

    // Added beyond the plan: the cancelling text must survive the next poll, which the timer does every 50 ms.
    [Fact] public void Cancelling_text_is_kept_when_progress_arrives_after_the_cancel_request() => Sta.Run(() =>
    {
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => { });
        w.RequestCancel();
        w.Apply(new(7, 10, "late item"));
        Assert.Equal(("Cancelling", "7 / 10", 7d), (w.ItemText, w.CountText, w.BarValue));
    });

    // Added beyond the plan: the timer wiring (Loaded starts it, TryTake feeds Apply) is otherwise not covered. Waits for the effect, no fixed sleep.
    [Fact] public void Shown_window_picks_up_reported_progress_by_itself() => Sta.RunAsync(async () =>
    {
        var progress = new LatestProgress();
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), progress, () => { });
        w.Show();
        try
        {
            progress.Report(new(3, 10, "item"));
            var deadline = Environment.TickCount64 + 5000;
            while (w.CountText != "3 / 10" && Environment.TickCount64 < deadline)
            {
                await Task.Delay(10);
            }

            Assert.Equal(("item", "3 / 10"), (w.ItemText, w.CountText));
        }
        finally { w.CloseByRunner(); }
    });
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using TotalWarModTool.Operations;

namespace TotalWarModTool.App;

/// <summary>
/// The modal progress window of <see cref="ProgressRunner"/>: bar, current element, count, percent and a Cancel button. It cannot be closed by the
/// user; closing (X) or Cancel only asks the operation to stop, and the runner closes the window with <see cref="CloseByRunner"/> once the work ended.
/// </summary>
public sealed partial class ProgressWindow : Window
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly AppTexts _texts;
    private readonly Action _requestCancel;
    private bool _cancelRequested;
    private bool _closedByRunner;

    public ProgressWindow(string title, AppTexts texts, LatestProgress progress, Action requestCancel)
    {
        _texts = texts;
        _requestCancel = requestCancel;

        InitializeComponent();
        Title = title;
        DataContext = texts;

        // The operation reports into LatestProgress from any thread; this timer is the only place that moves the newest value to the UI.
        var timer = new DispatcherTimer { Interval = PollInterval };
        timer.Tick += (_, _) =>
        {
            if (progress.TryTake(out var latest))
            {
                Apply(latest);
            }
        };
        Loaded += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    public bool IsIndeterminate => Bar.IsIndeterminate;

    public double BarMaximum => Bar.Maximum;

    public double BarValue => Bar.Value;

    public string ItemText => ItemBlock.Text;

    public string CountText => CountBlock.Text;

    public string PercentText => PercentBlock.Text;

    public bool CancelEnabled => CancelButton.IsEnabled;

    /// <summary>Same as clicking Cancel: asks the operation to stop (once), disables the button and shows the cancelling text.</summary>
    public void RequestCancel()
    {
        if (_cancelRequested)
        {
            return;
        }

        _cancelRequested = true;
        CancelButton.IsEnabled = false;
        ItemBlock.Text = _texts.ProgressCancelling;
        _requestCancel();
    }

    /// <summary>The only way the window really closes. Called by the runner when the work has ended.</summary>
    public void CloseByRunner()
    {
        _closedByRunner = true;
        Close();
    }

    /// <summary>
    /// A known total (greater than 0) gives a determinate bar, a count text and the percent (rounded down, between 0 and 100);
    /// otherwise the bar is indeterminate and count and percent are empty. After a cancel request the bar and the count still follow the
    /// work, but the item line and the percent stay as they were.
    /// </summary>
    public void Apply(OperationProgress progress)
    {
        string percent;
        if (progress.Total is long total && total > 0)
        {
            Bar.IsIndeterminate = false;
            Bar.Maximum = total;
            Bar.Value = progress.Done;
            CountBlock.Text = _texts.ProgressCount(progress.Done, total);
            percent = _texts.ProgressPercent((int)(Math.Clamp(progress.Done, 0, total) * 100 / total));
        }
        else
        {
            Bar.IsIndeterminate = true;
            CountBlock.Text = string.Empty;
            percent = string.Empty;
        }

        // After a cancel request the item line keeps saying so, and the percent stays as it was; the next poll must not overwrite them.
        if (!_cancelRequested)
        {
            ItemBlock.Text = progress.Item ?? string.Empty;
            PercentBlock.Text = percent;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_closedByRunner)
        {
            e.Cancel = true;
            RequestCancel();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => RequestCancel();
}

using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.App.Views;

public partial class TroubleshootingPageView : UserControl
{
    private readonly List<string> _progress = [];
    private bool _updatingHistory;

    public TroubleshootingPageView()
    {
        InitializeComponent();
        progressSummary.FontChanged += ScheduleInformationColors;
        progressSummary.HandleCreated += ScheduleInformationColors;
        history.CurrentCellChanged += (_, _) => ShowSelectedDetails();
        history.SizeChanged += (_, _) => UpdateHistoryMetrics();
        history.FontChanged += (_, _) => UpdateHistoryMetrics();
        fixedButton.Click += (_, _) => SubmitFeedback("fixed");
        notFixedButton.Click += (_, _) => SubmitFeedback("not_fixed");
        UpdateHistoryMetrics();
    }

    internal Button CancelButton => cancelButton;
    internal Button RefreshButton => refreshButton;
    internal Button ReproduceButton => reproduceButton;
    internal Button SkipReproductionButton => skipReproductionButton;
    internal Label StatusLabel => statusLabel;
    internal bool IsBusy { get; private set; }
    internal event EventHandler<TroubleshootingFeedbackEventArgs>? FeedbackRequested;

    private TroubleshootingCase? SelectedCase => history.CurrentRow?.Tag as TroubleshootingCase;

    internal void SetBusy(bool busy)
    {
        IsBusy = busy;
        descriptionLabel.Text = busy ? string.Empty : "查看AI的调查过程和建议，并反馈问题是否解决。";
        if (!busy) { SetReproductionPrompt(false); }
        refreshButton.Enabled = !busy;
        history.Enabled = !busy;
        fixedButton.Enabled = !busy && SelectedCase is not null;
        notFixedButton.Enabled = !busy && SelectedCase is not null;
        if (!busy) { ShowSelectedDetails(); }
    }

    internal void SetReproductionPrompt(bool visible)
    {
        reproduceButton.Visible = visible;
        skipReproductionButton.Visible = visible;
        fixedButton.Visible = !visible;
        notFixedButton.Visible = !visible;
    }

    internal void ResetProgress()
    {
        _progress.Clear();
        RenderInformation("正在准备诊断…");
    }

    internal void PrepareHistoryView()
    {
        _progress.Clear();
        RenderInformation("正在读取历史记录…");
    }

    internal void AppendProgress(string message)
    {
        if (_progress.Count == 6) { _progress.RemoveAt(0); }
        _progress.Add(message);
        RenderInformation(string.Join(Environment.NewLine + Environment.NewLine, _progress), messages: _progress);
        progressSummary.SelectionStart = progressSummary.TextLength;
        progressSummary.ScrollToCaret();
    }

    internal void SetHistory(IReadOnlyList<TroubleshootingCase> items)
    {
        string? selected = SelectedCase?.Id;
        _updatingHistory = true;
        try
        {
            history.Rows.Clear();
            foreach (TroubleshootingCase item in items.Take(TroubleshootingProtocol.MaximumHistory))
            {
                int row = history.Rows.Add(item.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                    TroubleshootingPresentation.Preview(item), TroubleshootingPresentation.Result(item));
                history.Rows[row].Tag = item;
            }
            if (history.Rows.Count > 0)
            {
                DataGridViewRow row = history.Rows.Cast<DataGridViewRow>().FirstOrDefault(row =>
                    (row.Tag as TroubleshootingCase)?.Id == selected) ?? history.Rows[0];
                history.CurrentCell = row.Cells[0];
            }
            UpdateHistoryMetrics();
        }
        finally { _updatingHistory = false; }
        ShowSelectedDetails();
    }

    private void ShowSelectedDetails()
    {
        if (_updatingHistory || IsBusy) { return; }
        TroubleshootingCase? item = SelectedCase;
        bool shortReply = item?.Plan is { RequestKind: not TroubleshootingRequestKinds.Diagnostic };
        descriptionLabel.Text = shortReply ? string.Empty : "查看AI的调查过程和建议，并反馈问题是否解决。";
        fixedButton.Visible = !shortReply;
        notFixedButton.Visible = !shortReply;
        fixedButton.Enabled = item is not null && !shortReply;
        notFixedButton.Enabled = item is not null && !shortReply;
        fixedButton.Text = item?.Feedback == "fixed" ? "✓ 已修复" : "已修复";
        notFixedButton.Text = item?.Feedback == "not_fixed" ? "✓ 没修好" : "没修好";
        RenderInformation(item is null ? "暂无诊断历史。新诊断请从上传问题诊断包页面发起。" : TroubleshootingPresentation.Details(item), item?.Plan?.Summary);
        progressSummary.SelectionStart = 0;
        progressSummary.ScrollToCaret();
    }

    private void UpdateHistoryMetrics()
    {
        timeColumn.Width = Math.Max(70, history.ClientSize.Width * 28 / 100);
        resultColumn.Width = Math.Max(90, history.ClientSize.Width * 36 / 100);
        int height = Math.Max(28, history.Font.Height + 10);
        history.RowTemplate.Height = height;
        history.ColumnHeadersHeight = height;
        foreach (DataGridViewRow row in history.Rows) { row.Height = height; }
    }

    private void SubmitFeedback(string value)
    {
        if (!IsBusy && SelectedCase is { } item) { FeedbackRequested?.Invoke(this, new(item.Id, value)); }
    }
}

internal sealed class TroubleshootingFeedbackEventArgs(string id, string value) : EventArgs
{
    public string Id { get; } = id;
    public string Value { get; } = value;
}

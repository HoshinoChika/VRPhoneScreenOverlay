namespace VRPhoneScreenOverlay.App.Views;

internal static class TroubleshootingTextColors
{
    internal static Color Conclusion => UiPalette.Success;
    internal static Color Action => UiPalette.Warning;
    internal static Color Skip => Color.FromArgb(128, 191, 255);
}

public partial class TroubleshootingPageView
{
    private static readonly char[] _sentenceEnd = ['。', '；', ';', '\n'];
    private string? _displayedConclusion;
    private IReadOnlyList<string>? _displayedMessages;
    private bool _colorRefreshPending;

    private void ScheduleInformationColors(object? sender, EventArgs args)
    {
        if (_colorRefreshPending || !progressSummary.IsHandleCreated || progressSummary.IsDisposed) { return; }
        _colorRefreshPending = true;
        progressSummary.BeginInvoke(() =>
        {
            _colorRefreshPending = false;
            if (IsDisposed || Disposing) { return; }
            int selection = progressSummary.SelectionStart;
            RenderInformation(progressSummary.Text, _displayedConclusion, _displayedMessages);
            progressSummary.Select(Math.Min(selection, progressSummary.TextLength), 0);
            progressSummary.ScrollToCaret();
        });
    }

    // Presentation only: semantic hints never affect diagnostics, checks, or actions.
    private void RenderInformation(string text, string? conclusion = null, IReadOnlyList<string>? messages = null)
    {
        _displayedConclusion = conclusion;
        _displayedMessages = messages;
        progressSummary.Text = SeparateActions(text);
        text = progressSummary.Text; // RichEdit normalizes CRLF; style offsets must use the rendered text.
        conclusion = conclusion is null ? null : SeparateActions(conclusion).ReplaceLineEndings("\n");
        using Font actionFont = new(progressSummary.Font, FontStyle.Bold);
        progressSummary.SelectAll();
        progressSummary.SelectionFont = progressSummary.Font;
        progressSummary.SelectionColor = UiPalette.TextSecondary;
        int conclusionStart = -1;
        if (!string.IsNullOrEmpty(conclusion))
        {
            conclusionStart = text.IndexOf(conclusion, StringComparison.Ordinal);
            if (conclusionStart >= 0) { Paint(conclusionStart, conclusion.Length, TroubleshootingTextColors.Conclusion); }
        }
        if (messages is not null)
        {
            int offset = 0;
            foreach (string message in messages)
            {
                string normalized = SeparateActions(message).ReplaceLineEndings("\n");
                int position = text.IndexOf(normalized, offset, StringComparison.Ordinal);
                if (position < 0) { continue; }
                if (!message.StartsWith("正在", StringComparison.Ordinal) && !message.StartsWith("已提交", StringComparison.Ordinal) &&
                    !message.StartsWith("第 ", StringComparison.Ordinal) && !message.StartsWith("已记录", StringComparison.Ordinal))
                { Paint(position, normalized.Length, TroubleshootingTextColors.Conclusion); }
                offset = position + normalized.Length;
            }
        }
        int start = 0;
        bool analysisParagraph = false;
        while (start < text.Length)
        {
            int end = text.IndexOfAny(_sentenceEnd, start);
            if (end < 0) { end = text.Length - 1; }
            string sentence = text[start..(end + 1)].Trim();
            if (sentence.StartsWith("DeepSeek分析：", StringComparison.Ordinal) ||
                sentence.StartsWith("排查结论：", StringComparison.Ordinal) || sentence.StartsWith("分析结论：", StringComparison.Ordinal) ||
                sentence.StartsWith("分析：", StringComparison.Ordinal) || sentence.StartsWith("后续安排：", StringComparison.Ordinal))
            { analysisParagraph = true; }
            bool emphasized = messages is not null || analysisParagraph ||
                (conclusionStart >= 0 && start >= conclusionStart && start < conclusionStart + conclusion!.Length);
            bool failure = sentence.Contains("检查失败", StringComparison.Ordinal) || sentence.Contains("检查超时", StringComparison.Ordinal) ||
                sentence.Contains("检查未能完成", StringComparison.Ordinal) || sentence.Contains("诊断未完成", StringComparison.Ordinal) ||
                sentence.Contains("额度不足", StringComparison.Ordinal);
            Color? color = failure ? UiPalette.Danger : !emphasized ? null : sentence.Contains("跳过", StringComparison.Ordinal) ? TroubleshootingTextColors.Skip :
                IsUserInstruction(sentence)
                    ? TroubleshootingTextColors.Action : analysisParagraph ? TroubleshootingTextColors.Conclusion : null;
            if (color is { } value) { Paint(start, end - start + 1, value); }
            if (text[end] == '\n') { analysisParagraph = false; }
            start = end + 1;
        }
        progressSummary.Select(0, 0);
        progressSummary.SelectionColor = UiPalette.TextSecondary;

        void Paint(int offset, int length, Color color)
        {
            progressSummary.Select(offset, length);
            progressSummary.SelectionColor = color;
            if (color == TroubleshootingTextColors.Action) { progressSummary.SelectionFont = actionFont; }
        }
    }

    private static string SeparateActions(string text) => text.Replace("。请", "。\n\n请", StringComparison.Ordinal)
        .Replace("。建议", "。\n\n建议", StringComparison.Ordinal)
        .Replace("；不方便可跳过", "；\n\n不方便可跳过", StringComparison.Ordinal);

    private static bool IsUserInstruction(string sentence)
    {
        if (sentence.Contains("点击", StringComparison.Ordinal) || sentence.Contains("开始采集后", StringComparison.Ordinal) ||
            sentence.StartsWith("建议", StringComparison.Ordinal)) { return true; }
        for (int index = 0; index < sentence.Length; index++)
        {
            if (sentence[index] == '请' && (index + 1 == sentence.Length || sentence[index + 1] != '求') &&
                (index == 0 || sentence[index - 1] is not ('申' or '邀'))) { return true; }
        }
        return false;
    }
}

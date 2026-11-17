using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal sealed class ClipboardPanelController : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private string? _failureCode;
    public string? FailureCode => Volatile.Read(ref _failureCode);
    public ClipboardPanelController(OpenVrClipboardChannel channel, PhoneClipboardStore store, IVideoLinkResolver resolver,
        Func<string, CancellationToken, Task> copy, IIncidentRecorder? incident = null,
        ClipboardConfigurationStore? configuration = null,
        Func<string, Func<bool>, CancellationToken, Task>? automaticCopy = null)
    {
        _worker = Task.Run(() => PanelWorker.RunAsync(
            () => RunAsync(channel, store, resolver, copy, configuration, automaticCopy, _stop.Token),
            "CLIPBOARD_PANEL_FAILED", code =>
            {
                Volatile.Write(ref _failureCode, code);
                channel.SetPressed(null);
                channel.Publish(ClipboardPanelRenderer.Render([], false, new HashSet<Guid>(), 0, true, null,
                    "剪贴板面板已停止，请重启软件后重试。"));
            }, incident, _stop.Token));
    }

    private static async Task RunAsync(OpenVrClipboardChannel channel, PhoneClipboardStore store, IVideoLinkResolver resolver,
        Func<string, CancellationToken, Task> copy, ClipboardConfigurationStore? configuration,
        Func<string, Func<bool>, CancellationToken, Task>? automaticCopy, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = lifetime.Token;
        var expanded = new HashSet<Guid>();
        long paintedRevision = -1, lastRefresh = 0;
        ClipboardPanelHit? paintedPress = null;
        int scroll = 0;
        Guid newest = Guid.Empty;
        Task active = Task.CompletedTask;
        bool paintedBusy = false;
        // One active operation and one latest-wins pending item; no growing auto-request queue.
        Guid observedHead = store.Snapshot.Entries.FirstOrDefault()?.Id ?? Guid.Empty;
        PhoneClipboardEntry? pending = null;
        ClipboardPreferences? pendingPreferences = null;
        CancellationTokenSource? automatic = null;
        Guid activeHead = Guid.Empty;
        ClipboardPreferences? activePreferences = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var snapshot = store.Snapshot;
                PhoneClipboardEntry? latest = snapshot.Entries.FirstOrDefault();
                if (latest is not null && latest.Id != observedHead)
                {
                    observedHead = latest.Id;
                    pending = latest;
                    pendingPreferences = configuration?.Preferences ?? new();
                }
                if (automatic is not null && (observedHead != activeHead || configuration?.Preferences != activePreferences))
                { await automatic.CancelAsync().ConfigureAwait(false); }
                if (active.IsCompleted && automatic is not null) { automatic.Dispose(); automatic = null; }
                bool repaint = paintedPress != channel.Pressed || paintedBusy != !active.IsCompleted;
                int requestedScroll = channel.TakeScroll();
                if (requestedScroll >= 0) { scroll = requestedScroll; repaint = true; }
                ClipboardPanelHit? command = channel.Take();
                if (command is not null && channel.IsPhoneConnected && snapshot.Connected)
                {
                    repaint = true;
                    switch (command.Action)
                    {
                        case ClipboardPanelAction.Up: scroll = Math.Max(0, scroll - 480); break;
                        case ClipboardPanelAction.Down: scroll = Math.Min(channel.Frame?.MaximumScroll ?? 0, scroll + 480); break;
                        case ClipboardPanelAction.Expand:
                            if (!expanded.Add(command.EntryId)) { expanded.Remove(command.EntryId); }
                            break;
                        case ClipboardPanelAction.Copy or ClipboardPanelAction.Parse when active.IsCompleted:
                            PhoneClipboardEntry? entry = snapshot.Entries.FirstOrDefault(item => item.Id == command.EntryId);
                            if (entry is not null)
                            { pending = null; active = ExecuteAsync(store, entry, command.Action, resolver, copy, token); }
                            break;
                    }
                }
                if (active.IsCompleted && pending is not null)
                {
                    PhoneClipboardEntry entry = pending;
                    ClipboardPreferences preferences = pendingPreferences!;
                    pending = null;
                    if (configuration?.Preferences == preferences && (preferences.AutoCopy || preferences.AutoParse))
                    {
                        automatic = CancellationTokenSource.CreateLinkedTokenSource(token);
                        activeHead = entry.Id; activePreferences = preferences;
                        active = ExecuteAutomaticAsync(store, entry, preferences, configuration, resolver, copy, automaticCopy, automatic.Token);
                    }
                }
                if (snapshot.Revision != paintedRevision && Environment.TickCount64 - lastRefresh >= 1000) { repaint = true; }
                if (!repaint && paintedRevision >= 0) { continue; }
                snapshot = store.Snapshot;
                Guid head = snapshot.Entries.FirstOrDefault()?.Id ?? Guid.Empty;
                if (head != newest) { newest = head; scroll = 0; }
                expanded.IntersectWith(snapshot.Entries.Select(item => item.Id));
                paintedBusy = !active.IsCompleted; paintedPress = channel.Pressed;
                ClipboardPanelFrame frame = ClipboardPanelRenderer.Render(snapshot.Entries, snapshot.Connected, expanded, scroll, paintedBusy, paintedPress);
                scroll = frame.Scroll;
                channel.Publish(frame);
                paintedRevision = snapshot.Revision; lastRefresh = Environment.TickCount64;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { await lifetime.CancelAsync().ConfigureAwait(false); throw; }
        finally
        {
            if (automatic is not null) { await automatic.CancelAsync().ConfigureAwait(false); }
            try { await active.ConfigureAwait(false); }
            finally { automatic?.Dispose(); }
        }
    }
    private static async Task ExecuteAutomaticAsync(PhoneClipboardStore store, PhoneClipboardEntry entry,
        ClipboardPreferences preferences, ClipboardConfigurationStore configuration, IVideoLinkResolver resolver,
        Func<string, CancellationToken, Task> copy, Func<string, Func<bool>, CancellationToken, Task>? guardedCopy, CancellationToken token)
    {
        bool Current() => !token.IsCancellationRequested && configuration.Preferences == preferences &&
            store.Snapshot.Entries.FirstOrDefault()?.Id == entry.Id;
        async Task CopyAsync(string text)
        {
            if (!Current()) { return; }
            if (guardedCopy is not null) { await guardedCopy(text, Current, token).ConfigureAwait(false); }
            else { await copy(text, token).ConfigureAwait(false); }
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            if (preferences.AutoCopy) { await CopyAsync(entry.Text).ConfigureAwait(false); }
            if (!Current()) { return; }
            if (preferences.AutoParse && entry.Url is not null && resolver.Supports(entry.Url))
            {
                store.Update(entry.Id, "正在自动解析…", true);
                string result = await resolver.ResolveAsync(entry.Url, timeout.Token).ConfigureAwait(false);
                if (!Current()) { return; }
                await CopyAsync(result).ConfigureAwait(false);
                if (Current()) { store.Update(entry.Id, "播放链接已自动复制到电脑", false); }
            }
            else if (preferences.AutoCopy) { store.Update(entry.Id, "已自动复制到电脑剪贴板", false); }
        }
        catch (OperationCanceledException) { store.Update(entry.Id, "自动操作已取消或超时", false); }
        catch (Exception) { store.Update(entry.Id, "自动操作未完成，原内容保留，可手动重试", false); }
        finally
        {
            if (!Current()) { store.Update(entry.Id, "已跳过旧内容的自动操作", false); }
        }
    }

    private static async Task ExecuteAsync(PhoneClipboardStore store, PhoneClipboardEntry entry, ClipboardPanelAction action,
        IVideoLinkResolver resolver, Func<string, CancellationToken, Task> copy, CancellationToken token)
    {
        bool copying = action == ClipboardPanelAction.Copy;
        store.Update(entry.Id, copying ? "正在复制…" : "正在解析…", true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            string text = copying ? entry.Text : entry.Url is not null
                ? await resolver.ResolveAsync(entry.Url, timeout.Token).ConfigureAwait(false)
                : throw new InvalidOperationException("VIDEO_URL_UNSUPPORTED");
            copying = true;
            await copy(text, timeout.Token).ConfigureAwait(false);
            store.Update(entry.Id, action == ClipboardPanelAction.Copy ? "已复制到电脑剪贴板" : "播放链接已复制到电脑剪贴板", false);
        }
        catch (OperationCanceledException) { store.Update(entry.Id, "操作已取消或超时，未完成复制", false); }
        catch (Exception exception)
        {
            string message = copying ? "电脑剪贴板暂时不可用，请重试" : exception.Message switch
            {
                "VIDEO_PLATFORM_UNSUPPORTED" => "暂不支持这个网站；支持B站、抖音和媒体直链",
                "VIDEO_WEBSITE_QUOTA" => "解析网站今日免费额度已用完，请稍后或明天再试",
                "VIDEO_WEBSITE_USER_ACTION" => "网站要求用户确认或付费，本软件未自动继续",
                "VIDEO_WEBSITE_CHANGED" => "解析网站协议已变化，请等待适配更新",
                "VIDEO_RATE_LIMITED" => "解析请求过于频繁，请稍后再试",
                "VIDEO_SINGLE_STREAM_UNAVAILABLE" => "未取得音视频完整的单一播放链接",
                _ => "解析未成功，请检查网络或链接后重试",
            };
            store.Update(entry.Id, message, false);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _worker.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}

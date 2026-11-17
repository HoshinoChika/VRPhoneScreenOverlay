using System.Diagnostics;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Protocols;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

/// <summary>Bounded application checks, without model commands, raw logs, device identities or screen capture.</summary>
internal sealed class LocalTroubleshootingInspector(IAndroidConnectionService phone, IPhoneOverlayService video,
    IPhoneAudioService audio, IPhoneControlService control, IPhoneMediaSessionCoordinator? media = null,
    LocalSoftwareFileInspector? files = null, Func<DiagnosticLogHealth>? logHealth = null,
    IOpenVrPlayspaceDragService? playspace = null, IAppSettingsService? settings = null,
    LocalDiagnosticReadTools? readTools = null) : ILocalTroubleshootingInspector
{
    public async Task<TroubleshootingCheckResult> InspectAsync(string check, IProgress<string>? progress,
        Func<CancellationToken, Task<bool>>? prepareReproduction, CancellationToken token)
    {
        if (!TroubleshootingChecks.Supported(check)) { throw new InvalidDataException("AI_CHECK_UNSUPPORTED"); }
        DateTimeOffset start = DateTimeOffset.UtcNow;
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(check == TroubleshootingChecks.Reproduce ? 150 : 30));
        try
        {
            if (check == TroubleshootingChecks.Reproduce)
            {
                progress?.Report("请准备重现刚才的问题。点击“开始复现采集”后，在20秒内按原来的步骤操作；不方便可跳过。");
                if (prepareReproduction is null || !await prepareReproduction(deadline.Token).ConfigureAwait(false))
                { return Result("skipped", "本次未采集用户操作过程。", [new("status", "collection", "user_reproduction_skipped")]); }
            }
            progress?.Report("正在" + TroubleshootingChecks.Label(check) + "…");
            if (check == TroubleshootingChecks.Snapshot)
            {
                PhoneStartupDiagnostic? current = media?.StartupDiagnostic;
                return Result("completed", "已读取当前运行状态和配置，由AI结合问题判断下一步。",
                    [Connection(phone.Snapshot), Binding(video.Snapshot), SpaceEvidence(), SettingsEvidence(),
                    new("startup", "startup", $"observed={current is not null};deviceReady={current?.DeviceReady};steamVrReady={current?.SteamVrReady};canStart={current?.CanStart};autoOpen={current?.AutoOpenEnabled};autoStartSuppressed={current?.AutoStartSuppressed};mediaState={current?.MediaState}"),
                    LocalDiagnosticReadTools.SteamVrProcesses()]);
            }
            if (check == TroubleshootingChecks.Logs)
            {
                return readTools is not null ? await readTools.ReadLogsAsync(deadline.Token).ConfigureAwait(false)
                    : Result("unavailable", "当前环境未配置日志读取工具。", [new("status", "collection", "log_tool_unavailable")]);
            }
            if (check == TroubleshootingChecks.Files)
            {
                return files is not null ? await files.InspectAsync(deadline.Token).ConfigureAwait(false)
                    : Result("unavailable", "当前环境未配置软件文件检查。", [new("status", "software_files", "inspector_unavailable")]);
            }
            if (check == TroubleshootingChecks.ConnectionDetails)
            {
                if (phone is not IAndroidConnectionInspectionService connectionInspector)
                { return Result("unavailable", "当前版本无法进行分阶段连接检查。", [new("status", "collection", "connection_inspector_unavailable")]); }
                AndroidConnectionInspection inspected = await connectionInspector.InspectConnectionAsync(deadline.Token).ConfigureAwait(false);
                List<TroubleshootingEvidence> details = [new("connectionMode", "connection_details",
                    $"method={inspected.Method};savedDevices={inspected.SavedDevices};autoConnectDevices={inspected.AutoConnectDevices};savedEndpoint={inspected.SavedEndpointReachability};savedEndpointMayBeStale=true")];
                foreach (AndroidConnectionInspectionStage stage in inspected.Stages)
                { details.Add(new(stage.Stage, "connection_details", $"stage={stage.Stage};succeeded={stage.Succeeded};timeout={stage.TimedOut};timeoutPhase={stage.TimeoutPhase};error={stage.ErrorCategory};exit={stage.ExitCode};durationMs={stage.DurationMilliseconds};items={stage.Items};ready={stage.ReadyDevices};unauthorized={stage.UnauthorizedDevices};offline={stage.OfflineDevices}")); }
                details.Add(Connection(phone.Snapshot));
                return Result("completed", inspected.Stages.Any(s => !s.Succeeded) ? "分阶段检查发现连接步骤未成功，已记录具体阶段。" : "连接工具和设备发现检查已完成，正在核对连接方式与保存的连接信息。", details.ToArray());
            }
            if (check == TroubleshootingChecks.Startup)
            {
                PhoneStartupDiagnostic? startup = media?.StartupDiagnostic;
                if (startup is null) { return Result("unavailable", "当前版本无法读取浮窗启动条件。", [new("status", "collection", "startup_inspector_unavailable")]); }
                string startupSummary = !startup.DeviceReady ? "手机连接尚未就绪，暂不能开启浮窗。" : !startup.SteamVrReady ? "SteamVR尚未就绪，浮窗无法开始显示。"
                    : !startup.CanStart ? "浮窗启动条件尚未全部满足，需要进一步确认。" : startup.MediaState == PhoneMediaSessionState.Running ? "浮窗会话已在运行，需要检查画面是否正常更新。"
                    : !startup.AutoOpenEnabled ? "启动条件已满足，但自动开启未启用。可在主页手动打开浮窗并观察。"
                    : startup.AutoStartSuppressed ? "启动条件已满足，自动开启目前处于暂停状态。可手动打开浮窗并观察。"
                    : !startup.AutomaticVideoAllowed ? "自动开启被画面参数保护暂缓，可手动打开浮窗检查启动结果。" : "已读取浮窗启动条件；需要观察实际开启过程才能确认失败环节。";
                return Result("completed", startupSummary, [new("startup", "startup",
                    $"deviceReady={startup.DeviceReady};steamVrReady={startup.SteamVrReady};canStart={startup.CanStart};autoOpen={startup.AutoOpenEnabled};autoStartSuppressed={startup.AutoStartSuppressed};attempts={startup.AutoStartAttempts};automaticVideoAllowed={startup.AutomaticVideoAllowed};mediaState={startup.MediaState};mediaReason={Code(startup.MediaReason)}")]);
            }
            if (check == TroubleshootingChecks.Connection)
            {
                DateTimeOffset? before = phone.Snapshot.LastSuccessfulScanAt;
                await phone.RefreshAsync(deadline.Token).ConfigureAwait(false);
                AndroidConnectionSnapshot current = phone.Snapshot;
                bool fresh = current.LastSuccessfulScanAt is { } scanned && scanned >= start && scanned != before;
                return Result(fresh ? "completed" : "unavailable", fresh
                    ? current.IsReady ? "已重新扫描，手机连接可用。" : "已重新扫描，手机连接尚未就绪。"
                    : "已尝试扫描，但未取得新的成功扫描结果。", [Connection(current), new("scan", "connection", $"freshScan={fresh};elapsedMs={(DateTimeOffset.UtcNow - start).TotalMilliseconds:F0}")]);
            }
            if (check == TroubleshootingChecks.Bindings)
            {
                bool changed = video.HasSavedBindingChanged();
                return Result("completed", "已检查当前手柄绑定；实际按键效果需配合操作确认。",
                    [Binding(video.Snapshot), new("bindingFile", "bindings", $"savedBindingChanged={changed};filesNotModified=true")]);
            }
            (TroubleshootingEvidence[] observations, string summary) = await ObserveAsync(check == TroubleshootingChecks.Reproduce ? 20 : 6, progress, deadline.Token).ConfigureAwait(false);
            return Result("completed", summary, observations);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return Result("timeout", "本项检查超时，尚不能据此判断原因。", [new("status", "collection", "check_timeout")]); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or AndroidConnectionException or PhoneOverlayServiceException)
        { return Result("failed", "本项检查未能完成，已保留失败状态。", [new("status", "collection", "check_failed")]); }

        TroubleshootingCheckResult Result(string status, string summary, TroubleshootingEvidence[] evidence) =>
            new(check, start, DateTimeOffset.UtcNow, status, summary, evidence);
    }

    private async Task<(TroubleshootingEvidence[] Evidence, string Summary)> ObserveAsync(int seconds, IProgress<string>? progress, CancellationToken token)
    {
        PhoneOverlaySnapshot firstVideo = video.Snapshot;
        PhoneAudioSnapshot firstAudio = audio.Snapshot;
        PhoneControlSnapshot firstControl = control.Snapshot;
        DiagnosticLogHealth? firstLog = logHealth?.Invoke();
        string? device = phone.Snapshot.SelectedDevice?.DeviceKey;
        bool deviceChanged = false;
        int stalledSamples = 0;
        long lastFrames = firstVideo.SubmittedFrames;
        // Owned by this request only: at most 20 one-second samples, no background queue or per-frame work.
        List<string> timeline = new(seconds);
        List<string> spaceTimeline = new(seconds);
        Stopwatch clock = Stopwatch.StartNew();
        for (int index = 0; index < seconds; index++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            PhoneOverlaySnapshot frame = video.Snapshot;
            if (frame.SubmittedFrames == lastFrames) { stalledSamples++; }
            lastFrames = frame.SubmittedFrames;
            deviceChanged |= device != phone.Snapshot.SelectedDevice?.DeviceKey;
            timeline.Add(FormattableString.Invariant($"{clock.Elapsed.TotalSeconds:F1}s:frames={frame.SubmittedFrames - firstVideo.SubmittedFrames},audio={audio.Snapshot.DecodedPackets - firstAudio.DecodedPackets},sent={control.Snapshot.SentCommands - firstControl.SentCommands}"));
            OpenVrPlayspaceDragSnapshot? space = playspace?.Snapshot;
            if (space is not null) { spaceTimeline.Add(FormattableString.Invariant($"{clock.Elapsed.TotalSeconds:F1}s:{space.State},enabled={space.Enabled},xyz={space.OffsetX:F3}/{space.OffsetY:F3}/{space.OffsetZ:F3}")); }
            if (seconds == 20 && (index + 1) % 5 == 0) { progress?.Report($"正在记录复现过程，已采集 {index + 1}/20 秒。请继续按原步骤操作。"); }
        }
        PhoneOverlaySnapshot currentVideo = video.Snapshot;
        PhoneAudioSnapshot currentAudio = audio.Snapshot;
        PhoneControlSnapshot currentControl = control.Snapshot;
        TroubleshootingEvidence[] evidence =
        [
            Connection(phone.Snapshot),
            new("video", "video", FormattableString.Invariant($"before={firstVideo.State};after={currentVideo.State};reason={Code(currentVideo.ReasonCode)};framesDelta={currentVideo.SubmittedFrames - firstVideo.SubmittedFrames};droppedDelta={currentVideo.DroppedFrames - firstVideo.DroppedFrames};noFrameSamples={stalledSamples};seconds={clock.Elapsed.TotalSeconds:F1};deviceChanged={deviceChanged};width={currentVideo.Width};height={currentVideo.Height};latencyMs={currentVideo.AverageLatencyMilliseconds:F1};hasLatencySamples={currentVideo.HasLatencySamples};latencySampleCount={currentVideo.LatencySampleCount};unavailableLatencySampleCount={currentVideo.UnavailableLatencySampleCount};rejectedLatencySampleCount={currentVideo.RejectedLatencySampleCount};latencyScope=session-mean-capture-to-submit;counterResetPossible=true;staticImageMayProduceNoFrames=true")),
            new("audio", "audio", FormattableString.Invariant($"before={firstAudio.State};after={currentAudio.State};reason={Code(currentAudio.ReasonCode)};packetsDelta={currentAudio.DecodedPackets - firstAudio.DecodedPackets};droppedDelta={currentAudio.DroppedBuffers - firstAudio.DroppedBuffers};bufferMs={currentAudio.BufferedDuration.TotalMilliseconds:F0};outputSelected={!string.IsNullOrEmpty(currentAudio.OutputDeviceId)};audibilityNotVerified=true")),
            new("control", "control", FormattableString.Invariant($"before={firstControl.State};after={currentControl.State};reason={Code(currentControl.ReasonCode)};sentDelta={currentControl.SentCommands - firstControl.SentCommands};pressDelta={(currentVideo.InputPath?.ObservedPressEdges ?? 0) - (firstVideo.InputPath?.ObservedPressEdges ?? 0)};rayHitsDelta={(currentVideo.InputPath?.RayHitSamples ?? 0) - (firstVideo.InputPath?.RayHitSamples ?? 0)};rejected={currentControl.Dispatch?.RejectedCommands};permissionRefusal={currentControl.ServerOutput?.InputPermissionRefusalObserved};queueMs={currentControl.LastQueueDelayMilliseconds:F1};phoneExecutionNotAcknowledged=true")),
            InputEvidence(firstVideo, currentVideo),
            PermissionEvidence(firstControl.ServerOutput, currentControl.ServerOutput),
            new("timeline", "timeline", BoundTimeline(timeline) + LogCoverage(firstLog, logHealth?.Invoke())),
            SpaceEvidence(),
            new("spaceTimeline", "playspace_timeline", spaceTimeline.Count > 0 ? BoundTimeline(spaceTimeline) : "not_observed"),
        ];
        evidence[1] = evidence[1] with
        {
            Observation = evidence[1].Observation +
            $";encodedBytesDelta={currentVideo.TotalEncodedBytes - firstVideo.TotalEncodedBytes};decoder={Code(currentVideo.VideoDecoderBackend ?? "unknown")};" +
            $"requestedResolution={currentVideo.RequestedResolutionPercent};requestedMbps={currentVideo.RequestedBitrateMbps};requestedFps={currentVideo.RequestedMaximumFramesPerSecond};decodeStageNotMeasured=true"
        };
        evidence[3] = evidence[3] with { Observation = evidence[3].Observation + ControlDetails(currentControl) };
        for (int i = 0; i < evidence.Length; i++)
        {
            if (evidence[i].Observation.Length > 1024)
            { evidence[i] = evidence[i] with { Observation = evidence[i].Observation[..970] + ";observation_truncated=true" }; }
        }
        string summary = deviceChanged ? "观察期间手机已切换，前后数据不能直接比较。" :
            $"已完成{seconds}秒运行观察，记录将交给AI结合你描述的问题分析。";
        return (evidence, summary);
    }

    private static TroubleshootingEvidence Connection(AndroidConnectionSnapshot snapshot) => new("connection", "connection",
        $"state={snapshot.State};reason={Code(snapshot.ReasonCode)};devices={snapshot.Devices.Count};ready={snapshot.Devices.Count(d => d.Status == AndroidDeviceStatus.Ready)};unauthorized={snapshot.Devices.Count(d => d.Status == AndroidDeviceStatus.Unauthorized)};offline={snapshot.Devices.Count(d => d.Status == AndroidDeviceStatus.Offline)};transport={snapshot.SelectedDevice?.Transport};lastSuccessfulScan={snapshot.LastSuccessfulScanAt:O}");

    private static TroubleshootingEvidence Binding(PhoneOverlaySnapshot snapshot) => new("bindings", "bindings",
        $"scope=phone_overlay;state={snapshot.BindingState};reason={Code(snapshot.BindingReasonCode)};inputReady={snapshot.SteamVrInputReady};pointerBound={snapshot.PointerPoseBound};pointerActive={snapshot.PointerPoseActive};touchBound={snapshot.TouchBound};touchActive={snapshot.TouchActive}");

    private static string Code(string text) => new(text.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Take(80).ToArray());

    private TroubleshootingEvidence SpaceEvidence()
    {
        OpenVrPlayspaceDragSnapshot? space = playspace?.Snapshot;
        return new("playspace", "playspace", FormattableString.Invariant($"observed={space is not null};state={space?.State};reason={Code(space?.ReasonCode ?? "unknown")};enabled={space?.Enabled};multiplier={space?.Multiplier};offsetX={space?.OffsetX};offsetY={space?.OffsetY};offsetZ={space?.OffsetZ};inertiaEnabled={space?.FlingEnabled};flingStrength={space?.MotionOptions?.FlingStrength};gravity={space?.MotionOptions?.Gravity};friction={space?.MotionOptions?.Friction};rawControllerPoseNotCollected=true"));
    }

    private TroubleshootingEvidence SettingsEvidence()
    {
        AppSettings? current = settings?.Snapshot.Value;
        return new("settings", "settings", $"observed={current is not null};autoOpen={current?.AutoOpenPhoneOverlay};hand={current?.ControllerHand};resolutionPercent={current?.VideoResolutionPercent};bitrateMbps={current?.VideoBitrateMbps};maximumFps={current?.VideoMaximumFramesPerSecond};inertiaEnabled={current?.PlayspaceInertiaEnabled};multiplier={current?.PlayspaceMultiplier};settingsPersisted={settings?.Snapshot.IsPersisted}");
    }

    private static TroubleshootingEvidence InputEvidence(PhoneOverlaySnapshot before, PhoneOverlaySnapshot after)
    {
        PhoneInputPathDiagnostic? input = after.InputPath;
        InputRoutingDiagnostic? routing = after.InputRouting;
        return Binding(after) with
        {
            Observation = Binding(after).Observation +
            $";inputObserved={input is not null};block={Code(input?.CurrentBlock ?? "unknown")};inputAt={input?.CapturedAt:O};" +
            $"actionErrorsDelta={input?.ActionReadFailures - before.InputPath?.ActionReadFailures};eligibleDelta={input?.EligibleSamples - before.InputPath?.EligibleSamples};" +
            $"sinkFailuresDelta={input?.SinkFailures - before.InputPath?.SinkFailures};awaitingFrame={input?.AwaitingFreshVideoFrame};" +
            $"routingObserved={routing is not null};routingAt={routing?.LastAt:O};receivedDelta={routing?.Received - before.InputRouting?.Received};forwardedDelta={routing?.Forwarded - before.InputRouting?.Forwarded};" +
            $"notReadyDelta={routing?.ControlNotReady - before.InputRouting?.ControlNotReady};geometryMissingDelta={routing?.GeometryMissing - before.InputRouting?.GeometryMissing};decision={Code(routing?.LastDecision ?? "unknown")};emptyDeltaMeansUnknown=true"
        };
    }

    private static string ControlDetails(PhoneControlSnapshot snapshot)
    {
        ControlGeometryDiagnostic? geometry = snapshot.Geometry;
        InputDispatchDiagnostic? dispatch = snapshot.Dispatch;
        return $";geometryObserved={geometry is not null};native={geometry?.NativeWidth}x{geometry?.NativeHeight};video={geometry?.VideoWidth}x{geometry?.VideoHeight};" +
            $"rotation={geometry?.Rotation};sizeUnverified={geometry?.NativeSizeUnverified};geometryAt={geometry?.LastVerifiedAt:O};" +
            $"geometryTimeouts={geometry?.ConsecutiveRefreshTimeouts};waitNewDown={geometry?.WaitingForNewDown};geometryReason={Code(geometry?.Reason ?? "unknown")};" +
            $"dispatchObserved={dispatch is not null};queueDepth={dispatch?.ReliableDepth};queueCapacity={dispatch?.ReliableCapacity};terminalDepth={dispatch?.TerminalDepth};" +
            $"suppression={Code(dispatch?.LastSuppressionReason ?? "unknown")};failure={Code(dispatch?.FailureReason ?? "unknown")};cumulativeCountersNotProofOfCurrentFailure=true";
    }

    internal static TroubleshootingEvidence PermissionEvidence(ServerOutputDiagnostic? before, ServerOutputDiagnostic? after)
    {
        ServerOutputError? previous = before?.Errors.FirstOrDefault(e => e.Category == "input-permission-denied");
        ServerOutputError? current = after?.Errors.FirstOrDefault(e => e.Category == "input-permission-denied");
        bool comparable = before is not null && after is not null && before.SessionId is { Length: > 0 } &&
            before.SessionId == after.SessionId && after.CapturedAt >= before.CapturedAt &&
            (current?.ObservedLines ?? 0) >= (previous?.ObservedLines ?? 0);
        long? delta = comparable ? (current?.ObservedLines ?? 0) - (previous?.ObservedLines ?? 0) : null;
        return new("permissionWindow", "control_permission", FormattableString.Invariant(
            $"observed={after is not null};sessionComparable={comparable};refusalObserved={current is not null};refusalLinesDelta={delta};lastRefusalAt={current?.LastAt:O};windowStart={before?.CapturedAt:O};windowEnd={after?.CapturedAt:O};linesAreNotRejectedCommandCount=true;historicalFlagAloneDoesNotProveCurrentRefusal=true"));
    }

    private static string LogCoverage(DiagnosticLogHealth? before, DiagnosticLogHealth? after) =>
        $";logObserved={after is not null};writeFailuresDelta={after?.WriteFailures - before?.WriteFailures};logDroppedDelta={after?.Dropped - before?.Dropped};logPending={after?.Pending};logFlushedAt={after?.LastFlushAt:O}";

    private static string BoundTimeline(List<string> samples)
    {
        string text = string.Join('|', samples);
        return text.Length <= 700 ? text : text[..700] + ";timeline_truncated=true";
    }
}

using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneMediaSessionCoordinatorTests
{
    [Fact]
    public async Task LostDeviceReleasesChannelsEvenWithoutAnAutomaticCandidate()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection);
        await coordinator.StartAsync("device", new(), CancellationToken.None);
        operations.Clear();
        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        await WaitUntilAsync(() => operations.Contains("control.stop"), TimeSpan.FromSeconds(3));
        Assert.DoesNotContain("overlay.start", operations);
        Assert.Contains("audio.stop", operations);
        Assert.Equal(PhoneMediaSessionState.WaitingForDevice, coordinator.Snapshot.State);
        connection.Publish(AndroidConnectionState.Ready, "other");
        await WaitUntilAsync(() => coordinator.Snapshot.State == PhoneMediaSessionState.Running, TimeSpan.FromSeconds(3));
        Assert.Contains("audio.start", operations);
    }

    [Fact]
    public async Task ManualCloseHoldsThroughScansAndNewSelectionUntilAnExplicitMethodRequest()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection, () => true);
        coordinator.ConfigureAutoStart(true, new());
        await coordinator.StartAsync("device", new(), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
        operations.Clear();
        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        connection.Publish(AndroidConnectionState.Ready, "other");
        coordinator.ConfigureAutoStart(true, new()); // Reapplying settings is not a new intent.
        await Task.Delay(3200);
        Assert.DoesNotContain("overlay.start", operations);
        coordinator.RequestAutomaticStart();
        await WaitUntilAsync(() => coordinator.Snapshot.State == PhoneMediaSessionState.Running, TimeSpan.FromSeconds(4));
        Assert.Single(operations, operation => operation == "overlay.start");
    }

    [Fact]
    public async Task AutomaticSelectionOfAnotherPhoneRestoresAllOwnedChannels()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection);
        await coordinator.StartAsync("device", new(), CancellationToken.None);
        operations.Clear();
        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        connection.Publish(AndroidConnectionState.Ready, "other");
        await WaitUntilAsync(() => operations.Contains("control.start"), TimeSpan.FromSeconds(3));
        Assert.True(operations.IndexOf("overlay.stop") < operations.IndexOf("overlay.start"));
        Assert.Contains("audio.stop", operations);
        Assert.Contains("audio.start", operations);
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task RepeatedStartOnTheRunningSelectedDeviceDoesNotReopenChannels()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection);
        await coordinator.StartAsync("device", new(), CancellationToken.None);
        operations.Clear();
        await coordinator.StartAsync("device", new(), CancellationToken.None);
        Assert.Empty(operations);
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task ConnectionManagementDoesNotSuppressTheConfiguredInitialAutomaticOverlayStart()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection);
        coordinator.ConfigureAutoStart(true, new());
        await coordinator.StopForConnectionChangeAsync(CancellationToken.None);
        connection.Publish(AndroidConnectionState.Ready, "device");
        await WaitUntilAsync(() => coordinator.Snapshot.State == PhoneMediaSessionState.Running, TimeSpan.FromSeconds(5));
        Assert.Contains("overlay.start", operations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectingAnotherAdbConnectionStopsOldChannelsBeforeSelectionAndReopensOnlyIfActive(bool active)
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        connection.AddUnselectedDevice();
        FakeAndroidMediaPlayback playback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control, playback, connection);
        if (active) { await coordinator.StartAsync("device", new(), CancellationToken.None); }
        operations.Clear();
        connection.Selecting = key =>
        {
            Assert.Equal("other", key);
            Assert.Contains("audio.stop", operations);
            Assert.Contains("overlay.stop", operations);
            Assert.Contains("control.stop", operations);
            Assert.DoesNotContain("overlay.start", operations);
            operations.Add("select.other");
        };
        await coordinator.SwitchDeviceAsync("other", () =>
        {
            Assert.Equal("other", connection.Snapshot.SelectedDevice?.DeviceKey);
            return new();
        }, CancellationToken.None);
        Assert.Equal("other", connection.Snapshot.SelectedDevice?.DeviceKey);
        Assert.Equal(active ? PhoneMediaSessionState.Running : PhoneMediaSessionState.Stopped, coordinator.Snapshot.State);
        if (active)
        {
            Assert.True(operations.IndexOf("select.other") < operations.IndexOf("overlay.start"));
            Assert.Contains("audio.start", operations);
            Assert.Contains("control.start", operations);
            Assert.DoesNotContain("device", playback.ResumedDevices);
        }
        else { Assert.DoesNotContain("overlay.start", operations); }
        operations.Clear();
        await coordinator.SwitchDeviceAsync("other", () => new(), CancellationToken.None);
        Assert.Empty(operations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionOrNewStartFailureLeavesAllChannelsClosed(bool failSelection)
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        connection.AddUnselectedDevice();
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection);
        await coordinator.StartAsync("device", new(), CancellationToken.None);
        connection.FailSelection = failSelection;
        overlay.FailStart = !failSelection;
        await Assert.ThrowsAnyAsync<Exception>(() => coordinator.SwitchDeviceAsync("other", () => new(), CancellationToken.None).AsTask());
        Assert.Equal(PhoneMediaSessionState.Faulted, coordinator.Snapshot.State);
        Assert.Equal(PhoneOverlayState.Stopped, overlay.Snapshot.State);
        Assert.Equal("control.stop", operations[^1]);
        int starts = operations.Count(value => value == "overlay.start");
        connection.Publish(AndroidConnectionState.Ready, "device");
        await Task.Delay(850);
        Assert.Equal(starts, operations.Count(value => value == "overlay.start"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablingOrStoppingAnInFlightAutoStartCancelsItsRecoveryIntent(bool manualStop)
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations) { BlockStart = true };
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = new(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection, () => true);
        coordinator.ConfigureAutoStart(true, new());
        await WaitUntilAsync(() => coordinator.Snapshot.State == PhoneMediaSessionState.Starting, TimeSpan.FromSeconds(5));
        if (manualStop) { await coordinator.StopAsync(CancellationToken.None); }
        else { coordinator.ConfigureAutoStart(false, new()); }
        await WaitUntilAsync(() => coordinator.Snapshot.State == PhoneMediaSessionState.Stopped, TimeSpan.FromSeconds(2));
        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        connection.Publish(AndroidConnectionState.Ready, "device");
        await Task.Delay(1100);
        Assert.Equal(1, operations.Count(item => item == "overlay.start"));
        Assert.Contains("overlay.stop", operations);
    }

    [Fact]
    public async Task ManualAndAutomaticStartWaitForSteamVrEvenWhenAdbAndPhoneAreReady()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback playback = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        bool steamVrReady = false;
        await using PhoneMediaSessionCoordinator coordinator = new(overlay, audio, control, playback, connection, () => steamVrReady);
        Assert.False(coordinator.CanStart("device"));
        PhoneOverlayServiceException failure = await Assert.ThrowsAsync<PhoneOverlayServiceException>(
            () => coordinator.StartAsync("device", new(), CancellationToken.None).AsTask());
        Assert.Equal(PhoneReasonCodes.MediaStartNotReady, failure.ReasonCode);
        coordinator.ConfigureAutoStart(true, new());
        await Task.Delay(3200);
        Assert.Empty(operations);
        steamVrReady = true;
        await WaitUntilAsync(() => coordinator.Snapshot.State == PhoneMediaSessionState.Running, TimeSpan.FromSeconds(5));
        Assert.Equal(1, operations.Count(item => item == "overlay.start"));
    }

    [Theory]
    [InlineData(AndroidConnectionState.AuthorizationRequired)]
    [InlineData(AndroidConnectionState.Faulted)]
    [InlineData(AndroidConnectionState.WaitingForDevice)]
    public async Task ManualStartRejectsUnavailableAdbOrPhone(AndroidConnectionState state)
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(state, "device");
        await using PhoneMediaSessionCoordinator coordinator = new(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection, () => true);
        Assert.False(coordinator.CanStart("device"));
        await Assert.ThrowsAsync<PhoneOverlayServiceException>(() => coordinator.StartAsync("device", new(), CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<PhoneOverlayServiceException>(() => coordinator.RestartScreenAsync("device", new(), null, CancellationToken.None).AsTask());
        Assert.Empty(operations);
    }

    [Fact]
    public async Task ExistingSessionReconnectWaitsForSteamVrBeforeStartingChannels()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        bool steamVrReady = true;
        await using PhoneMediaSessionCoordinator coordinator = new(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection, () => steamVrReady);
        await coordinator.StartAsync("device", new(), CancellationToken.None);
        steamVrReady = false;
        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        connection.Publish(AndroidConnectionState.Ready, "device");
        await Task.Delay(1100);
        Assert.Equal(1, operations.Count(item => item == "overlay.start"));
        steamVrReady = true;
        await WaitUntilAsync(() => operations.Count(item => item == "overlay.start") == 2, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task FailedAutomaticStartDoesNotReconnectAfterDisablingTheSetting()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations) { FailStart = true };
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = new(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection, () => true);
        coordinator.ConfigureAutoStart(true, new());
        await WaitUntilAsync(() => coordinator.Snapshot.State == PhoneMediaSessionState.Faulted, TimeSpan.FromSeconds(5));
        Assert.Equal(PhoneReasonCodes.MediaAutoStartFailed, coordinator.Snapshot.ReasonCode);
        Assert.Contains("overlay.stop", operations);
        Assert.Contains("audio.stop", operations);
        Assert.Contains("control.stop", operations);
        coordinator.ConfigureAutoStart(false, new());
        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        connection.Publish(AndroidConnectionState.Ready, "device");
        await Task.Delay(3300);
        Assert.Equal(1, operations.Count(item => item == "overlay.start"));
        await coordinator.DisposeAsync();
        Assert.Equal(PhoneMediaSessionState.Stopped, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task ShutdownStillCleansAllChannelsWhenTheAutoStartObserverFaults()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations) { FailStart = true };
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = new(overlay, audio, control,
            new FakeAndroidMediaPlayback(operations), connection, () => true);
        TaskCompletionSource observerFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, args) =>
        {
            if (args.Snapshot.State == PhoneMediaSessionState.Faulted)
            {
                observerFailed.TrySetResult();
                throw new InvalidOperationException("Synthetic observer failure.");
            }
        };
        coordinator.ConfigureAutoStart(true, new());
        await observerFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.DisposeAsync();
        Assert.Equal(PhoneMediaSessionState.Stopped, coordinator.Snapshot.State);
        Assert.True(operations.Count(item => item == "overlay.stop") >= 2);
        Assert.True(operations.Count(item => item == "audio.stop") >= 2);
        Assert.True(operations.Count(item => item == "control.stop") >= 2);
    }

    [Fact]
    public async Task AutomaticallyStartsReadyPhoneOnceAndHonorsManualCloseAcrossReconnect()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback playback = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control, playback, connection, () => true);
        coordinator.ConfigureAutoStart(true, new AndroidVideoOptions());
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, args) =>
        {
            if (args.Snapshot.State == PhoneMediaSessionState.Running) { started.TrySetResult(); }
        };
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync(CancellationToken.None);
        connection.Publish(AndroidConnectionState.Discovering, "device");
        connection.Publish(AndroidConnectionState.Ready, "device");
        await Task.Delay(3400);
        Assert.Equal(PhoneMediaSessionState.Stopped, coordinator.Snapshot.State);
    }
    [Fact]
    public async Task AddingUnselectedPhoneDoesNotRestartReadySelectedPhone()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay, audio, control, mediaPlayback, connection);
        await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None);
        operations.Clear();

        connection.AddUnselectedDevice();
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        Assert.Empty(operations);
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task StaleDisconnectEventDoesNotCancelCurrentReconnect()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using FakeAndroidConnectionService connection = new();
        AndroidConnectionSnapshot staleDisconnected = connection.Snapshot;
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay, audio, control, mediaPlayback, connection);
        await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None);
        operations.Clear();
        TaskCompletionSource recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, args) =>
        {
            if (args.Snapshot.State == PhoneMediaSessionState.Running)
            {
                recovered.TrySetResult();
            }
        };

        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        connection.Publish(AndroidConnectionState.Ready, "device");
        connection.Replay(staleDisconnected);
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, operations.Count(operation => operation == "overlay.start"));
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task DelayedDisconnectNotificationStillRestoresTheNewReadySession()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay, audio, control, mediaPlayback, connection);
        await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None);
        operations.Clear();
        TaskCompletionSource recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, args) =>
        {
            if (args.Snapshot.State == PhoneMediaSessionState.Running)
            {
                recovered.TrySetResult();
            }
        };

        AndroidConnectionSnapshot disconnected = connection.SetState(AndroidConnectionState.WaitingForDevice, null);
        AndroidConnectionSnapshot reconnected = connection.SetState(AndroidConnectionState.Ready, "device");
        // The live snapshot is already Ready, but these are unseen notifications
        // from a real intervening disconnect, delivered in revision order.
        connection.Replay(disconnected);
        connection.Replay(reconnected);
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, operations.Count(operation => operation == "overlay.start"));
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task SwitchingDevicesDiscardsThePreviousDevicesPendingResume()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control, mediaPlayback);
        await coordinator.StartAsync("device-a", new AndroidVideoOptions(), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
        operations.Clear();

        await coordinator.StartAsync("device-b", new AndroidVideoOptions(), CancellationToken.None);

        Assert.Empty(mediaPlayback.ResumedDevices);
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
        await coordinator.StopAsync(CancellationToken.None);
        await coordinator.StartAsync("device-b", new AndroidVideoOptions(), CancellationToken.None);
        Assert.Equal(["device-b"], mediaPlayback.ResumedDevices);
    }

    [Fact]
    public async Task FailedPlaybackResumeStopsStartedChannelsAndCanRetryTheSameLease()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control, mediaPlayback);
        await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
        mediaPlayback.FailResume = true;

        await Assert.ThrowsAsync<AndroidConnectionException>(async () =>
            await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None));

        Assert.Equal(PhoneMediaSessionState.Faulted, coordinator.Snapshot.State);
        Assert.Equal(PhoneOverlayState.Stopped, overlay.Snapshot.State);
        Assert.Equal(PhoneAudioState.Stopped, audio.Snapshot.State);
        Assert.Equal(PhoneControlState.Stopped, control.Snapshot.State);
        mediaPlayback.FailResume = false;
        await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None);
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
        Assert.Equal(1, operations.Count(operation => operation == "phone.pause"));
        Assert.Equal(["device", "device"], mediaPlayback.ResumedDevices);
    }

    [Fact]
    public async Task StartingAnotherDeviceStopsThePreviouslyOwnedChannels()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control, mediaPlayback);
        await coordinator.StartAsync("device-a", new AndroidVideoOptions(), CancellationToken.None);
        operations.Clear();

        await coordinator.StartAsync("device-b", new AndroidVideoOptions(), CancellationToken.None);

        Assert.Equal(
            ["audio.stop", "overlay.stop", "control.stop", "overlay.start", "audio.start", "control.start"],
            operations);
        Assert.Empty(mediaPlayback.ResumedDevices);
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task FailedScreenPlaybackResumeStopsScreenAndPreservesSameDeviceAudio()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control, mediaPlayback);
        await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None);
        mediaPlayback.FailResume = true;

        await Assert.ThrowsAsync<AndroidConnectionException>(async () =>
            await coordinator.RestartScreenAsync("device", new AndroidVideoOptions(), null, CancellationToken.None));

        Assert.Equal(PhoneMediaSessionState.Faulted, coordinator.Snapshot.State);
        Assert.Equal(PhoneOverlayState.Stopped, overlay.Snapshot.State);
        Assert.Equal(PhoneAudioState.Playing, audio.Snapshot.State);
        Assert.Equal(PhoneControlState.Stopped, control.Snapshot.State);
        mediaPlayback.FailResume = false;
        await coordinator.RestartScreenAsync("device", new AndroidVideoOptions(), null, CancellationToken.None);
        Assert.Equal(1, operations.Count(operation => operation == "phone.pause"));
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task FailedScreenRestartToAnotherDeviceDoesNotLeaveItsAudioRunning()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control, mediaPlayback);
        await coordinator.StartAsync("device-a", new AndroidVideoOptions(), CancellationToken.None);
        mediaPlayback.FailResume = true;

        await Assert.ThrowsAsync<AndroidConnectionException>(async () =>
            await coordinator.RestartScreenAsync("device-b", new AndroidVideoOptions(), null, CancellationToken.None));

        Assert.Equal(["device-b"], mediaPlayback.ResumedDevices);
        Assert.Equal(PhoneMediaSessionState.Faulted, coordinator.Snapshot.State);
        Assert.Equal(PhoneOverlayState.Stopped, overlay.Snapshot.State);
        Assert.Equal(PhoneAudioState.Stopped, audio.Snapshot.State);
        Assert.Equal(PhoneControlState.Stopped, control.Snapshot.State);
    }

    [Fact]
    public async Task CancellationDuringPlaybackResumeReleasesChannelsAndDoesNotReconnect()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay, audio, control, mediaPlayback, connection);
        await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        mediaPlayback.BeforeResume = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await coordinator.StartAsync("device", new AndroidVideoOptions(), cancellation.Token));

        Assert.Equal(PhoneOverlayState.Stopped, overlay.Snapshot.State);
        Assert.Equal(PhoneAudioState.Stopped, audio.Snapshot.State);
        Assert.Equal(PhoneControlState.Stopped, control.Snapshot.State);
        operations.Clear();
        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        connection.Publish(AndroidConnectionState.Ready, "device");
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        Assert.Empty(operations);
    }

    [Fact]
    public async Task CancellationDuringStopStillReleasesAllOwnedChannels()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(overlay, audio, control, mediaPlayback);
        await coordinator.StartAsync("device", new AndroidVideoOptions(), CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        mediaPlayback.BeforePause = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await coordinator.StopAsync(cancellation.Token));

        Assert.Equal(PhoneMediaSessionState.Stopped, coordinator.Snapshot.State);
        Assert.Equal(PhoneOverlayState.Stopped, overlay.Snapshot.State);
        Assert.Equal(PhoneAudioState.Stopped, audio.Snapshot.State);
        Assert.Equal(PhoneControlState.Stopped, control.Snapshot.State);
    }

    [Fact]
    public async Task ReadyTransitionRestoresDesiredMediaSessionForSameDevice()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using FakeAndroidConnectionService connection = new();
        connection.Publish(AndroidConnectionState.Ready, "device");
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay,
            audio,
            control,
            mediaPlayback,
            connection);
        await coordinator.StartAsync(
            "device",
            new AndroidVideoOptions(),
            CancellationToken.None);
        operations.Clear();

        connection.Publish(AndroidConnectionState.WaitingForDevice, null);
        Assert.Equal(PhoneMediaSessionState.WaitingForDevice, coordinator.Snapshot.State);
        connection.Publish(AndroidConnectionState.Ready, "device");

        await WaitUntilAsync(
            () => operations.Count(operation => operation == "control.start") == 1,
            TimeSpan.FromSeconds(2));
        Assert.Equal(
            [
                "audio.stop",
                "overlay.stop",
                "control.stop",
                "overlay.start",
                "audio.start",
                "control.start",
            ],
            operations);
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task ScreenRestartPausesPhonePlaybackAndLeavesPcAudioRunning()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay,
            audio,
            control,
            mediaPlayback);

        await coordinator.RestartScreenAsync(
            "device",
            new AndroidVideoOptions(),
            _ =>
            {
                operations.Add("reconfigure");
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(
            [
                "phone.pause",
                "overlay.stop",
                "control.stop",
                "reconfigure",
                "overlay.start",
                "control.start",
                "phone.resume",
            ],
            operations);
        Assert.Equal(PhoneMediaSessionState.Running, coordinator.Snapshot.State);
        Assert.DoesNotContain("audio.stop", operations);
        Assert.DoesNotContain("audio.start", operations);
    }

    [Fact]
    public async Task ClosingOverlayPausesPhonePlaybackAndOpeningResumesAfterFirstFrame()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations);
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay,
            audio,
            control,
            mediaPlayback);
        await coordinator.StartAsync(
            "device",
            new AndroidVideoOptions(),
            CancellationToken.None);
        operations.Clear();

        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(
            ["phone.pause", "audio.stop", "overlay.stop", "control.stop"],
            operations);
        operations.Clear();

        await coordinator.StartAsync(
            "device",
            new AndroidVideoOptions(),
            CancellationToken.None);

        Assert.Equal(
            ["overlay.start", "audio.start", "control.start", "phone.resume"],
            operations);
    }

    [Fact]
    public async Task FailedScreenResumeKeepsPhonePlaybackPausedAndReportsFault()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations) { FailStart = true };
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay,
            audio,
            control,
            mediaPlayback);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.RestartScreenAsync(
                "device",
                new AndroidVideoOptions(),
                null,
                CancellationToken.None));

        Assert.Equal(
            ["phone.pause", "overlay.stop", "control.stop", "overlay.start"],
            operations);
        Assert.Equal(PhoneMediaSessionState.Faulted, coordinator.Snapshot.State);
        Assert.Equal("PHONE_MEDIA_SCREEN_RESTART_FAILED", coordinator.Snapshot.ReasonCode);
        Assert.DoesNotContain("audio.stop", operations);
        Assert.DoesNotContain("audio.start", operations);
    }

    [Fact]
    public async Task RetryAfterFailedScreenResumeReusesPauseAndResumesOriginalPlayback()
    {
        List<string> operations = [];
        await using FakeOverlayService overlay = new(operations) { FailStart = true };
        await using FakeAudioService audio = new(operations);
        await using FakeControlService control = new(operations);
        FakeAndroidMediaPlayback mediaPlayback = new(operations);
        await using PhoneMediaSessionCoordinator coordinator = CreateCoordinator(
            overlay,
            audio,
            control,
            mediaPlayback);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.RestartScreenAsync(
                "device",
                new AndroidVideoOptions(),
                null,
                CancellationToken.None));

        overlay.FailStart = false;
        await coordinator.RestartScreenAsync(
            "device",
            new AndroidVideoOptions(),
            null,
            CancellationToken.None);

        Assert.Equal(1, operations.Count(operation => operation == "phone.pause"));
        Assert.Equal(1, operations.Count(operation => operation == "phone.resume"));
        Assert.DoesNotContain("audio.stop", operations);
        Assert.DoesNotContain("audio.start", operations);
    }

    private sealed class FakeAndroidMediaPlayback(List<string> operations) :
        IAndroidMediaPlaybackControl
    {
        public bool FailResume { get; set; }

        public Action? BeforePause { get; set; }

        public Action? BeforeResume { get; set; }

        public List<string?> ResumedDevices { get; } = [];

        public ValueTask<AndroidMediaPauseLease> PauseForVrInterruptionAsync(
            string? deviceKey,
            CancellationToken cancellationToken)
        {
            operations.Add("phone.pause");
            BeforePause?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new AndroidMediaPauseLease(
                deviceKey,
                true,
                "TEST",
                "test"));
        }

        public ValueTask ResumeAfterVrReadyAsync(
            AndroidMediaPauseLease lease,
            CancellationToken cancellationToken)
        {
            operations.Add("phone.resume");
            ResumedDevices.Add(lease.DeviceKey);
            BeforeResume?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (FailResume)
            {
                throw new AndroidConnectionException(AndroidReasonCodes.MediaResumeFailed, "resume failed");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAndroidConnectionService : IAndroidConnectionService
    {
        public Action<string>? Selecting { get; set; }
        public bool FailSelection { get; set; }
        public AndroidConnectionSnapshot Snapshot { get; private set; } = CreateSnapshot(
            AndroidConnectionState.WaitingForDevice,
            null);

        public event EventHandler<AndroidConnectionChangedEventArgs>? StateChanged;

        public void Publish(AndroidConnectionState state, string? deviceKey)
        {
            SetState(state, deviceKey);
            StateChanged?.Invoke(this, new AndroidConnectionChangedEventArgs(Snapshot));
        }

        public AndroidConnectionSnapshot SetState(AndroidConnectionState state, string? deviceKey)
        {
            Snapshot = CreateSnapshot(state, deviceKey) with { Revision = Snapshot.Revision + 1 };
            return Snapshot;
        }

        public void AddUnselectedDevice()
        {
            Snapshot = Snapshot with
            {
                Revision = Snapshot.Revision + 1,
                ReasonCode = AndroidReasonCodes.ReadyMultiple,
                Devices = [.. Snapshot.Devices,
                    new("other", "other", "other", AndroidDeviceStatus.Ready, AndroidTransport.Usb, false)],
            };
            StateChanged?.Invoke(this, new AndroidConnectionChangedEventArgs(Snapshot));
        }

        public void Replay(AndroidConnectionSnapshot snapshot) =>
            StateChanged?.Invoke(this, new AndroidConnectionChangedEventArgs(snapshot));

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask SelectDeviceAsync(
            string deviceKey,
            CancellationToken cancellationToken)
        {
            Selecting?.Invoke(deviceKey);
            if (!FailSelection) { Publish(AndroidConnectionState.Ready, deviceKey); }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static AndroidConnectionSnapshot CreateSnapshot(
            AndroidConnectionState state,
            string? deviceKey)
        {
            AndroidDeviceDetails? details = deviceKey is null
                ? null
                : new AndroidDeviceDetails(
                    deviceKey,
                    "test",
                    "test",
                    "test",
                    "test",
                    "16",
                    36,
                    "arm64-v8a",
                    AndroidTransport.Usb);
            return new AndroidConnectionSnapshot(
                1,
                state,
                "TEST",
                "test",
                deviceKey is null ? [] :
                    [new(deviceKey, "test", "test", AndroidDeviceStatus.Ready, AndroidTransport.Usb, true)],
                details,
                DateTimeOffset.UtcNow,
                state == AndroidConnectionState.Ready ? DateTimeOffset.UtcNow : null);
        }
    }

    private static PhoneMediaSessionCoordinator CreateCoordinator(IPhoneOverlayService overlay, IPhoneAudioService audio,
        IPhoneControlService control, IAndroidMediaPlaybackControl playback, IAndroidConnectionService? connection = null,
        Func<bool>? runtimeReady = null) => new(overlay, audio, control, playback, connection, runtimeReady,
            deviceKey => connection is null || PhoneStartReadiness.CanStart(connection.Snapshot, runtimeReady?.Invoke() ?? true, deviceKey));
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using CancellationTokenSource cancellation = new(timeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
        }
    }

    private sealed class FakeOverlayService(List<string> operations) : IPhoneOverlayService
    {
        public ValueTask SwitchControllerHandAsync(OpenVrControllerHand hand, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public OpenVrBindingResult ConfigureSteamVrAutoLaunch(bool enabled) => new(true, "TEST_STARTUP", "test");
        public bool FailStart { get; set; }
        public bool BlockStart { get; set; }

        public PhoneOverlaySnapshot Snapshot { get; private set; } = new(
            PhoneOverlayState.Stopped,
            "TEST",
            "test");

        public PhoneOverlaySnapshot DiagnosticSnapshot => Snapshot;

        public event EventHandler<PhoneOverlayChangedEventArgs>? StateChanged;

        public async ValueTask StartAsync(
            string? deviceKey,
            AndroidVideoOptions options,
            CancellationToken cancellationToken)
        {
            operations.Add("overlay.start");
            if (FailStart)
            {
                throw new InvalidOperationException("start failed");
            }
            if (BlockStart) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }

            Snapshot = Snapshot with { State = PhoneOverlayState.Running };
            StateChanged?.Invoke(this, new PhoneOverlayChangedEventArgs(Snapshot));
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations.Add("overlay.stop");
            Snapshot = Snapshot with { State = PhoneOverlayState.Stopped };
            StateChanged?.Invoke(this, new PhoneOverlayChangedEventArgs(Snapshot));
            return ValueTask.CompletedTask;
        }

        public OpenVrBindingResult OpenBindingUi() => new(true, "TEST", "test");

        public bool HasSavedBindingChanged() => false;

        public OpenVrBindingResult ReloadLocalBinding() => new(true, "TEST", "test");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void ConfigureLockScreenFeatures(
            bool keepAwakeWhileGrabbed,
            bool unlockKeypadEnabled)
        {
        }
    }

    private sealed class FakeAudioService(List<string> operations) : IPhoneAudioService
    {
        public PhoneAudioSnapshot Snapshot { get; private set; } = new(
            PhoneAudioState.Stopped,
            "TEST",
            "test");

        public PhoneAudioSnapshot DiagnosticSnapshot => Snapshot;

        public event EventHandler<PhoneAudioChangedEventArgs>? StateChanged;

        public ValueTask StartAsync(string? deviceKey, CancellationToken cancellationToken)
        {
            operations.Add("audio.start");
            Snapshot = Snapshot with { State = PhoneAudioState.Playing };
            StateChanged?.Invoke(this, new PhoneAudioChangedEventArgs(Snapshot));
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations.Add("audio.stop");
            Snapshot = Snapshot with { State = PhoneAudioState.Stopped };
            StateChanged?.Invoke(this, new PhoneAudioChangedEventArgs(Snapshot));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeControlService(List<string> operations) : IPhoneControlService
    {
        public void ConfigurePhysicalScreen(bool turnOffScreen) { }
        public PhoneControlSnapshot Snapshot { get; private set; } = new(
            PhoneControlState.Stopped,
            "TEST",
            "test",
            null,
            0,
            0,
            0);

        public PhoneControlSnapshot DiagnosticSnapshot => Snapshot;

        public event EventHandler<PhoneControlChangedEventArgs>? StateChanged;

        public ValueTask StartAsync(string? deviceKey, CancellationToken cancellationToken)
        {
            operations.Add("control.start");
            Snapshot = Snapshot with { State = PhoneControlState.Ready };
            StateChanged?.Invoke(this, new PhoneControlChangedEventArgs(Snapshot));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(
            PhoneInputCommandKind kind,
            float normalizedX,
            float normalizedY,
            int screenWidth,
            int screenHeight,
            float scrollDelta,
            CancellationToken cancellationToken,
            int unlockDigit = -1) => ValueTask.CompletedTask;

        public void QueueFromSteamVr(
            PhoneInputCommandKind kind,
            float normalizedX,
            float normalizedY,
            int screenWidth,
            int screenHeight,
            float scrollDelta = 0,
            int unlockDigit = -1)
        {
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations.Add("control.stop");
            Snapshot = Snapshot with { State = PhoneControlState.Stopped };
            StateChanged?.Invoke(this, new PhoneControlChangedEventArgs(Snapshot));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

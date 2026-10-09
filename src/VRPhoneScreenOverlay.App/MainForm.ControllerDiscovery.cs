using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private ControllerDiscoverySnapshot _detectedController = new(false, null, "CONTROLLER_NOT_CONNECTED");
    private bool _controllerMonitorRunning;

    private ControllerBindingGuide DescribeSelectedGuide(ControllerBindingGuide guide, string key) => guide with
    {
        SelectionKey = key,
        Unrecognized = key == OpenVrBindingGuide.GenericKey && _detectedController.Connected &&
            ControllerBindingGuideService.SelectDetectedGroup(_detectedController, null) == OpenVrBindingGuide.GenericKey,
    };

    private async void OnControllerDiscoveryShown(object? sender, EventArgs args)
    {
        if (_controllerMonitorRunning || IsDisposed || Disposing) { return; }
        using UiOperationLifetime.Lease? lease = _uiOperations.TryEnter(_formLifetime.Token);
        if (lease is null) { return; }
        _controllerMonitorRunning = true;
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
            ControllerHandPreference? previousHand = null;
            ControllerDiscoverySnapshot? previous = null;
            do
            {
                ControllerHandPreference hand = _settings.Snapshot.Value.ControllerHand;
                ControllerDiscoverySnapshot detected = await ControllerBindingGuideService.DetectAsync(
                    hand == ControllerHandPreference.Left ? OpenVrControllerHand.Left : OpenVrControllerHand.Right, lease.Token).ConfigureAwait(true);
                if (IsDisposed || Disposing || _bindingGuideView.IsRestoringDefaults) { continue; }
                if (detected == previous && previousHand == hand) { continue; }
                previous = detected;
                previousHand = hand;
                _detectedController = detected;
                int revision = ++_bindingGuideRevision;
                _bindingGuideView.ControllerSelector.TryGetSelectedValue(out string selected);
                ControllerBindingGuide guide = await ControllerBindingGuideService.ReadDetectedAsync(detected, selected, lease.Token).ConfigureAwait(true);
                if (!IsDisposed && !Disposing && revision == _bindingGuideRevision) { _bindingGuideView.SetGuide(guide); }
            } while (await timer.WaitForNextTickAsync(lease.Token).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (lease.Token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing)
            { _bindingGuideView.OperationStatus.Text = "手柄识别暂时不可用，请重新打开软件 [CONTROLLER_MONITOR_FAILED]"; }
        }
        finally { _controllerMonitorRunning = false; }
    }
}

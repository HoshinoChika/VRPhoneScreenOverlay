using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public sealed class PhoneBindingDefaultsService(IBindingDefaultsRestorer defaults, IBindingLiveApplication live)
{
    public PhoneBindingDefaultsService() : this(OpenVrBindingDefaults.CreateDefault(), new OpenVrLiveBindingApplication()) { }

    public Task RestoreAsync(CancellationToken token) => RestoreAsync(null, null, token);

    public async Task RestoreAsync(ControllerDiscoverySnapshot? controller, string? selectedKey, CancellationToken token)
    {
        IBindingDefaultsTransaction? files = null;
        IBindingLiveTransaction? application = null;
        try
        {
            // Capture SteamVR's selection before replacing any user file.
            application = await live.BeginAsync(token).ConfigureAwait(false);
            string[] genericTypes = controller is { Connected: true, ControllerType: { Length: > 0 } type } &&
                ControllerBindingGuideService.SelectDetectedGroup(controller, selectedKey) == OpenVrBindingGuide.GenericKey ? [type] : [];
            files = await defaults.BeginResetAsync(application.ControllerTypes, genericTypes, token).ConfigureAwait(false);
            await application.ApplyAsync(files.RuntimeBindings, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (files.SavedRevision is { } revision) { application.Commit(revision); }
            else { application.Commit(); }
            files.Commit();
        }
        catch (Exception failure)
        {
            List<Exception> recoveryErrors = [];
            // Restore original file bytes before reselecting the old binding.
            // Attempt both layers even when one rollback fails.
            if (files is not null)
            {
                IBindingDefaultsTransaction original = files;
                files = null;
                try { await original.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { recoveryErrors.Add(error); }
            }
            if (application is not null)
            {
                IBindingLiveTransaction original = application;
                application = null;
                try { await original.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { recoveryErrors.Add(error); }
            }
            if (recoveryErrors.Count > 0)
            {
                recoveryErrors.Insert(0, failure);
                throw new SettingsApplyException("BINDING_DEFAULTS_RECOVERY_FAILED", "恢复未完成，原配置恢复失败；请重新打开手柄绑定检查",
                    new AggregateException(recoveryErrors));
            }
            if (failure is OperationCanceledException && token.IsCancellationRequested) { throw; }
            throw new SettingsApplyException("BINDING_DEFAULTS_ROLLED_BACK", "恢复未完成，原配置已保留；请确认 SteamVR 正在运行后重试", failure);
        }
        finally
        {
            if (files is not null) { await files.DisposeAsync().ConfigureAwait(false); }
            if (application is not null) { await application.DisposeAsync().ConfigureAwait(false); }
        }
    }
}

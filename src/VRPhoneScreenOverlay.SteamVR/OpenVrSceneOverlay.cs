using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

public sealed record OpenVrOverlaySettings(
    string Key,
    string Name,
    float WidthMeters = 0.65f,
    float DistanceMeters = 1.1f)
{
    public static OpenVrOverlaySettings PhoneDefault { get; } = new(
        "io.github.vrphonescreen.overlay.phone",
        "VRPhoneScreen Overlay - Phone");
}

public interface IOpenVrSceneOverlay : IDisposable
{
    public ValueTask SwitchControllerHandAsync(OpenVrControllerHand hand, CancellationToken cancellationToken);
    public bool IsVisible { get; }

    public long GraphicsAdapterLuid { get; }

    public OpenVrPhoneInteractionSnapshot Interaction { get; }

    public OpenVrBindingHealthSnapshot BindingHealth { get; }

    public event OpenVrPhoneInputSink? PhoneInputReceived;

    public void ConfigureLockScreenFeatures(
        bool keepAwakeWhileGrabbed,
        bool unlockKeypadEnabled);

    public void Submit(GpuVideoFrame frame);
    public void SetPhoneLocked(bool? locked);
    public void SetScreenGuard(bool enabled, bool faulted);

    public OpenVrBindingResult OpenBindingUi();

    public void Hide();
}

public static class OpenVrSceneOverlayFactory
{
    public static IOpenVrSceneOverlay Create(OpenVrOverlaySettings? settings = null, OpenVrVideoSettingsChannel? videoSettings = null) =>
        new OpenVrSceneOverlay(settings ?? OpenVrOverlaySettings.PhoneDefault, videoSettings);
}

public sealed class OpenVrOverlayException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}

internal sealed class OpenVrSceneOverlay : IOpenVrSceneOverlay
{
    public ValueTask SwitchControllerHandAsync(OpenVrControllerHand hand, CancellationToken cancellationToken)
    {
        OpenVrPhoneInteraction? interaction;
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); interaction = _interaction; }
        return interaction?.SwitchControllerHandAsync(hand, cancellationToken) ??
            ValueTask.FromException(new InvalidOperationException("Phone input unavailable."));
    }
    public void SetScreenGuard(bool enabled, bool faulted)
    {
        lock (_gate) { _interaction?.SetScreenGuard(enabled, faulted); }
    }

    public void SetPhoneLocked(bool? locked)
    {
        lock (_gate) { _interaction?.SetPhoneLocked(locked); }
    }
    private readonly object _gate = OpenVrRuntimeHost.ApiGate;
    private readonly float _shortEdgeMeters;
    private readonly OpenVrRetainedVideoTexture _lastTexture = new();
    private OpenVrPhoneInteraction? _interaction;
    private CVROverlay? _overlay;
    private ulong _handle = OpenVR.k_ulOverlayHandleInvalid;
    private bool _disposed;

    public OpenVrSceneOverlay(OpenVrOverlaySettings settings, OpenVrVideoSettingsChannel? videoSettings = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Key) || string.IsNullOrWhiteSpace(settings.Name))
        {
            throw new ArgumentException("OpenVR overlay key and name must not be empty.", nameof(settings));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.WidthMeters, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.DistanceMeters, 0);
        _shortEdgeMeters = settings.WidthMeters;

        try
        {
            EVRInitError initializationError = EVRInitError.None;
            CVRSystem system = OpenVrRuntimeHost.GetOrStart(ref initializationError);
            if (initializationError != EVRInitError.None)
            {
                throw new OpenVrOverlayException(
                    OpenVrReasonCodes.InitializationFailed,
                    $"SteamVR 初始化失败：{initializationError}");
            }

            ulong rawAdapterLuid = 0;
            system.GetOutputDevice(ref rawAdapterLuid, ETextureType.DirectX, nint.Zero);
            GraphicsAdapterLuid = unchecked((long)rawAdapterLuid);
            _overlay = OpenVR.Overlay ?? throw new OpenVrOverlayException(
                OpenVrReasonCodes.OverlayInterfaceMissing,
                "SteamVR 没有返回浮窗接口");

            Check(
                _overlay.CreateOverlay(settings.Key, settings.Name, ref _handle),
                OpenVrReasonCodes.OverlayCreateFailed,
                "SteamVR 手机浮窗创建失败");
            Check(
                _overlay.SetOverlayWidthInMeters(_handle, settings.WidthMeters),
                OpenVrReasonCodes.OverlayWidthFailed,
                "SteamVR 手机浮窗尺寸设置失败");
            Check(
                _overlay.SetOverlayAlpha(_handle, 1f),
                OpenVrReasonCodes.OverlayAlphaFailed,
                "SteamVR 手机浮窗透明度设置失败");
            Check(
                _overlay.SetOverlayInputMethod(_handle, VROverlayInputMethod.None),
                OpenVrReasonCodes.OverlayInputFailed,
                "SteamVR 手机浮窗输入模式设置失败");
            _interaction = new OpenVrPhoneInteraction(
                _gate,
                system,
                _overlay,
                _handle,
                settings.DistanceMeters,
                SubmitRetainedTexture, videoSettings);
            _interaction.PhoneInputReceived += OnPhoneInputReceived;
        }
        catch (OpenVrOverlayException)
        {
            Dispose();
            throw;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Dispose();
            throw new OpenVrOverlayException(
                OpenVrReasonCodes.NativeLibraryFailed,
                "OpenVR 运行库加载失败",
                exception);
        }
        catch (Exception exception)
        {
            Dispose();
            throw new OpenVrOverlayException(
                OpenVrReasonCodes.InitializationUnexpected,
                "SteamVR 浮窗初始化遇到未预期错误",
                exception);
        }
    }

    public bool IsVisible => _interaction?.PhonePresented ?? false;

    public long GraphicsAdapterLuid { get; }

    public OpenVrPhoneInteractionSnapshot Interaction =>
        _interaction?.Snapshot ?? OpenVrPhoneInteractionSnapshot.Stopped;

    public OpenVrBindingHealthSnapshot BindingHealth =>
        _interaction?.BindingHealth ?? OpenVrBindingHealthSnapshot.Stopped;

    public event OpenVrPhoneInputSink? PhoneInputReceived;

    public void ConfigureLockScreenFeatures(
        bool keepAwakeWhileGrabbed,
        bool unlockKeypadEnabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _interaction?.ConfigureLockScreenFeatures(
            keepAwakeWhileGrabbed,
            unlockKeypadEnabled);
    }

    public void Submit(GpuVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame.NativeTexturePointer == nint.Zero)
        {
            throw new ArgumentException("GPU frame texture pointer must not be zero.", nameof(frame));
        }

        lock (_gate)
        {
            _lastTexture.Update(frame.NativeTexturePointer);
            float widthMeters = CalculateOverlayWidthMeters(
                _shortEdgeMeters,
                frame.Width,
                frame.Height);
            _interaction!.UpdateFrameGeometry(widthMeters, frame.Width, frame.Height);
            _interaction.PrepareVideoSubmission();
            if (IsVisible) { SubmitRetainedTexture(); }
        }
    }

    private void SubmitRetainedTexture()
    {
        if (_lastTexture.Pointer == 0) { return; }
        Texture_t texture = new() { handle = _lastTexture.Pointer, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Auto };
        Check(_overlay!.SetOverlayTexture(_handle, ref texture),
            OpenVrReasonCodes.OverlayTextureFailed, "SteamVR 手机画面提交失败");
    }

    internal static float CalculateOverlayWidthMeters(
        float shortEdgeMeters,
        int frameWidth,
        int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(shortEdgeMeters, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameHeight, 1);

        return frameWidth <= frameHeight
            ? shortEdgeMeters
            : shortEdgeMeters * frameWidth / frameHeight;
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _interaction?.StopPresentation();
        }
    }

    public OpenVrBindingResult OpenBindingUi()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _interaction?.OpenBindingUi() ?? new OpenVrBindingResult(
            false,
            OpenVrReasonCodes.InputNotReady,
            "SteamVR 手柄输入尚未就绪");
    }

    public void Dispose()
    {
        OpenVrPhoneInteraction? interaction;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            interaction = _interaction;
            _interaction = null;
            if (interaction is not null)
            {
                interaction.PhoneInputReceived -= OnPhoneInputReceived;
            }
        }

        interaction?.Dispose();

        lock (_gate)
        {
            if (_overlay is not null && _handle != OpenVR.k_ulOverlayHandleInvalid)
            {
                _ = _overlay.HideOverlay(_handle);
                _ = _overlay.ClearOverlayTexture(_handle);
                _ = _overlay.DestroyOverlay(_handle);
                _handle = OpenVR.k_ulOverlayHandleInvalid;
            }

            _overlay = null;
            _lastTexture.Dispose();
        }
    }

    private void OnPhoneInputReceived(OpenVrPhoneInputCommand command) =>
        PhoneInputReceived?.Invoke(command);

    private static void Check(EVROverlayError error, string reasonCode, string message)
    {
        if (error != EVROverlayError.None)
        {
            throw new OpenVrOverlayException(reasonCode, $"{message}：{error}");
        }
    }
}

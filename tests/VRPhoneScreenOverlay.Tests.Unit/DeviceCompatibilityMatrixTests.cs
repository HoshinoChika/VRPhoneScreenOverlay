using System.Text.Json.Nodes;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class DeviceCompatibilityMatrixTests
{
    [Theory]
    [InlineData("Google", "arm64-v8a", 36)]
    [InlineData("Samsung", "arm64-v8a", 35)]
    [InlineData("Xiaomi", "armeabi-v7a", 29)]
    [InlineData("OnePlus", "arm64-v8a", 33)]
    [InlineData("HONOR", "arm64-v8a", 31)]
    [InlineData("HUAWEI", "arm64-v8a", 28)]
    [InlineData("Lenovo", "x86_64", 30)]
    public void VendorAndAbiPropertyFormatsDoNotChangePlatformCapabilityRules(string vendor, string abi, int sdk)
    {
        IReadOnlyDictionary<string, string> properties = AdbOutputParser.ParseProperties(
            $"[ro.product.manufacturer]: [{vendor}]\r\n[ro.product.brand]: [{vendor}]\r\n" +
            $"[ro.product.model]: [SyntheticDevice]\r\n[ro.product.cpu.abi]: [{abi}]\r\n[ro.build.version.sdk]: [{sdk}]\r\n");
        Assert.Equal(vendor, properties["ro.product.manufacturer"]);
        Assert.Equal(abi, properties["ro.product.cpu.abi"]);
        AndroidDeviceCapabilities capabilities = AndroidCapabilityEvaluator.Evaluate(int.Parse(properties["ro.build.version.sdk"],
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(capabilities.Video.IsAvailable);
        Assert.Equal(sdk >= 30, capabilities.InternalAudio.IsAvailable);
    }
    public static TheoryData<int, int> DisplaySizes => new()
    {
        { 480, 800 }, { 720, 1280 }, { 1080, 1920 }, { 1080, 2400 },
        { 1440, 3200 }, { 1840, 2208 }, { 2560, 1600 }, { 3040, 1904 },
        { 2160, 3840 }, { 1920, 1920 },
    };

    [Theory]
    [MemberData(nameof(DisplaySizes))]
    public void PhoneTabletAndFoldableGeometriesKeepNativeAndReducedTouchSpacesCoherent(int width, int height)
    {
        AndroidDeviceDetails device = new("synthetic", "synthetic", "synthetic", "synthetic", "synthetic",
            "synthetic", 35, "arm64-v8a", AndroidTransport.Usb)
        { NativeDisplayWidth = width, NativeDisplayHeight = height };
        foreach (bool rotate in new[] { false, true })
        {
            int expectedWidth = rotate ? height : width;
            int expectedHeight = rotate ? width : height;
            PhoneOverlaySnapshot overlay = new(PhoneOverlayState.Running, "synthetic", "synthetic", expectedWidth, expectedHeight);
            AndroidVideoOptions native = PhoneVideoOptionsFactory.Create(AppSettings.Default with { VideoResolutionPercent = 100 }, device, overlay);
            Assert.Equal(0, native.MaximumSize);
            AndroidVideoOptions reduced = PhoneVideoOptionsFactory.Create(AppSettings.Default with { VideoResolutionPercent = 70 }, device, overlay);
            VideoResolutionProfile resolved = VideoResolutionProfiles.Resolve(expectedWidth, expectedHeight, 70);
            Assert.Equal(resolved.MaximumSize, reduced.MaximumSize);
            if (width != height)
            {
                PhoneInputCommand pointer = new(1, PhoneInputCommandKind.PointerDown, -2, 1, 1,
                    resolved.ExpectedWidth, resolved.ExpectedHeight, DateTimeOffset.UnixEpoch);
                PhoneInputCommand mapped = new AndroidDisplaySize(width, height).Map(pointer);
                Assert.Equal(expectedWidth, mapped.ScreenWidth);
                Assert.Equal(expectedHeight, mapped.ScreenHeight);
                Assert.Equal(1, mapped.NormalizedX);
                Assert.Equal(1, mapped.NormalizedY);
            }
        }
    }

    [Theory]
    [InlineData(19, false, false)]
    [InlineData(21, true, false)]
    [InlineData(23, true, false)]
    [InlineData(26, true, false)]
    [InlineData(28, true, false)]
    [InlineData(29, true, false)]
    [InlineData(30, true, true)]
    [InlineData(31, true, true)]
    [InlineData(33, true, true)]
    [InlineData(35, true, true)]
    [InlineData(36, true, true)]
    public void AndroidPlatformVersionsSeparateVideoControlFromInternalAudio(int sdk, bool video, bool audio)
    {
        AndroidDeviceCapabilities result = AndroidCapabilityEvaluator.Evaluate(sdk);
        Assert.Equal(video, result.Video.IsAvailable);
        Assert.Equal(video, result.Control.IsAvailable);
        Assert.Equal(audio, result.InternalAudio.IsAvailable);
    }

    [Fact]
    public async Task EveryCompiledControllerFamilyAndGenericProfileRoutesBothHandsWithoutLosingCoreActions()
    {
        foreach (string type in OpenVrBuiltInBindings.Bindings.Keys.Append("generic"))
        {
            foreach (OpenVrControllerHand hand in Enum.GetValues<OpenVrControllerHand>())
            {
                ControllerBindingGuide guide = await OpenVrBindingGuide.ReadDefaultAsync(type, hand, CancellationToken.None);
                string phoneHand = hand == OpenVrControllerHand.Left ? "/user/hand/left" : "/user/hand/right";
                Assert.Contains(guide.Entries, entry => entry.Action == "抓握" && entry.DevicePath == phoneHand);
                Assert.Contains(guide.Entries, entry => entry.Action == "触屏点击" && entry.DevicePath == phoneHand);
                Assert.All(guide.Entries.Where(entry => entry.Action is "返回" or "桌面" or "最近任务" or "隐藏后唤回"),
                    entry => Assert.Equal(phoneHand, entry.DevicePath));
                Assert.All(guide.Entries.Where(entry => entry.Action is "空间拖拽" or "重置空间"),
                    entry => Assert.NotEqual(phoneHand, entry.DevicePath));
            }
        }
        JsonObject generic = JsonNode.Parse(OpenVrGenericBinding.Create("synthetic_unknown_driver"))!.AsObject();
        Assert.Equal("synthetic_unknown_driver", generic["controller_type"]!.GetValue<string>());
        Assert.Equal(OpenVrInputManifest.ApplicationKey, generic["app_key"]!.GetValue<string>());
    }
}

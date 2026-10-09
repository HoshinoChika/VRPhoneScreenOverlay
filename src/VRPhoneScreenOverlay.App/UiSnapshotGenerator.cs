using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal static class UiSnapshotGenerator
{
    private static readonly JsonSerializerOptions _manifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    internal static readonly string[] ExpectedFileNames =
    [
        "motion-default.png",
        "motion-advanced.png",
        "motion-reset-all.png",
        "home-empty.png",
        "wireless-empty.png",
        "wireless-refreshed.png",
        "wireless-busy.png",
        "wireless-error.png",
        "wireless-ready.png",
        "wireless-code.png",
        "wireless-code-busy.png",
        "wireless-code-error.png",
        "wireless-manual.png",
        "wireless-manual-busy.png",
        "wireless-manual-error.png",
        "wireless-manual-connect.png",
        "connection-usb.png",
        "connection-method-help.png",
        "connection-current-help.png",
        "connection-history-help.png",
        "connection-device-current.png",
        "connection-device-connected.png",
        "connection-device-connected-edited.png",
        "connection-device-history.png",
        "connection-device-name-invalid.png",
        "connection-device-name-long.png",
        "home-waiting-steamvr.png",
        "home-connected.png",
        "home-wireless-busy.png",
        "home-opening.png",
        "home-running.png",
        "home-closing.png",
        "home-error.png",
        "home-reset-unavailable.png",
        "settings.png",
        "settings-off.png",
        "settings-on.png",
        "settings-pico-waiting.png",
        "settings-pico-starting.png",
        "settings-pico-retry.png",
        "video.png",
        "video-pending.png",
        "bindings-empty.png",
        "bindings-left.png",
        "bindings-recovery.png",
        "bindings-loading.png",
        "bindings-defaults-restoring.png",
        "bindings-defaults-restored.png",
        "bindings-defaults-failed.png",
        "bindings-current.png",
        "bindings-current-wmr.png",
        "bindings-current-touch.png",
        "bindings-knuckles.png",
        "bindings-oculus_touch.png",
        "bindings-oculus_rift.png",
        "bindings-vive_controller.png",
        "bindings-holographic_controller.png",
        "bindings-wmr_lenovo.png",
        "bindings-wmr_dell.png",
        "bindings-hpmotioncontroller.png",
        "bindings-pico_controller.png",
        "bindings-generic.png",
        "bindings-unrecognized.png",
        "bindings-pico_controller_ice.png",
        "about.png",
        "diagnostic-report.png",
        "diagnostics-upload-failed.png",
        "close-confirmation.png",
    ];

    public static int Run(string? outputDirectory, string? only = null)
    {
        try
        {
            string output = ResolveOutputDirectory(outputDirectory);
            HashSet<string>? selected = only is null ? null : new(only.Split(','), StringComparer.Ordinal);
            if (selected is not null && selected.Any(name => !ExpectedFileNames.Contains(name, StringComparer.Ordinal)))
            { throw new ArgumentException("Unknown UI snapshot name.", nameof(only)); }
            RenderSet(output, 1f, selected);
            RenderSet(Path.Combine(output, "720p-100"),
                UiLayoutMetrics.DisplayScale(new Size(1280, 720), new Size(1280, 680), 96), selected);
            RenderSet(Path.Combine(output, "768p-150"),
                UiLayoutMetrics.DisplayScale(new Size(1366, 768), new Size(1366, 728), 144), selected);
            RenderSet(Path.Combine(output, "1080p-150"),
                UiLayoutMetrics.DisplayScale(new Size(1920, 1080), new Size(1920, 1040), 144), selected);
            RenderSet(Path.Combine(output, "4k-200"),
                UiLayoutMetrics.DisplayScale(new Size(3840, 2160), new Size(3840, 2080), 192), selected);
            RenderSet(Path.Combine(output, "1440p-150"),
                UiLayoutMetrics.DisplayScale(new Size(2560, 1440), new Size(2560, 1400), 144), selected);
            RenderSet(Path.Combine(output, "1024x768-100"),
                UiLayoutMetrics.DisplayScale(new Size(1024, 768), new Size(1024, 728), 96), selected);
            RenderSet(Path.Combine(output, "900p-125"),
                UiLayoutMetrics.DisplayScale(new Size(1600, 900), new Size(1600, 860), 120), selected);
            RenderSet(Path.Combine(output, "1080p-175"),
                UiLayoutMetrics.DisplayScale(new Size(1920, 1080), new Size(1920, 1040), 168), selected);
            RenderSet(Path.Combine(output, "ultrawide-125"),
                UiLayoutMetrics.DisplayScale(new Size(3440, 1440), new Size(3440, 1400), 120), selected);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                ExternalException or InvalidOperationException or JsonException)
        {
            return 20;
        }
    }

    private static void RenderSet(string output, float scale, HashSet<string>? selected)
    {
        void Render(string directory, string name, Func<Control> createPage, SnapshotPage page, float renderScale,
            Action<Control>? afterHandleCreated = null, Func<Control, Control?>? selectOverlay = null)
        {
            if (selected is null || selected.Contains(name))
            { UiSnapshotGenerator.Render(directory, name, createPage, page, renderScale, afterHandleCreated, selectOverlay); }
        }
        Directory.CreateDirectory(output);
        Render(output, "home-empty.png", CreateHomeEmpty, SnapshotPage.Home, scale);
        Render(output, "connection-usb.png", () =>
        {
            HomePageView page = CreateHomeEmpty();
            page.ShowWireless();
            page.WirelessPanel.SetDevices(SampleConnectionDevices());
            return page;
        }, SnapshotPage.Home, scale);
        Render(output, "connection-method-help.png", () =>
        {
            HomePageView page = CreateHomeEmpty();
            page.ShowWireless();
            return page;
        }, SnapshotPage.Home, scale, selectOverlay: page =>
        {
            ContextHelpIcon icon = (ContextHelpIcon)page.Controls.Find("titleHelp", true).Single();
            icon.ShowHelp();
            return ((MainShellView)page.Parent!.Parent!).ContextHelp;
        });
        foreach (bool history in new[] { false, true })
        {
            Render(output, history ? "connection-history-help.png" : "connection-current-help.png", () =>
            {
                HomePageView page = CreateHomeEmpty();
                page.ShowWireless();
                page.WirelessPanel.SetDevices(SampleConnectionDevices());
                return page;
            }, SnapshotPage.Home, scale, selectOverlay: page =>
            {
                ContextHelpIcon icon = (ContextHelpIcon)page.Controls.Find(history ? "historyDevicesTitleHelp" : "currentDevicesTitleHelp", true).Single();
                icon.ShowHelp();
                return ((MainShellView)page.Parent!.Parent!).ContextHelp;
            });
        }
        foreach (bool available in new[] { true, false })
        {
            Render(output, available ? "connection-device-current.png" : "connection-device-history.png", () =>
            {
                HomePageView page = CreateHomeEmpty();
                page.ShowWireless();
                page.WirelessPanel.ShowDeviceManager(new("sample", "示例手机 Pro", "DEMO-PRO", AndroidTransport.Usb,
                    available, available ? AndroidDeviceStatus.Ready : AndroidDeviceStatus.Offline, false, true,
                    DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)), history: !available);
                return page;
            }, SnapshotPage.Home, scale, selectOverlay: page => ((HomePageView)page).WirelessPanel.DeviceManagerHost);
        }
        foreach (bool edited in new[] { false, true })
        {
            Render(output, edited ? "connection-device-connected-edited.png" : "connection-device-connected.png", () =>
            {
                HomePageView page = CreateHomeEmpty();
                page.ShowWireless();
                page.WirelessPanel.ShowDeviceManager(new("connected", "当前平板", "DEMO", AndroidTransport.Usb, true,
                    AndroidDeviceStatus.Ready, true, true, DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture), "当前平板"));
                if (edited)
                {
                    page.WirelessPanel.DeviceManager.RenameTextBox.Text = "修改后的平板";
                    page.WirelessPanel.DeviceManager.AutoConnectToggle.Checked = false;
                }
                return page;
            }, SnapshotPage.Home, scale, selectOverlay: page => ((HomePageView)page).WirelessPanel.DeviceManagerHost);
        }
        foreach (bool invalid in new[] { true, false })
        {
            Render(output, invalid ? "connection-device-name-invalid.png" : "connection-device-name-long.png", () =>
            {
                HomePageView page = CreateHomeEmpty();
                page.ShowWireless();
                page.WirelessPanel.ShowDeviceManager(new("sample", "备用手机", "DEMO-PRO", AndroidTransport.Usb,
                    false, AndroidDeviceStatus.Offline, false, true, DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture), "备用手机"), history: true);
                page.WirelessPanel.DeviceManager.RenameTextBox.Text = invalid ? "备用手机😀" : new string('中', 20);
                return page;
            }, SnapshotPage.Home, scale, selectOverlay: page => ((HomePageView)page).WirelessPanel.DeviceManagerHost);
        }
        foreach (string state in new[] { "default", "advanced", "reset-all" })
        {
            Render(output, $"motion-{state}.png", () =>
            {
                HomePageView page = CreateHomeEmpty();
                page.ShowMotion();
                page.InertiaToggle.Checked = state != "default";
                page.InertiaToggle.Text = state == "default" ? "关闭" : "开启";
                page.MotionPanel.ResetAllOffsets = state == "reset-all";
                page.MotionPanel.Strength.Value = state == "default" ? 1 : 3;
                return page;
            }, SnapshotPage.Home, scale);
        }
        foreach (string state in new[] { "empty", "refreshed", "busy", "error", "ready" })
        {
            Render(output, $"wireless-{state}.png", () =>
            {
                HomePageView page = CreateHomeEmpty();
                page.ShowWireless();
                WirelessConnectionView wireless = page.WirelessPanel;
                wireless.SelectMethod(WirelessPairingMethod.Scan);
                wireless.ShowQr(state is "empty" or "refreshed" ? "WIFI:T:ADB;S:studio-snapshot;P:snapshot-not-a-real-credential;;" : null);
                wireless.SetBusy(state is "busy" or "empty" or "refreshed");
                if (state is "empty" or "refreshed") { wireless.SetQrWaiting(); }
                wireless.Status.Text = state switch
                {
                    "refreshed" => "等待扫码",
                    "busy" => "正在配对…",
                    "error" => "配对失败：请重新打开配对码窗口，核对配对端口与六位码",
                    "ready" => "已连接",
                    _ => "等待扫码",
                };
                wireless.Status.ForeColor = state == "error" ? UiPalette.Warning
                    : state == "ready" ? UiPalette.Success : UiPalette.Info;
                if (state == "ready")
                {
                    wireless.SetDevices(SampleConnectionDevices());
                }
                return page;
            }, SnapshotPage.Home, scale);
        }
        foreach (WirelessPairingMethod method in new[] { WirelessPairingMethod.Code, WirelessPairingMethod.Manual })
        {
            foreach (string state in new[] { "", "-busy", "-error" })
            {
                string name = method == WirelessPairingMethod.Code ? "code" : "manual";
                Render(output, $"wireless-{name}{state}.png", () =>
                {
                    HomePageView page = CreateHomeEmpty();
                    page.ShowWireless();
                    WirelessConnectionView wireless = page.WirelessPanel;
                    wireless.SelectMethod(method);
                    wireless.SetBusy(state == "-busy");
                    wireless.ShowManualFallback(state == "-error");
                    wireless.Status.Text = state switch
                    {
                        "-busy" => "正在配对…",
                        "-error" => "自动连接未完成，请转到手动配对，填写手机当前显示的地址。",
                        _ => method == WirelessPairingMethod.Code ? ""
                            : "",
                    };
                    wireless.Status.ForeColor = state == "-error" ? UiPalette.Warning : UiPalette.Info;
                    wireless.SetDevices(SampleConnectionDevices());
                    return page;
                }, SnapshotPage.Home, scale);
            }
        }
        Render(output, "wireless-manual-connect.png", () =>
        {
            HomePageView page = CreateHomeEmpty();
            page.ShowWireless();
            page.WirelessPanel.SelectMethod(WirelessPairingMethod.Manual);
            page.WirelessPanel.Status.Text = "";
            page.WirelessPanel.SetDevices(SampleConnectionDevices());
            return page;
        }, SnapshotPage.Home, scale);
        Render(output, "close-confirmation.png", CreateHomeRunning, SnapshotPage.Home, scale);
        Render(output, "home-waiting-steamvr.png", () =>
        {
            HomePageView page = CreateHomeConnected();
            page.SteamVrStatus.Text = "连接超时";
            page.SteamVrStatus.ForeColor = UiPalette.Danger;
            PhoneOverlayButtonPolicy.Apply(page.OpenOverlayButton, false, false, false);
            page.PlayspaceStatus.Text = "已关闭";
            return page;
        }, SnapshotPage.Home, scale);
        Render(output, "home-connected.png", CreateHomeConnected, SnapshotPage.Home, scale);
        Render(output, "home-wireless-busy.png", () =>
        {
            HomePageView page = CreateHomeConnected();
            PhoneOverlayButtonPolicy.Apply(page.OpenOverlayButton, false, true, false, wirelessBusy: true);
            return page;
        }, SnapshotPage.Home, scale);
        Render(output, "home-opening.png", CreateHomeOpening, SnapshotPage.Home, scale);
        Render(output, "home-running.png", CreateHomeRunning, SnapshotPage.Home, scale);
        Render(output, "home-closing.png", CreateHomeClosing, SnapshotPage.Home, scale);
        Render(output, "home-error.png", CreateHomeError, SnapshotPage.Home, scale);
        Render(output, "home-reset-unavailable.png", () =>
        {
            HomePageView page = CreateHomeConnected();
            page.PlayspaceStatus.Text = "复位失败";
            page.PlayspaceStatus.ForeColor = UiPalette.Warning;
            page.PlayspaceCoordinates.Text = "地面坐标不可用，请重试";
            return page;
        }, SnapshotPage.Home, scale);
        Render(output, "settings.png", CreateSettings, SnapshotPage.Settings, scale);
        foreach (bool enabled in new[] { false, true })
        {
            Render(output, enabled ? "settings-on.png" : "settings-off.png", () =>
            {
                SettingsPageView page = CreateSettings();
                page.KeepAwakeWhileGrabbedToggle.Checked = enabled;
                page.VrUnlockKeypadToggle.Checked = enabled;
                page.AutoOpenToggle.Checked = enabled;
                page.ScreenOffToggle.Checked = enabled;
                page.SteamVrToggle.Checked = enabled;
                page.MinimizeToggle.Checked = enabled;
                page.PicoToggle.Checked = enabled;
                page.PicoStatus.Text = enabled ? "运行中" : "已关闭";
                return page;
            }, SnapshotPage.Settings, scale);
        }
        foreach ((string name, string text) in new[]
        {
            ("waiting", "未连接"),
            ("starting", "连接中"),
            ("retry", "重连中"),
        })
        {
            Render(output, $"settings-pico-{name}.png", () =>
            {
                SettingsPageView page = CreateSettings();
                page.PicoToggle.Checked = true;
                page.PicoStatus.Text = text;
                return page;
            }, SnapshotPage.Settings, scale);
        }
        Render(output, "video.png", () => CreateVideo(false), SnapshotPage.Video, scale);
        Render(output, "video-pending.png", () => CreateVideo(true), SnapshotPage.Video, scale);
        RenderBindingGuides(output, scale, selected);
        Render(output, "about.png", CreateAbout, SnapshotPage.About, scale);
        Render(output, "diagnostics-upload-failed.png", () =>
        {
            AboutPageView page = CreateAbout();
            page.DiagnosticsStatus.Text = "上传失败，诊断包已移至软件根目录；请联系作者并提供该文件。";
            page.DiagnosticsStatus.ForeColor = UiPalette.Danger;
            return page;
        }, SnapshotPage.About, scale);
        Render(
            output,
            "diagnostic-report.png",
            CreateDiagnosticReport,
            SnapshotPage.About,
            scale,
            page => ShowDiagnosticReport((AboutPageView)page),
            page => ((AboutPageView)page).DiagnosticReportPanel);
        WriteManifest(output, scale, selected);
    }

    private static void RenderBindingGuides(string output, float scale, HashSet<string>? selected)
    {
        foreach (ControllerBindingChoice choice in OpenVrBindingGuide.Choices)
        {
            ControllerBindingGuide guide = OpenVrBindingGuide.ReadDefaultAsync(choice.ControllerType,
                OpenVrControllerHand.Right, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            RenderGuide($"bindings-{choice.Key}.png", guide, choice.Key);
        }
        // An unverified legacy profile is displayed as the current binding, not
        // advertised as a second PICO retail product or included in the catalog.
        ControllerBindingGuide legacy = OpenVrBindingGuide.ReadDefaultAsync("pico_controller_ice", OpenVrControllerHand.Right,
            CancellationToken.None).AsTask().GetAwaiter().GetResult();
        RenderGuide("bindings-pico_controller_ice.png", legacy, "pico_controller_ice");
        RenderGuide("bindings-current-wmr.png", OpenVrBindingGuide.ReadDefaultAsync("holographic_controller", OpenVrControllerHand.Right,
            CancellationToken.None).AsTask().GetAwaiter().GetResult(), "pico_controller");
        RenderGuide("bindings-current-touch.png", OpenVrBindingGuide.ReadDefaultAsync("oculus_touch", OpenVrControllerHand.Right,
            CancellationToken.None).AsTask().GetAwaiter().GetResult(), "pico_controller");
        RenderGuide("bindings-current.png", new("knuckles", "已保存绑定 · 演示数据", "",
        [
            new("抓握", "/user/hand/right", "/input/grip", "button", "click"),
            new("抓握", "/user/hand/right", "/input/a", "button", "double"),
            new("触屏点击", "/user/hand/right", "/input/trigger", "trigger", "click"),
            new("返回", "/user/hand/right", "/input/a", "button", "click"),
            new("桌面", "/user/hand/right", "/input/a", "button", "long"),
            new("截屏", "/user/hand/right", "/input/b", "button", "double"),
            new("隐藏后唤回", "/user/hand/right", "/input/b", "button", "double"),
            new("空间拖拽", "/user/hand/left", "/input/b", "button", "click"),
        ]), "knuckles");
        RenderGuide("bindings-empty.png", new("pico_controller", "绑定读取失败", "暂时无法读取说明，请刷新重试", []), "pico_controller");
        ControllerBindingGuide generic = OpenVrBindingGuide.ReadDefaultAsync(OpenVrBindingGuide.GenericKey,
            OpenVrControllerHand.Right, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        RenderGuide("bindings-unrecognized.png", generic with { Unrecognized = true }, OpenVrBindingGuide.GenericKey);
        ControllerBindingGuide leftGuide = OpenVrBindingGuide.ReadDefaultAsync("pico_controller",
            OpenVrControllerHand.Left, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        RenderGuide("bindings-left.png", leftGuide, "pico_controller");
        ControllerBindingGuide pico = OpenVrBindingGuide.ReadDefaultAsync("pico_controller", OpenVrControllerHand.Right,
            CancellationToken.None).AsTask().GetAwaiter().GetResult();
        RenderGuide("bindings-recovery.png", pico, "pico_controller");
        RenderGuide("bindings-loading.png", pico, "pico_controller");
        RenderGuide("bindings-defaults-restoring.png", pico, "pico_controller");
        RenderGuide("bindings-defaults-restored.png", pico, "pico_controller");
        RenderGuide("bindings-defaults-failed.png", pico, "pico_controller");

        void RenderGuide(string name, ControllerBindingGuide guide, string controller)
        {
            if (selected is not null && !selected.Contains(name)) { return; }
            Render(output, name, () =>
            {
                ControllerBindingsView page = new();
                page.ControllerSelector.SetNodes(OpenVrBindingGuide.Choices.Select(
                    item => new FixedChoiceNode<string>(item.Key, item.Label)), controller);
                page.HandSelector.SetNodes(
                [new(ControllerHandPreference.Left, "左手"), new(ControllerHandPreference.Right, "右手")],
                    name == "bindings-left.png" ? ControllerHandPreference.Left : ControllerHandPreference.Right);
                page.SetGuide(guide);
                if (name.StartsWith("bindings-defaults-", StringComparison.Ordinal))
                {
                    bool restoring = name == "bindings-defaults-restoring.png";
                    page.OperationStatus.Text = restoring ? "正在恢复所有手柄的默认绑定并应用…" : name == "bindings-defaults-restored.png"
                        ? "所有手柄已恢复默认绑定并应用" : "应用失败，原配置已恢复；请启动 SteamVR 后重试";
                    page.OperationStatus.ForeColor = restoring ? UiPalette.Info : name == "bindings-defaults-restored.png" ? UiPalette.Success : UiPalette.Danger;
                    page.RestoreBindingsButton.Enabled = !restoring;
                    page.OpenBindingsButton.Enabled = !restoring;
                    page.HandSelector.Enabled = !restoring;
                    page.ControllerSelector.Enabled = !restoring;
                    page.RefreshButton.Enabled = !restoring;
                }
                if (name is "bindings-recovery.png" or "bindings-loading.png")
                {
                    if (name == "bindings-recovery.png")
                    { page.BindingNotice.UpdateConnectionNotice("SteamVR连接超时，请检查SteamVR状态和运行权限。"); }
                    else { page.BindingNotice.UpdateNotice(OpenVrBindingHealthState.Loading, true, "正在读取 SteamVR 手柄绑定…"); }
                }
                return page;
            }, SnapshotPage.Features, scale);
        }
    }

    private static ManagedAndroidDevice[] SampleConnectionDevices()
    {
        DateTimeOffset date = DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        return
        [
            new("1", "主用手机A1", "Demo", AndroidTransport.Usb, true, AndroidDeviceStatus.Ready, true, true, date, "主用手机A1"),
            new("2", "备用手机2", "Demo", AndroidTransport.Network, true, AndroidDeviceStatus.Ready, false, false, date.AddHours(-1), "备用手机2"),
            new("3", "示例手机 Pro", "Demo", AndroidTransport.Usb, true, AndroidDeviceStatus.Unauthorized, false, false, null),
            new("4", "示例手机 Lite", "Demo", AndroidTransport.Network, true, AndroidDeviceStatus.Ready, false, false, null),
            new("5", "VR专用手机", "Demo", AndroidTransport.Usb, false, AndroidDeviceStatus.Offline, false, true, date.AddHours(-2), "VR专用手机"),
            new("6", "历史手机4", "Demo", AndroidTransport.Network, false, AndroidDeviceStatus.Offline, false, false, date.AddHours(-3), "历史手机4"),
        ];
    }

    private static string ResolveOutputDirectory(string? outputDirectory)
    {
        string path = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-snapshots")
            : outputDirectory;
        return Path.GetFullPath(path);
    }

    private static void Render(
        string outputDirectory,
        string fileName,
        Func<Control> createPage,
        SnapshotPage selectedPage,
        float scale,
        Action<Control>? afterHandleCreated = null,
        Func<Control, Control?>? selectOverlay = null)
    {
        using Font defaultFont = new("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        using MainShellView shell = new()
        {
            Location = Point.Empty,
            Size = UiLayoutMetrics.MainWindowClientSize,
            Font = defaultFont,
        };
        using Control page = createPage();
        page.Location = UiLayoutMetrics.PageLocation;
        shell.PageHost.Controls.Add(page);
        SelectNavigation(shell, selectedPage);
        using UiScaleLayout layout = new(shell);
        CreateVisibleControlTree(shell);
        afterHandleCreated?.Invoke(page);
        CreateVisibleControlTree(shell);
        layout.Apply(scale);
        shell.PerformLayout();
        page.PerformLayout();
        Application.DoEvents();

        Control? overlay = selectOverlay?.Invoke(page);
        if (fileName == "close-confirmation.png")
        {
            overlay = shell.CloseConfirmation;
            overlay.Visible = true;
            overlay.BringToFront();
            CreateVisibleControlTree(overlay);
        }
        if (overlay is not null)
        {
            overlay.Visible = false;
            Application.DoEvents();
        }

        using Bitmap bitmap = new(
            shell.ClientSize.Width,
            shell.ClientSize.Height,
            PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(96, 96);
        shell.DrawToBitmap(bitmap, new Rectangle(Point.Empty, shell.ClientSize));
        if (overlay is not null)
        {
            overlay.Visible = true;
            overlay.BringToFront();
            CreateVisibleControlTree(overlay);
            overlay.PerformLayout();
            Application.DoEvents();
            using Bitmap overlayBitmap = new(
                overlay.ClientSize.Width,
                overlay.ClientSize.Height,
                PixelFormat.Format32bppPArgb);
            overlayBitmap.SetResolution(96, 96);
            overlay.DrawToBitmap(
                overlayBitmap,
                new Rectangle(Point.Empty, overlay.ClientSize));
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.DrawImageUnscaled(overlayBitmap, LocationRelativeTo(overlay, shell));
        }

        string destination = Path.Combine(outputDirectory, fileName);
        string temporary = destination + ".new";
        bitmap.Save(temporary, ImageFormat.Png);
        File.Move(temporary, destination, true);
        WriteLayoutAudit(shell, outputDirectory, fileName);
    }

    private static void WriteLayoutAudit(Control shell, string directory, string fileName)
    {
        List<object> controls = [];
        void Inspect(Control parent, string path)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) { continue; }
                string name = path + "/" + (string.IsNullOrEmpty(child.Name) ? child.GetType().Name : child.Name);
                bool scrollContent = parent is ScrollableControl { AutoScroll: true };
                bool contained = scrollContent || parent.ClientRectangle.Contains(child.Bounds);
                Size? textSize = child is Label label && label.Text.Length > 0
                    ? TextRenderer.MeasureText(label.Text, label.Font,
                        new Size(Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal), int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix) : null;
                bool captionFits = true;
                if (child is ModernButton button && button.Text.Length > 0)
                {
                    int horizontalInset = button.VerticalContent ? UiLayoutMetrics.Pixels(button, 10) :
                        button.Icon != UiIcon.None || button.HasApplicationIcon ? UiLayoutMetrics.Pixels(button, 25) + 4 : 7;
                    int height = button.VerticalContent ? button.Height / 2 - UiLayoutMetrics.Pixels(button, 5) : button.Height;
                    Size caption = TextRenderer.MeasureText(button.Text, button.Font,
                        new Size(Math.Max(1, button.Width - horizontalInset), int.MaxValue),
                        button.VerticalContent ? TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix : TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    captionFits = caption.Height <= height && (button.VerticalContent || caption.Width <= button.Width - horizontalInset);
                }
                controls.Add(new
                {
                    name,
                    parent = path,
                    siblingIndex = parent.Controls.GetChildIndex(child),
                    type = child.GetType().Name,
                    child.Bounds,
                    contained,
                    scrollContent,
                    horizontalContained = !scrollContent || (child.Left >= parent.AutoScrollOffset.X && child.Right <= parent.ClientSize.Width),
                    intentionalOverlay = child is ContextHelpPopup or CloseConfirmationView || child.Name is "deviceManagerHost" or "qrFullscreenPanel"
                        or "diagnosticReportPanel" or "currentEmpty" or "historyEmpty" or "titleDivider",
                    text = child is Label ? child.Text : null,
                    textHeight = textSize?.Height,
                    textFits = textSize is null || textSize.Value.Height <= child.ClientSize.Height - child.Padding.Vertical,
                    captionFits,
                });
                Inspect(child, name);
            }
        }
        Inspect(shell, "shell");
        File.WriteAllText(Path.Combine(directory, fileName + ".layout.json"), JsonSerializer.Serialize(controls, _manifestJsonOptions));
    }

    private static Point LocationRelativeTo(Control control, Control ancestor)
    {
        Point location = Point.Empty;
        Control? current = control;
        while (current is not null && !ReferenceEquals(current, ancestor))
        {
            location.Offset(current.Left, current.Top);
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException("UI snapshot overlay is outside the rendered shell.");
        }

        return location;
    }

    private static void CreateVisibleControlTree(Control control)
    {
        control.CreateControl();
        foreach (Control child in control.Controls)
        {
            if (child.Visible)
            {
                CreateVisibleControlTree(child);
            }
        }
    }

    private static void SelectNavigation(MainShellView shell, SnapshotPage selectedPage)
    {
        shell.HomeNavigation.Selected = selectedPage == SnapshotPage.Home;
        shell.SettingsNavigation.Selected = selectedPage == SnapshotPage.Settings;
        shell.FeaturesNavigation.Selected = selectedPage == SnapshotPage.Features;
        shell.VideoNavigation.Selected = selectedPage == SnapshotPage.Video;
        shell.AboutNavigation.Selected = selectedPage == SnapshotPage.About;
    }

    private static HomePageView CreateHomeEmpty()
    {
        HomePageView page = CreateHomeBase();
        page.PhoneStatus.Text = "等待设备";
        page.PhoneStatus.ForeColor = UiPalette.TextSecondary;
        page.PhoneDetails.Text = "未连接";
        page.AdbDetail.Text = "未连接手机，请在连接管理里连接手机。";
        page.VideoStatus.Text = "未运行";
        page.AudioStatus.Text = "未运行";
        page.ControlStatus.Text = "未运行";
        page.SteamVrStatus.Text = "已连接";
        page.SteamVrStatus.ForeColor = UiPalette.Success;
        PhoneOverlayButtonPolicy.Apply(page.OpenOverlayButton, false, false, false);
        return page;
    }

    private static HomePageView CreateHomeConnected()
    {
        HomePageView page = CreateHomeBase();
        page.PhoneStatus.Text = "已连接";
        page.PhoneStatus.ForeColor = UiPalette.Success;
        page.PhoneName.Text = "Google Pixel";
        page.PhoneDetails.Text = "ID DEMO-PIXEL\nAndroid 16 · 1080 × 2400";
        page.AdbDetail.Text = "已连接 Google Pixel";
        page.VideoStatus.Text = "待启动";
        page.AudioStatus.Text = "待启动";
        page.ControlStatus.Text = "待启动";
        page.SteamVrStatus.Text = "已连接";
        page.SteamVrStatus.ForeColor = UiPalette.Success;
        PhoneOverlayButtonPolicy.Apply(page.OpenOverlayButton, false, true, false);
        return page;
    }

    private static HomePageView CreateHomeRunning()
    {
        HomePageView page = CreateHomeBase();
        page.PhoneStatus.Text = "已连接";
        page.PhoneStatus.ForeColor = UiPalette.Success;
        page.PhoneName.Text = "Google Pixel";
        page.PhoneDetails.Text = "ID DEMO-PIXEL\nAndroid 16 · 1080 × 2400";
        page.AdbDetail.Text = "已连接 Google Pixel";
        page.VideoStatus.Text = "运行中";
        page.VideoStatus.ForeColor = UiPalette.Success;
        page.AudioStatus.Text = "播放中";
        page.AudioStatus.ForeColor = UiPalette.Success;
        page.ControlStatus.Text = "已就绪";
        page.ControlStatus.ForeColor = UiPalette.Success;
        page.SteamVrStatus.Text = "已连接";
        page.SteamVrStatus.ForeColor = UiPalette.Success;
        page.MetricResolution.Text = "1080×2400";
        page.MetricBitrate.Text = "15.8 Mbps";
        page.MetricFrameRate.Text = "59.7 FPS";
        page.MetricLatency.Text = "42 ms";
        page.PlayspaceStatus.Text = "已连接";
        page.PlayspaceStatus.ForeColor = UiPalette.Success;
        page.PlayspaceCoordinates.Text = "X 0.12  Y 0.04  Z -0.28 m";
        page.OpenOverlayButton.Text = "关闭手机浮窗";
        page.OpenOverlayButton.Icon = UiIcons.Pause;
        page.OpenOverlayButton.Tone = UiButtonTone.Danger;
        page.OpenOverlayButton.Enabled = true;
        return page;
    }

    private static HomePageView CreateHomeOpening()
    {
        HomePageView page = CreateHomeConnected();
        page.VideoStatus.Text = "连接中";
        page.VideoStatus.ForeColor = UiPalette.Info;
        page.OpenOverlayButton.Text = "正在打开";
        page.OpenOverlayButton.Icon = UiIcons.LoadingDots;
        page.OpenOverlayButton.Tone = UiButtonTone.Neutral;
        page.OpenOverlayButton.Enabled = false;
        return page;
    }

    private static HomePageView CreateHomeClosing()
    {
        HomePageView page = CreateHomeRunning();
        page.VideoStatus.Text = "关闭中";
        page.VideoStatus.ForeColor = UiPalette.Info;
        page.OpenOverlayButton.Text = "正在关闭";
        page.OpenOverlayButton.Icon = UiIcons.LoadingDots;
        page.OpenOverlayButton.Tone = UiButtonTone.Neutral;
        page.OpenOverlayButton.Enabled = false;
        return page;
    }

    private static HomePageView CreateHomeError()
    {
        HomePageView page = CreateHomeBase();
        page.PhoneStatus.Text = "需要授权";
        page.PhoneStatus.ForeColor = UiPalette.Warning;
        page.PhoneDetails.Text = "请解锁手机并允许这台电脑进行 USB 调试";
        page.AdbDetail.Text = "已发现设备，但 ADB 返回 unauthorized";
        page.VideoStatus.Text = "不可用";
        page.VideoStatus.ForeColor = UiPalette.Danger;
        page.AudioStatus.Text = "等待手机";
        page.AudioStatus.ForeColor = UiPalette.Warning;
        page.ControlStatus.Text = "不可用";
        page.ControlStatus.ForeColor = UiPalette.Danger;
        page.SteamVrStatus.Text = "已连接";
        page.SteamVrStatus.ForeColor = UiPalette.Success;
        PhoneOverlayButtonPolicy.Apply(page.OpenOverlayButton, false, false, false);
        return page;
    }

    private static HomePageView CreateHomeBase()
    {
        HomePageView page = new();

        page.MetricResolution.Text = "--";
        page.MetricBitrate.Text = "--";
        page.MetricFrameRate.Text = "--";
        page.MetricLatency.Text = "--";
        page.PlayspaceStatus.Text = "已关闭";
        page.PlayspaceStatus.ForeColor = UiPalette.TextSecondary;
        page.PlayspaceCoordinates.Text = "X 0.00  Y 0.00  Z 0.00 m";
        page.PlayspaceToggle.Checked = false;
        return page;
    }

    private static SettingsPageView CreateSettings()
    {
        SettingsPageView page = new();
        page.KeepAwakeWhileGrabbedToggle.Checked = true;
        return page;
    }

    private static VideoPageView CreateVideo(bool pending)
    {
        VideoPageView page = new();
        IReadOnlyList<VideoResolutionProfile> resolutionProfiles =
            VideoResolutionProfiles.Create(1440, 3200);
        page.ResolutionSelector.SetNodes(
            resolutionProfiles.Select(profile =>
                new FixedChoiceNode<VideoResolutionProfile>(profile, profile.DisplayName)),
            resolutionProfiles.First(profile => profile.Percent == 80));
        page.BitrateSelector.SetNodes(
            AppSettingsPolicy.VideoBitratesMbps.Order().Select(
                value => new FixedChoiceNode<int>(value, $"{value} Mbps")),
            16);
        page.FrameRateSelector.SetNodes(
            AppSettingsPolicy.VideoMaximumFrameRates.Order().Select(
                value => new FixedChoiceNode<int>(value, $"{value} FPS")),
            60);
        page.QualityStatus.Text = pending ? "待应用 · 将重启浮窗" : "已保存";
        page.DiscardButton.Enabled = pending;
        page.ApplyButton.Enabled = pending;
        return page;
    }

    private static AboutPageView CreateAbout()
    {
        AboutPageView page = new();
        ConfigureAbout(page);
        return page;
    }

    private static AboutPageView CreateDiagnosticReport()
    {
        AboutPageView page = new();
        ConfigureAbout(page);
        page.IssueTypeComboBox.SetItems(
        [
            "视频异常",
            "音频异常",
            "控制异常",
            "手机连接异常",
            "SteamVR / 手柄绑定异常",
            "空间拖拽异常",
            "软件更新异常",
            "其他问题",
        ]);
        return page;
    }

    private static void ShowDiagnosticReport(AboutPageView page)
    {
        page.ShowDiagnosticReport();
        page.OccurredAtPicker.MinDate = new DateTime(2000, 1, 1);
        page.OccurredAtPicker.MaxDate = new DateTime(2099, 12, 31);
        page.OccurredAtPicker.Value = new DateTime(2026, 8, 20, 21, 30, 0);
        page.IssueTypeComboBox.SelectedIndex = 0;
        page.IssueDescriptionTextBox.Text = "手机旋转后画面比例异常，重新打开浮窗后恢复。";
        page.ConfirmDiagnosticUploadButton.Enabled = true;
    }

    private static void ConfigureAbout(AboutPageView page)
    {
        page.UpdateChannelSelector.SetNodes(
        [
            new FixedChoiceNode<UpdateChannel>(UpdateChannel.Beta, "测试"),
        ], UpdateChannel.Beta);
        page.UpdateChannelSelector.Enabled = false;
        string version = Application.ProductVersion.Split('+', 2)[0];
        page.SetProductDetails(
            $"版本 {version}");
        page.OperationStatus.Text = "";
    }

    private static void WriteManifest(string outputDirectory, float scale, HashSet<string>? selected)
    {
        string path = Path.Combine(outputDirectory, "manifest.json");
        string temporary = path + ".new";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                width = UiLayoutMetrics.ClientSizeFor(scale).Width,
                height = UiLayoutMetrics.ClientSizeFor(scale).Height,
                scale,
                files = selected is null ? ExpectedFileNames : ExpectedFileNames.Where(selected.Contains).ToArray(),
            }, _manifestJsonOptions));
        File.Move(temporary, path, true);
    }

    private enum SnapshotPage
    {
        Home,
        Settings,
        Features,
        Video,
        About,
    }
}

# 构建与验证

需要 Windows x64 与 global.json 指定的 .NET SDK。仓库内置必要的 ADB、scrcpy server 和 OpenVR 运行资源，构建不会下载或执行未知发布包。

```powershell
.\tools\Install-DevelopmentSdk.ps1
dotnet restore VRPhoneScreenOverlay.slnx --locked-mode
dotnet build VRPhoneScreenOverlay.slnx -c Release
.\tools\Build-Local.ps1 -FocusedValidation
```

使用本次修改相关的 `dotnet test --filter` 做验证。`Build-Local -FocusedValidation` 保留编译、发布烟测和安装目录轮换，但不运行全量测试/全界面快照。最终目录为 `release/VRPhoneScreenOverlay`，根部只有主 EXE 和 app/。

如修改圆角着色器，运行 `tools/Compile-PhoneShellShaders.ps1`；如修改 VR 中文菜单标签，运行 `tools/Update-OverlayLabels.ps1`。这些是资源生成器，不需要单独 UI 编辑器。WinForms Designer 文件保持可编辑。

如修改内置 Android server，见 `third_party/scrcpy/README.md`，其中包含固定上游版本和补丁。

服务配置：复制根目录 `service.config.example.json` 为 `service.private.json`，填写 Client 的 HTTPS 服务接口与 Deployment 的维护参数/凭证文件路径。私有文件已忽略，不得提交。构建只白名单导出 Client 到安装目录的 `app/service.config.json`，不会导出 Deployment；没有私有配置时输出禁用网络服务的配置，本地功能照常可构建。

发布签名需维护者自己的私钥及服务器资料；维护脚本默认读取同一个 `service.private.json`，显式指定旧 `release.local.psd1` 仍受支持。验证公钥可公开，私钥不可公开。网站源码在 website/vrpso，准备脚本为 tools/Prepare-Website.ps1，未自动部署。

准备首次公开提交时使用 `python tools/Prepare-PublicSource.py --output artifacts/public-source --prepare-first-commit`，得到经过隐私检查的源码 ZIP 和独立的新仓库。新仓库 main 尚无提交，源码已暂存，没有远端，也不共享原仓库对象；准备步骤不会提交或推送。原仓库只保留为本地恢复材料。公开源码不包含运行配置或维护凭证，接收方需自行配置服务；客户端运行配置里的通信地址可见，维护凭证始终不分发。

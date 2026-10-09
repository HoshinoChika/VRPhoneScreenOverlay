<div align="center">

<img src="docs/assets/app-icon.png" width="76" alt="VRPhoneScreen Overlay">

# VRPhoneScreen Overlay

**在 VR 里查看和操作你的 Android 手机。**

Windows x64 · SteamVR · Android · USB / 无线连接

**[网站下载](https://hoshinochika.com/) · [使用方法](#开始使用) · [反馈问题](https://github.com/HoshinoChika/VRPhoneScreenOverlay/issues)**

当前版本 **0.2.7** · 解压即用

<img src="docs/assets/vr-preview.jpg" width="960" alt="SteamVR 中的手机浮窗和手柄射线">

</div>

## 能做什么

| 功能 | 说明 |
| :--- | :--- |
| 操作手机 | 用手柄点击、滑动、滚动，也能返回、回到桌面和切换应用。 |
| 移动浮窗 | 抓取、缩放，放在顺手的位置；隐藏后可用手柄长按唤回。 |
| 手机声音 | 把支持采集的手机内部音频播放到电脑或头显。 |
| 有线和无线 | 支持 USB、扫码、配对码和手动 IP 连接，可记住设备。 |
| 调整画面 | 设置分辨率、码率和帧率，适应电脑性能和网络情况。 |
| 空间拖拽 | 可选功能，提供惯性、重力等设置。 |
| 手柄绑定 | 按手柄显示操作说明，可修改 SteamVR 绑定；保存后实时应用。 |

<table>
<tr>
<td width="50%"><img src="docs/assets/main-window.jpg" alt="主界面的连接状态和运行数据"><br><b>主界面</b> · 查看设备、连接和运行状态</td>
<td width="50%"><img src="docs/assets/video-settings.jpg" alt="分辨率、码率和帧率设置"><br><b>画面设置</b> · 按需要调整画质</td>
</tr>
</table>

截图为实际使用画面，点击图片可查看原图。

## 下载

**[打开 HoshinoChika 网站](https://hoshinochika.com/)，点击右上角“下载”。** 服务器会返回当前版本的 115 直链，无需登录 115。

下载的是完整 Windows x64 ZIP。解压后运行 `VRPhoneScreenOverlay.exe`，保留同目录的 `app` 文件夹。

[GitHub Releases](https://github.com/HoshinoChika/VRPhoneScreenOverlay/releases) 是另一下载入口，安装包上架后可在这里下载。当前仓库仍为私有，需要仓库权限才能访问。

## 开始使用

1. 在电脑上启动 SteamVR。
2. 手机开启开发者选项和 USB 调试，插上数据线，在手机上允许电脑连接。
3. 启动软件，进入“连接管理”，选择设备并连接。
4. 打开手机浮窗，在“功能”页查看手柄操作说明。

使用无线连接时，电脑和手机连接同一个局域网。手机开启无线调试，按软件中的扫码、配对码或 IP 指引操作。

## 使用要求

- Windows x64 电脑，SteamVR 正常运行。
- Android 手机或平板，需要调试授权；目前不支持 iPhone。
- 无线调试配对和内部音频通常要求 Android 11 或以上。部分系统需要额外开启输入权限。
- PICO、Quest 等头显通过电脑进入 SteamVR 使用，软件不是一体机 APK。不同设备的权限和按键可能有差别。
- 这是本机浮窗，同一个 VRChat 房间里的其他人不会自动看到你的手机。

## 遇到问题

手机无声时，检查系统版本、音频输出和应用是否允许录音。不能操作时，检查手机的调试和输入权限。SteamVR 超时时，检查运行状态和权限。

仍有问题可在软件“关于”页填写现象并上传诊断，或到 [Issues](https://github.com/HoshinoChika/VRPhoneScreenOverlay/issues) 反馈。请说明版本、设备和复现步骤。诊断不包含手机画面、音频或原始绑定文件。

<details>
<summary>0.2.7 更新内容</summary>

- 修改并保存手柄绑定后，运行中的浮窗同步应用新绑定。
- 功能说明刷新时保留现有内容，减少闪烁。
- SteamVR 连接超时后显示明确提示。
- 改善 SteamVR 手柄绑定页的打开方式。
- 自动更新和手动下载使用同一个全量包。

</details>

项目采用 GPL-3.0。第三方组件遵循各自许可证，安装包内附有说明。源码和构建文档整理后会补齐到本仓库。

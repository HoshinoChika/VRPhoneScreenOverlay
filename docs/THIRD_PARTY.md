# 第三方组件

- PICO 麦克风改善功能参考 Kill11 的 [PicoStreamingMicrophoneKeeper](https://github.com/Kill11/PicoStreamingMicrophoneKeeper) 和 fz6m 的 [问题分析](https://gist.github.com/fz6m/337aa44c27a7bde449ccff0e352f3040)。本仓库使用自己的 C# 实现，没有打包上游程序或 C++ 源文件。

- OpenVR：Valve 许可证，见 src/VRPhoneScreenOverlay.SteamVR/Resources/OpenVR/LICENSE-VALVE。
- Vortice：MIT，见同目录 Vortice-LICENSE；NuGet 依赖版本与完整依赖树由 packages.lock.json 固定。
- OVR Advanced Settings：GPL v3，空间运动实现的来源、修改与固定提交见 OVRAS-NOTICE.txt，许可证见 OVRAS-LICENSE.txt。
- scrcpy server：Apache 2.0，固定 v4.1；仓库附修改补丁与重建说明（third_party/scrcpy）。随包第三方通知见 Android/Resources/Scrcpy。
- Android platform-tools / ADB：随组件提供的 THIRD_PARTY_NOTICE.txt 保留在资源与用户包中。
- QRCoder：MIT，许可证在 App/Resources/Licenses。
- .NET、NAudio、Concentus 及其他 NuGet 组件遵循各自许可证；版本锁文件随源码提供。

第三方许可证、通知和修改补丁是分发的一部分，不应在打包时删除。本项目原有 GPL v3 许可不因更换服务域名或首次公开仓库而改变。

用户包 app/licenses 另附本项目 GPL v3、.NET Runtime/Desktop、NAudio、Concentus、SharpGen、Vortice.Mathematics 与 System.Numerics.Tensors 的许可证/通知。来自当前锁定包或其固定上游提交；用于保留分发许可声明。

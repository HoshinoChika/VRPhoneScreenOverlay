# 兼容条件与检查结果

本文件区分平台条件、软件仿真和真机验收。品牌名称存在于说明目录不代表该品牌全部型号已通过实际测试；合成数据只用于检查协议解析与行为规则。

## 手机

| 条件 | 能力边界 |
| --- | --- |
| Android 5.0 / API 21 及以上 | 满足当前 scrcpy 4.1 视频与输入协议的平台门槛；仍需 ADB 授权、可用 H.264 编码器及实际输入权限 |
| Android 11 / API 30 及以上 | 满足内部音频平台门槛；录音权限、音频编码器和应用限制仍可能使音频降级 |
| Android 11 | 开始音频捕获时需要屏幕解锁；音频失败不能影响视频和控制 |
| USB | 需要 USB 调试及电脑授权 |
| 扫码或配对码 | 需要系统提供 Android 无线调试配对功能及可达的同一网络 |
| 系统不提供 ADB / Android app_process | 不属于本程序当前手机链路的支持范围，例如 iOS |

厂商没有视频白名单。Google、Samsung、Xiaomi、OnePlus、HONOR、HUAWEI、Lenovo 的属性格式和 ARM32、ARM64、x86_64 字段以合成数据验证；这不证明上述厂商的实际型号均可用。部分 Xiaomi 设备还需启用“USB 调试（安全设置）”才能注入输入，应按设备系统提示处理。

仿真覆盖 480×800、720×1280、1080×1920、1080×2400、1440×3200、1840×2208、2560×1600、3040×1904、2160×3840、1920×1920 的原生/降采样、横竖屏和触控坐标空间。覆盖 API 19 的拒绝，以及 API 21、23、26、28、29、30、31、33、35、36 的视频/控制与音频分离规则。折叠、分屏、动态屏幕覆盖尺寸的实际切换仍需真机验证；测试尺寸不是实测机型名称。

## 头显与手柄

| 家族/路径 | 当前软件覆盖 | 真机状态 |
| --- | --- | --- |
| PICO，经 PC 串流进入 SteamVR | pico_controller 与保留的兼容内部类型、左右手布局 | 本轮未做硬件操作 |
| Meta Quest / Oculus，经 PC 桥接进入 SteamVR | oculus_touch；无法确认商品型号时通用组，可手动查看品牌组 | 本轮未做硬件操作 |
| HTC VIVE / VIVE Pro | vive_controller，触控板和菜单键配置 | 本轮未做硬件操作 |
| Valve Index | knuckles 默认配置与自定义绑定 | 本轮未做硬件操作 |
| Samsung / Lenovo / Dell WMR | holographic_controller；共享类型不猜品牌 | 本轮未做硬件操作 |
| HP Reverb G2 | hpmotioncontroller | 本轮未做硬件操作 |
| 未知驱动类型 | 通用说明；为真实 controller_type 生成通用默认，应用与失败回滚 | 软件仿真通过；实际键位是否存在需验证 |

PC 端需要 Windows x64、可用的 D3D11/Media Foundation 和 SteamVR/OpenVR。头显品牌不直接决定渲染路径：驱动必须向 SteamVR 提供可用的头显姿态、图形适配器以及控制器输入。PICO/Quest 原生独立应用模式不是本 Windows 程序的执行平台。

微软原生 WMR 在 Windows 11 24H2 起已被移除，原 WMR for SteamVR 路径不能仅靠本程序的默认绑定恢复；若另有可用的 SteamVR 驱动/桥接，其兼容性应单独实际验证。不能把 WMR 默认 JSON 仿真通过当作 Windows 11 24H2 原生 WMR 可用的证明。

所有七个内置驱动配置及通用模板验证左右手动作归属、手机抓取/触控、空间拖拽/重置、应用身份和未知类型保留。真实头显清晰度、纹理提交、游戏输入覆盖、串流延迟和连续运行稳定性不由这些配置测试替代。

## 真机验收范围

逐型号记录 USB/无线授权、H.264 首帧与横竖屏、触控四角、音频降级、断线恢复、头显浮窗、左右手按键、默认恢复与自定义绑定、Dashboard/游戏切换和持续运行。没有该型号的本轮硬件证据时，状态保持 NotTested；不得填成 Verified。

## 依据

- [固定上游 scrcpy 4.1 要求](https://github.com/Genymobile/scrcpy/blob/v4.1/README.md)
- [scrcpy 4.1 音频条件](https://github.com/Genymobile/scrcpy/blob/v4.1/doc/audio.md)
- [微软已移除的 Windows 功能](https://learn.microsoft.com/en-us/windows/whats-new/removed-features)
- 本仓库 AndroidCapabilityEvaluator、PhoneVideoOptionsFactory、AndroidDisplaySize、OpenVrBindingGuide、内置绑定及相应仿真测试。

# 0.7.3 显示刷新率策略

## 帧率档位

以 Unity `Screen.currentResolution.refreshRateRatio.value` 读取的当前桌面显示模式计算刷新率，并四舍五入到标称整数。Unity 2023.2 文档说明窗口模式下 `Screen.currentResolution` 返回桌面当前分辨率；当前 API 互操作结构也通过 AA host contract 检查。

| 显示刷新率 | 第一档 | 第二档 | ∞ 档 |
|---|---:|---:|---:|
| 60Hz | 30Hz | 60Hz | 与显示器同步至 60Hz |
| 75Hz | 38Hz | 75Hz | 与显示器同步至 75Hz |
| 120Hz | 60Hz | 120Hz | 与显示器同步至 120Hz |
| 144Hz | 60Hz | 120Hz | 与显示器同步至 144Hz |
| 240Hz | 60Hz | 120Hz | 与显示器同步至 240Hz |
| 320Hz | 60Hz | 120Hz | 与显示器同步至 320Hz |

低于 120Hz 时前两档为显示刷新率的一半和全速。120Hz 或以上时前两档为固定 60/120Hz。∞ 统一设置 Unity `vSyncCount=1`、`targetFrameRate=-1`，依 Unity 桌面渲染规则与显示刷新同步。

对前两档，如果显示刷新率能够以 Unity `vSyncCount` 的整数倍（1 到 4）精确得到目标帧率，就优先用 VSync；否则用 `targetFrameRate` 上限且关闭 VSync。例如 240Hz 可用 VSync 4/2 得到 60/120；320Hz 下 60/120 使用软件帧率上限。Unity `targetFrameRate` 是软件 pacing，不承诺完全无 microstutter。

显示模式每秒采样一次；如检测到刷新率变化，会保留当前所选 tier 并重新计算 cap。开屏时读不到有效刷新率，60/120 档保留原设置并延迟重试；∞ 档仍可立即用 VSync 1 保持不超过当前显示刷新率。

## 验证

- 映射单测覆盖 60、75、119.88、120、144、240、320Hz，以及 VSync divisor/软件 cap 策略和无效读数拒绝。
- HostProfile、scroll guard、interop mutation 与标签签名检查通过；完整源码干净解压构建成功。
- HostProfile/帧率策略 41 项、ScrollActivityGuard 16 项、Interop 元数据变异 21 项及回调签名 5 项通过，共 83 项。
- HostProfile/帧率策略 40 项、ScrollActivityGuard 16 项、Interop 元数据 21 项及回调签名 5 项通过，共 82 项。
- Unity 官方说明：[`Screen.currentResolution`](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Screen-currentResolution.html)、[`Application.targetFrameRate`](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Application-targetFrameRate.html)、[`QualitySettings.vSyncCount`](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/QualitySettings-vSyncCount.html)。

尚未在 AA 实机切换各类刷新率做实测；特殊多显示器与远程桌面输出方式仍需用户体验验证。

# 0.7.3 显示刷新率与静止编辑优化

## 帧率策略

以 Unity `Screen.currentResolution.refreshRateRatio.value` 读取当前桌面显示模式，并四舍五入到标称整数。窗口模式下 Unity `Screen.currentResolution` 返回桌面当前分辨率。

| 显示刷新率 | 第一档 | 第二档 | ∞ 档 |
|---|---:|---:|---:|
| 60Hz | 30Hz | 60Hz | 同步至 60Hz |
| 75Hz | 38Hz | 75Hz | 同步至 75Hz |
| 120Hz | 60Hz | 120Hz | 同步至 120Hz |
| 144Hz | 60Hz | 120Hz | 同步至 144Hz |
| 240Hz | 60Hz | 120Hz | 同步至 240Hz |
| 320Hz | 60Hz | 120Hz | 同步至 320Hz |

低于 120Hz 时，前两档为半刷新率/全刷新率；120Hz 及以上固定为 60/120。∞ 使用 `vSyncCount=1`、`targetFrameRate=-1`，不会在桌面上无限渲染。

前两档在刷新率能以 1 到 4 个整数 VSync 间隔精确得到目标帧率时使用 VSync，否则使用 `targetFrameRate` 上限。例如 240Hz 可用 VSync 4/2 得到 60/120，320Hz 下 60/120 使用目标帧率上限，∞ 同步至 320Hz。Unity 官方说明 `targetFrameRate` 是软件 pacing，可能有 microstutter。

显示模式每秒重新采样并重新应用当前档位；读不到刷新率时，有限档保留并延迟重试，∞ 仍使用 VSync 1。

## 静止编辑与操作安全

- 静止的嵌入预览不再因为预览对象存在就强制每帧绘制；检测到自动播放、语音、场景/背景/文字动画、延迟推进或背景效果时恢复保护。
- 已识别的背景、弹窗图片、音效、BGM、角色和 Mod 选择窗口可在无输入/加载时进入静态 cadence；未知窗口继续保护。
- Fix 宿主启用纯文本对白快速路径，但不接管预览纹理或独立相机；添加/删除对话期间保留原生同步，不异步修改数据。

## 验证边界

构建通过；46 项帧率/静态生产者与宿主策略、16 项滚动、21 项接口变异和 5 项回调形状检查通过，实际宿主解析通过。尚未启动 AA 做新版实机验收；Task Manager 的 3D 利用率不能单独证明 GPU 时钟或 CPU 瓶颈，大稿增删卡顿仍需体验确认。

Unity 依据：[Screen.currentResolution](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Screen-currentResolution.html)、[Application.targetFrameRate](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Application-targetFrameRate.html)、[QualitySettings.vSyncCount](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/QualitySettings-vSyncCount.html)。

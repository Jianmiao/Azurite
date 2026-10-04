# Azurite · 蓝铜矿

AA（AzureArchive）的日常编辑体验优化模组。提供滚轮调整、60 / 120 / ∞ 帧率档位和保守的静态页面按需绘制；通过固定接口与 AAVideoExport 交接渲染控制。

## 下载和安装

1. 退出 AA，在 [Releases](https://github.com/Jianmiao/Azurite/releases/latest) 下载 **Azurite-1.0.0.zip**。
2. 将 ZIP 内容直接解压到 **AA 本体的 mods 文件夹**。
3. 启动 AA，在模组管理器中选择 **Azurite 1.0.0** 并启用，按提示重启。

解压后的路径应为：

```text
AA 本体/
└─ mods/
   └─ Azurite/
      └─ 1.0.0/
         ├─ manifest.json
         ├─ Azurite.dll
         ├─ Azurite.Core.dll
         ├─ icon.png
         ├─ README.md
         ├─ SOURCE.md
         └─ SHA256SUMS.txt
```

不要额外套一层 `mods` 或压缩包名称目录，也不要放到 `BepInEx/plugins`。旧版目录可以保留，在管理器中切换版本，不要同时启用两份 Azurite。

普通安装无需 SDK、编译工具或 FFmpeg。GitHub 自动提供的 **Source code** 与另附的 **source ZIP** 是开发源码，不是安装包。

## 支持环境和功能

当前主要目标是 **Windows x64、AA 1.0.0-fix、正常工作的 BepInEx 6 IL2CPP / ModTheAzureArchive 环境**。安装包不含 AA 本体、模组加载器或用户资源。

| 项目 | 行为 |
|---|---|
| 帧率选择 | 低于 120Hz 使用半速/全速/∞；120Hz 及以上使用 60 / 120 / ∞；∞ 与当前显示刷新率同步 |
| 编辑滚轮 | 对话列表、背景图库、目录、属性和模组列表可分别调整；不改变鼠标拖动距离 |
| 滚动保护 | 滚轮、拖动、惯性、视口移动及结束后一秒保持正常绘制 |
| 静态页面 | 仅在活动状态可确认且没有预览、加载或导出工作时减少绘制；保留安全重绘 |
| 导出协作 | AAVideoExport 通过 `RenderControlV1` 接管前恢复设置，结束后交还 |
| 配置 | 使用当前 AA Profile 的 `configs/halocue.azurite.cfg`；安装包不携带个人配置 |

AAVideoExport 是可选模组。与其同时使用时，新版 AA 要求导出模组提供固定 `RenderControlV1` 接口；推荐 [AAVideoExport 0.2.0](https://github.com/Jianmiao/AAVideoExport/releases/tag/v0.2.0)。未知或不兼容的接口会让 Azurite 停止渲染调度。

Azurite 不控制 GPU 频率。GPU 降频可能来自驱动调度或等待 CPU，仅凭频率变化不能判断是否发生了跳帧。节省负载也不等于所有大稿都能流畅滚动。

## 0.7.1 继承的兼容范围

本版本从留存的 0.7.0 程序恢复可构建工程，并增加 AA 1.0.0-fix 适配。原始注释、独立测试和构建脚本没有完整找回；恢复来源和新增验证见 [SOURCE.md](https://github.com/Jianmiao/Azurite/blob/main/docs/SOURCE.md)。

新 AA 保留原生程序与元数据的版本检查，并核对实际使用的托管接口，不再要求其他电脑生成的 interop 文件与开发机逐字节相同。未知原生版本、缺失或不兼容接口仍会停用优化，日志给出原因。

**AA 1.0.0-fix 只启用经过接口验证的纯文本对白快速路径。** 布局缓存、屏外面板补丁、预览纹理和预览限频所有权仍关闭；这些限制不受旧配置中开关值影响。加载计数非零时保持正常绘制。旧版精确匹配的历史宿主保留原有能力，其版本绑定不代表广泛兼容承诺。

## 1.0.0 变更

- 将滚动输入/唤醒检查与完整视口运动采样分到 `Update` 与 `LateUpdate`，每帧只扫描一次活跃滚动视图；新 wheel 事件会立即重新发现当前活跃视图。
- 滚动保护期间跳过不影响安全判断的 HostActivity 状态遍历。活动滚动及结束后一秒仍保持 `renderFrameInterval=1`。
- 在经校验的 `SettingPanel.UpdateWidgets` 和 `OnFpsSliderChanged` 回调后立即修正标签，消除半秒轮询造成的原生 160/320 暂态；方法/属性缺失时关闭立即回调并保留慢速兼容刷新。

## 帧率规则

- 将刷新率四舍五入为标称整数。显示器低于 120Hz 时，前两个档位按半速和全速显示；60Hz 对应 **30 / 60 / ∞**。
- 显示器为 120Hz 或更高时，前两个档位固定 **60 / 120 / ∞**。
- 所有刷新率下，∞ 使用 VSync 1 与当前显示器同步；60Hz 时 60 档与 ∞ 档都会封顶在 60Hz。
- 对 60 / 120Hz 档位，若屏幕刷新率能以 VSync 的整数间隔精确得到目标帧率，则使用 VSync；否则使用 Unity 的目标帧率上限。比如 240Hz 可用 4/2 次 VSync 得到 60/120，320Hz 使用 60/120 软件上限，∞ 以 320Hz 同步。
- 每秒检测一次显示模式变化并重新计算当前档位；设置标签在 AA 更新设置或滑条后立即跟随显示器规则。
- 静止的 script 编辑预览和已识别的背景/资源选择窗口不再仅因预览对象存在而强制全速；动画、自动播放、语音、加载、滚动或未知窗口仍保持每帧绘制。
- Fix 宿主启用安全的纯文本对白快速路径，但不启用未经验证的预览纹理接管或相机所有权；新增/删除对话仍保持原生同步，不异步改数据。
- 新增对白增删事务门：原生插入、删除和列表同步仍完整同步执行；事务期间与完成后的 settle 窗口禁止布局缓存/面板实验和静态降帧，避免半重排状态被优化逻辑读取。
- 静态场景动态渲染只对已识别的编辑表面生效；未知窗口和无法确认的动态源保持全帧。

## 验证

已完成源码构建、宿主接口静态检查、版本保护和滚动保护回归，以及安装 ZIP 的路径、程序集与校验和检查。持续集成在不依赖 AA 的环境中运行可移植测试；本版 46 项帧率/兼容、16 项滚动、26 项接口检查通过。

本次 1.0.0 的静态场景和大稿保护仍需 AA 实机确认。上述回归不等于已证明 AA 整体主线程耗时、GPU 频率或大稿实际帧时间/流畅度改善，也不承诺具体性能倍数。未知窗口、活动动画、加载、滚动和增删操作保持保守全帧。

## 开发

安装 .NET SDK，在已初始化 interop 的 AA 环境构建：

```powershell
./Build.ps1 -AAInstallPath 'C:\Games\AzureArchive'
dotnet run --project Tests/HostProfile.Tests.csproj -c Release
dotnet run --project ScrollTests/ScrollTests.csproj -c Release
dotnet run --project InteropTests/InteropTests.csproj -c Release
```

宿主引用只从指定 AA 目录读取，不随产物复制。`Core/` 保留恢复源；本次插件继续引用 `references/Azurite.Core.dll` 的原版 Core，以减少重建范围。[第三方组件说明](https://github.com/Jianmiao/Azurite/blob/main/THIRD_PARTY_NOTICES.md)。

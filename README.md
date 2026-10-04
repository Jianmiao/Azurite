# Azurite 0.7.1 — 恢复工程与 AA 1.0.0-fix 适配

本工程从已归档的 Azurite 0.7.0 .NET DLL 反编译恢复，再增加新版 AA 的受限兼容配置。
它不是原始源码或原始测试的完整备份。原工程先前位于 `F:\AzureArchive_Win_1.0_beta\mods\Azurite`，当前该目录不存在；本次没有确定删除或移动的原因。

## 来源

- 原插件：`E:\aamod\Azurite\0.7.0\Azurite.dll`，SHA-256 `A5AB09163853364F393E65818C23C80FDE47CE3F40312746605DE469E6BC415F`。
- 原 Core：`references/Azurite.Core.dll`，SHA-256 `76A46C4A896DFBDFFD66C2BE85E9A0E07D2D0523FAAEF0841763D997C65DF1A5`。
- 反编译工具：ILSpyCmd 9.1.0.7988。`Core/` 保留可构建的恢复源供维护；本次运行包仍使用原 Core DLL。
- 恢复不能找回原注释、原独立测试及构建脚本。此目录中的测试和脚本是本轮新增。
- 宿主 AA、Unity、BepInEx、interop 文件仅用于本地引用，不随源码或 Mod 包分发。

## 0.7.1 适配范围

新增 `HostProfile`，仅在原生宿主及两个生成绑定文件的整组 SHA-256 匹配时启用功能；未知版本及混合绑定仍停止优化。

AA 1.0.0-fix 配置启用滚轮倍率、滚动活动保护、60/120/∞ 档位以及保守的静止页面按需绘制。滚动输入、拖动、惯性、视口变化和结束后一秒均阻止空闲跳帧。设置标签直接刷新，不安装设置方法的 Harmony 钩子。

新版暂不安装旧版专用的文字解析、布局缓存、屏外面板或方法计时原生补丁，也不复用旧预览纹理及预览限频优化。加载计数非零时保持正常绘制，不沿用旧版的加载计数例外。旧配置不能绕过这些能力限制。旧版受支持宿主保留原有能力。

新版与 AAVideoExport 通过固定 `RenderControlV1` 交接；不使用导出模组私有方法兼容层。0.2.0 及先前带该协议的构建均可按接口连接。

## 构建

安装 .NET SDK，使用已完成 BepInEx/interop 初始化的本地 AA：

```powershell
./Build.ps1 -AAInstallPath 'F:\AzureArchive_100_fix'
dotnet run --project ./Tests/HostProfile.Tests.csproj -c Release
dotnet run --project ./ScrollTests/ScrollTests.csproj -c Release
```

运行文件为构建输出中的 `Azurite.dll`、原 `references/Azurite.Core.dll` 和 `assets/` 的图标、manifest。普通用户使用单独提供的安装 ZIP，解压到 AA 的 mods 文件夹。

## 验证边界

本轮不启动 AA，不操作用户界面。宿主接口和帧率原生路径已做静态检查；回归测试使用真实实现及必要的宿主替身。通过构建和测试不等于已确认真实滚动帧时间、GPU 频率或大稿体验恢复。

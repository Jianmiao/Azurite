# Azurite · 蓝铜矿

AA（AzureArchive）的日常编辑体验优化模组。提供滚轮调整、60 / 120 / ∞ 帧率档位和保守的静态页面按需绘制；通过固定接口与 AAVideoExport 交接渲染控制。

## 下载和安装

1. 退出 AA，在 [Releases](https://github.com/Jianmiao/Azurite/releases/latest) 下载 **Azurite-0.7.1.zip**。
2. 将 ZIP 内容直接解压到 **AA 本体的 mods 文件夹**。
3. 启动 AA，在模组管理器中选择 **Azurite 0.7.1** 并启用，按提示重启。

解压后的路径应为：

```text
AA 本体/
└─ mods/
   └─ Azurite/
      └─ 0.7.1/
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
| 帧率选择 | 将 AA 的三个档位映射为 60 / 120 / ∞；不修改显示器刷新率 |
| 编辑滚轮 | 对话列表、背景图库、目录、属性和模组列表可分别调整；不改变鼠标拖动距离 |
| 滚动保护 | 滚轮、拖动、惯性、视口移动及结束后一秒保持正常绘制 |
| 静态页面 | 仅在活动状态可确认且没有预览、加载或导出工作时减少绘制；保留安全重绘 |
| 导出协作 | AAVideoExport 通过 `RenderControlV1` 接管前恢复设置，结束后交还 |
| 配置 | 使用当前 AA Profile 的 `configs/halocue.azurite.cfg`；安装包不携带个人配置 |

AAVideoExport 是可选模组。与其同时使用时，新版 AA 要求导出模组提供固定 `RenderControlV1` 接口；推荐 [AAVideoExport 0.2.0](https://github.com/Jianmiao/AAVideoExport/releases/tag/v0.2.0)。未知或不兼容的接口会让 Azurite 停止渲染调度。

Azurite 不控制 GPU 频率。GPU 降频可能来自驱动调度或等待 CPU，仅凭频率变化不能判断是否发生了跳帧。节省负载也不等于所有大稿都能流畅滚动。

## 0.7.1 的范围

本版本从留存的 0.7.0 程序恢复可构建工程，并增加 AA 1.0.0-fix 适配。原始注释、独立测试和构建脚本没有完整找回；恢复来源和新增验证见 [SOURCE.md](https://github.com/Jianmiao/Azurite/blob/main/docs/SOURCE.md)。

新 AA 保留原生程序与元数据的版本检查，并核对实际使用的托管接口，不再要求其他电脑生成的 interop 文件与开发机逐字节相同。未知原生版本、缺失或不兼容接口仍会停用优化，日志给出原因。

**AA 1.0.0-fix 暂不启用旧版专用的文字解析、布局缓存、屏外面板补丁，以及预览纹理和预览限频优化。** 这些限制不受旧配置中开关值影响。加载计数非零时保持正常绘制。旧版精确匹配的历史宿主保留原有能力，其版本绑定不代表广泛兼容承诺。

## 验证

已完成源码构建、宿主接口静态检查、版本保护和滚动保护回归，以及安装 ZIP 的路径、程序集与校验和检查。持续集成在不依赖 AA 的环境中运行可移植测试。

本次发布未启动 AA 做新版实机验收。上述检查不等于已证明 GPU 频率、实际帧时间或所有大稿体验正常，不承诺具体性能倍数。

## 开发

安装 .NET SDK，在已初始化 interop 的 AA 环境构建：

```powershell
./Build.ps1 -AAInstallPath 'C:\Games\AzureArchive'
dotnet run --project Tests/HostProfile.Tests.csproj -c Release
dotnet run --project ScrollTests/ScrollTests.csproj -c Release
dotnet run --project InteropTests/InteropTests.csproj -c Release
```

宿主引用只从指定 AA 目录读取，不随产物复制。`Core/` 保留恢复源；本次插件继续引用 `references/Azurite.Core.dll` 的原版 Core，以减少重建范围。[第三方组件说明](https://github.com/Jianmiao/Azurite/blob/main/THIRD_PARTY_NOTICES.md)。

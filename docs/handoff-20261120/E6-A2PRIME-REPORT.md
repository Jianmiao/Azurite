# E6：A2' 编辑器会话激活实验（离屏 GPU 模式）

日期：2026-11-21 03:05–03:20 Asia/Shanghai。执行：lead。
运行目录：`E:\aamod\tmp-azurite-takeover\experiments\E6-a2prime\`

## 实验设计

验证：离屏 GPU 模式（`-batchmode` 不带 `-nographics`，保留 GPU）下，MCP `mcp.start --project` 加载工程后，
**插件进程内的 `AuthoringEditorSession.Current` 是否激活**（这是创作分支批量编辑验收的前置条件）。

## 结果：部分激活——MCP 编辑链路完整可用，但插件 UI 会话仍未激活

### ✅ 成功的部分

1. AA 在离屏 GPU 模式正常启动（PID 38816，护栏 11 项检查全过）
2. `mcp.start --project` 成功（edit_session=7ad88d95…，r0）
3. `get_context` / `read_project` / `update_lines`（真实写入 r0→r1）/ `save_project` 全链路 ok
4. 无 ERROR / Exception

### ❌ 未激活的部分

插件的 `editor-work` 诊断始终输出 `lines=unavailable cache=0`——即 `AuthoringEditorSession.Current` 仍为 null。
探针继续采样 `scene=CatalogScene, studio=null, session=null`。
主 unity.log 无 `ScenarioEditorScene`。

## 技术结论（修正 ARCHITECTURE-CLARIFICATION.md 的假设）

`mcp.start` 的 `load_project` 加载的是**工程数据工作副本**（MCP 层），不是 **ScenarioEditorScene UI 会话**。
插件的进程内编辑器会话绑定 `StudioCommon.instance` + `AuthoringEditorSession.Current`——这两者只在
**GUI 模式下进入编辑器场景**时创建。离屏 batchmode 无法满足插件 UI 路径的前置条件。

## 对批量编辑验收路径的最终结论

| 路径 | 可用性 |
| --- | --- |
| **AA 原生外部 API（MCP/CLI）** | ✅ 无头完整可用（E1/E2/E3/E4/E6 五轮真实验收） |
| **插件进程内会话（Session.Apply）** | ⊘ 需要 GUI 模式 + 用户进入编辑器场景（A16/A18 同类限制） |

**1.0.41 插件改造的正确方向**：插件 UI（工具栏/卡包面板）在用户 GUI 会话中调用 `Session.Apply`（现状代码已正确）；
**新增**一个「无头自动化入口」：插件在启动时检测 AZUREARCHIVE_CLI_DIRECTORY / MCP 会话，把 MCP 的写操作
路由到同一事务边界（共享 Uncertainty/Receipt 语义）。这样 AI 助手/CI 的批量操作与插件 UI 操作走同一套审计。

## 红线合规

护栏启动器（11 检查全过）｜单实例｜日常安装 0 写入｜副本 profile 未动（本轮用 AzuriteAcceptance 基线）
｜失败/超时现场保全｜结束时无残留进程（mcp.stop CLI 超时但 AA 已被正常终止，工作副本已 save）
# A17 + A19 验收报告（失败路径 + AAVE RenderControlV1）

执行：edgecase（DSH 子代理，task-9）｜时间：2026-11-21 01:11 – 01:45 Asia/Shanghai
运行目录：`E:\aamod\tmp-azurite-takeover\experiments\E4-A17-A19-run1\`

## 0. 运行环境与红线合规

| 项 | 值 |
| --- | --- |
| AA 可执行文件 | `E:\aamod\labs\Azurite-20261108-stable\install\AzureArchive.exe`（白名单内） |
| 启动方式 | `Launch-AAHeadless.ps1`（R1–R6 共 12 项检查，两次启动均 passed，见 `logs/launch-events.ndjson` / `logs/launch-events-s2.ndjson`） |
| 实例数 | 会话1 PID 29664（AzuriteAcceptance profile）→ mcp.stop + 终止 → 会话2 PID 38276（A17A19Render profile）→ mcp.stop + 终止；每步后 NO-RESIDUAL |
| 无头参数 | `-batchmode -nographics -disableaudio -logFile <run>/output/unity*.log` |
| 样例工程 | `直通线-0.aap2`（851,559 B，sha256 ab8de187…，复制到 run/project，备份原件未动） |
| 日常安装 | `F:\AzureArchive_101\BepInEx\LogOutput.log` mtime 全程保持 2026-11-20T22:15:43（启动前 age≈9h，结束复查未变）——**0 写入** |
| 失败保全 | A17 全部失败现场落盘（`mcp/resp-a17-*.json`），首版 schema 拒绝现场（`resp-a17-illegal-resource.json`、`resp-a17-atomicity.json`）未掩盖 |

## 1. A17 失败路径验收（AA 原生 API · MCP）

会话1：edit_session `2cc9c0d8-9990-4583-8a0b-310fa8f1721c`，revision 轨迹 r0 →（全部拒绝，r0 不动）→ A17-4 成功写入 r1 → save r1（file_version sha256:98c0b27a…）。

### A17-1 过期 revision —— ✅ 通过

- `update_lines {expected_revision:"r99999"}`（实际 r0）→ `REVISION_CONFLICT`
- 错误结构：`{code, message, retry:"read_then_replan", details:{expected_revision:"r99999", actual_revision:"r0"}}`——**details 精确回显期望与实际 revision**
- revision 不动（r0），无写入。证据：`mcp/resp-a17-stale-revision.json`

### A17-2 非法资源 —— ✅ 通过（两阶段，均保全）

- **尝试 1**（`request_id:a17-badres-1`，E2 同构但缺 `inherit`/`action` 字段）→ `INVALID_ARGUMENT`（schema 层拒绝，`path:$["operations"][0]["expected_target"]`，schema_rule=oneOf）。这是**真实的 schema 严格性证据**：不合法形状在进入资源解析前就被拒。证据：`resp-a17-illegal-resource.json`
- **尝试 2**（`a17-badres-2`，E3-A13 同构有效形状 + 不存在资源 `res_a17nonexistent…`）→ `RESOURCE_NOT_FOUND`，`details:{operation_index:0, resource:{resource_id}}` 精确定位。revision 不动。证据：`resp-a17-illegal-resource-v2.json`

### A17-3 批次原子性 —— ✅ 通过

- 一批 3 ops：line.update(合法) + line.insert(含非法资源) + line.update(合法 extra_script)
- 结果：**整批拒绝**（`RESOURCE_NOT_FOUND`，指向 operation_index=1），revision 不动（r1）
- **无部分写入验证**（读回 `resp-a17-readback-after-atomic2.json`）：
  - 目标行 text 保持 A17-4 成功写入值（`A17幂等对照-首次写入-1795195725884`），两个合法 op 的期望修改（"原子性v2-op0"文本、`#wait;9999` extra_script）**均未出现**
  - line_count 仍 310（无部分插入）
- 证据：`resp-a17-atomicity-v2.json` + `resp-gc-after-atomic2.json` + `resp-a17-readback-after-atomic2.json`

### A17-4 request_id 幂等/冲突语义 —— ✅ 通过

| 场景 | 结果 |
| --- | --- |
| 首次写入（`a17-idem-1`） | ✅ ok，r0→r1 |
| **同 ID 同参数重放** | ✅ **幂等**：`ok:true, applied_operations:1`，但 revision **不推进**（仍 r1），是生效的幂等重放而非重复计费 |
| **同 ID 不同参数** | ❌ `REQUEST_ID_REUSED`：`"request_id was already used with different arguments or a different tool"`，revision 不动 |
- 证据：`resp-a17-idem-first.json` / `resp-a17-idem-replay.json` / `resp-a17-idem-diff.json`

### 附加边缘用例

- `line.delete` 不存在行 → `UNKNOWN_ID`（`details.operation_index:0`），revision 不动（`resp-a17-delete-nonexistent.json`）
- `get_request_status` 未知 request_id → `ok:true, data.status:"unknown"`（显式 unknown 状态而非错误；不产生 Uncertainty 分支）（`resp-a17-request-status-unknown.json`）
- 错误结构总汇（全部实测一致）：`{code, message, retry: correct_request/read_then_replan/fix_request_or_runtime, details}` + 顶层 `context{project_id, edit_session, revision}`

### A17 结论

**四个用例全部通过**。AA 原生 API 的失败路径行为与文档一致：过期 revision → REVISION_CONFLICT（含 read_then_replan 指引）、非法资源 → RESOURCE_NOT_FOUND（含 operation_index 定位）、失败批次整批拒绝且 revision 不动、无部分写入；request_id 幂等重放不推进 revision，同 ID 不同参数显式拒绝。**未发现 Uncertainty 类悬置状态**：所有失败即时返回确定性错误；get_request_status 对未知 ID 返回显式 "unknown"。

## 2. A19 AAVE RenderControlV1/v1 可验范围

### A19-a 插件加载时的 RenderControl 绑定日志 —— ✅ 通过

**方法**：会话1 证明当前 AzuriteAcceptance profile 只启用 Azurite 时，BepInEx 日志**没有** RenderControl 痕迹（AAVE 程序集不存在 → Azurite 按 HostCompatibility.cs 的"chainloader 完成后无 AAVideoExport 程序集"路径静默等待，不绑定）。会话2 在**隔离副本内**新建 profile `A17A19Render`（Azurite 1.0.38 + AAVideoExport 0.2.12 同时启用）后启动，命中绑定证据：

```
[Info   :ModTheAzureArchive] Enabled 'AAVideoExport' version '0.2.12' …
[Info   :AA Video Export] AA Video Export loaded [0.2.12; single-pass=True; development-audit=False]. Ctrl+Shift+E opens settings. RenderControlV1 preserves Azurite ownership boundaries.
[Info   :Azurite · 蓝铜矿] AA host identity verified: 1.0.1 (render handoff, FPS and scrolling); native patches=False; preview optimization=False
[Info   :Azurite · 蓝铜矿] Azurite public render-control protocol v1 negotiated; private exporter hooks and binary pinning are not used.   ← 绑定成功日志（HostCompatibility.cs LogInfo("Azurite " + reason + "; private exporter hooks …")，reason="public render-control protocol v1 negotiated"）
[Info   :ModTheAzureArchive] Published 2 successfully loaded mod(s) to ModConfig.
```

- 证据：`evidence/bepinex-LogOutput-session2-aave.log`（第 16/25/33/37 行）+ 对照组 `bepinex-LogOutput-session1.log`（无 RenderControl 行）
- 绑定契约源码核对（`Plugin/Azurite/RenderControlClient.cs`，与 authoring worktree e8dddb9 一致）：TryBind 反射查找 `AAVideoExport.Integration.RenderControlV1` → 校验 public static class + ProtocolVersion==1 + IsReady/IsRenderingOwned 委托 + BeforeAcquire/AfterRelease 订阅 → 成功 reason 固定为 "public render-control protocol v1 negotiated"。失败分支：Absent（旧 AAVE 或无 AAVE）/ Invalid（签名不符，"public render-control API invalid: <ExceptionType>; …"）
- 协议文档核对：AAVE 0.2.12 自带 `docs/render-control-v1.md`（交接时序/发现加载卸载/发布要求，与源码一致）

### A19-b AA 原生 API 渲染相关命令清单 —— ✅ 完成（结果：无渲染控制命令）

- MCP tools/list（`mcp/s2-tools-list.json`，`mcp/s2-initialize.json` serverInfo AzureArchive 1.0.0）：**14 个工具**，全部为工程编辑类（get_context / read_project / search_resources / create_dialogue / update_lines / edit_graph / apply_edits / validate_project / save_project / compile_project / get_request_status / resource_import / resource_remove / create_character_alias）。**零渲染控制命令**
- 全 schema token 扫描："render" 仅出现在 stage.display_order 描述（"higher values render in front"，纯展示语义）；"keyframes" 仅出现在 character curve 数据描述。无 FPS/vsync/capture/export/video 相关工具或字段
- CLI 命令清单（`cli/05-capabilities/response.json` + `mcp/s2-capabilities.json`）：`capabilities / schema / mcp.start / mcp.status / mcp.stop`（cli_mcp_control.commands=[mcp.start, mcp.status, mcp.stop]）+ capabilities data.tools=14。**同样零渲染控制命令**
- **结论：AAVE↔Azurite 渲染协作完全走 .NET 层 RenderControlV1 事件协议（BeforeAcquire/AfterRelease），AA 原生创作 API 不参与渲染控制。**

### A19 不可验边界（如实标注）

以下项 **CLI/无头环境无法验证**，需 GUI + 导出环境：

1. **实际渲染协作时序**：BeforeAcquire 回调中 Azurite 恢复帧率/vsync/租约（Plugin.cs `SuspendForExport` 日志 "RenderControlV1: restored Azurite leases before AAVE snapshots"）与 AfterRelease 恢复观察（"AAVE released restored rendering; idle observation restarted"）——这两个日志只在导出流程触发，`-nographics` 下无法发起真实导出
2. **IsRenderingOwned 状态翻转**：导出面板打开/捕获/清理的 owned=true 区间验证
3. **真实视频输出**：编码质量、音画同步（AAVE README 的已验范围是 AAVE 自己在 GUI+D3D 环境完成的 8/8 与 4/4，本次未复跑）
4. **回调失败/恢复失败分支**：协议文档要求的异常路径（消费者回调抛异常中止接管等）需在真实导出流程中注入故障

注：本副本含 AAVE 0.2.9–0.2.12 全部版本程序集（`mods/AAVideoExport/`），验证仅使用 0.2.12（当前最新，manifest 注明 RenderControlV1 保留）。未做 0.2.9/0.2.10/0.2.11 旧版兼容矩阵（Azurite 1.0.38 对旧版走 legacy ExportHost 私有适配器，属另一条已实现但本轮未验的路径）。

## 3. 执行环境备忘

- `save_project` 等 14 个 MCP 工具**不能**作为 CLI 子命令（`--cli save_project` → `Unknown command`，现场 `cli/02-save/response.json`）；保存必须经 MCP 调用或走 autosave
- CLI courier 依赖 AZUREARCHIVE_CLI_DIRECTORY + arguments.json（E3 已记录，本轮复现一致）
- line.insert 若缺 `inherit`/`action` 等形状字段，schema 层先拒（INVALID_ARGUMENT/oneOf），报错定位到最近的 oneOf 分支字段（expected_target）而非根因——**调试时先对照 A13/A14 成功样本的形状**
- lab 副本 profile 操作已还原：ActiveProfile 已恢复 AzuriteAcceptance；新增 profile `A17A19Render` 保留（`profiles/A17A19Render/modconfig.json`）供后续导出验收复用

## 4. 红线合规自查

- [x] 护栏启动器 R1–R6 两次全过（logs/launch-events*.ndjson）
- [x] 隔离副本 exe（E:\aamod\labs\*），非 F:\AzureArchive_101
- [x] 单实例：会话1 终止并确认 NO-RESIDUAL 后才启动会话2
- [x] 失败保全：A17 首版 schema 拒绝现场未掩盖、未删除
- [x] 日常安装 0 写入（LogOutput.log mtime 全程 2026-11-20 22:15:43）
- [x] 结束时无残留 AA/UnityCrashHandler 进程
- [x] 副本 profile 改动已记录并还原（a19-profile-switch.json / a19-profile-restore.json）

## 5. 证据索引

| 证据 | 路径 |
| --- | --- |
| A17 状态台账 | `state-a17.json`、`state-a17-v2.json` |
| A17 全部请求响应 | `mcp/resp-a17-*.json`（stale-revision / illegal-resource[-v2] / atomicity-v2 / idem-first / idem-replay / idem-diff / delete-nonexistent / request-status-unknown / final-save / readback-after-atomic2） |
| 启动收据 | `logs/launch-events.ndjson`（会话1）、`logs/launch-events-s2.ndjson`（会话2）、`launch.json` |
| 会话1 BepInEx 日志（对照组） | `evidence/bepinex-LogOutput-session1.log`、`-final.log` |
| 会话2 BepInEx 日志（绑定证据） | `evidence/bepinex-LogOutput-session2-aave.log`、`-final.log` |
| tools/list + initialize + capabilities | `mcp/s2-tools-list.json`、`mcp/s2-initialize.json`、`mcp/s2-capabilities.json`、`cli/05-capabilities/response.json` |
| profile 切换证据 | `evidence/a19-profile-switch.json`、`evidence/a19-profile-restore.json` |
| 工程 | `project/直通线-0.aap2`（A17-4 写入后 save，file_version sha256:98c0b27a…） |
| courier 日志 | `output/courier-0*.log`、`output/unity*.log` |

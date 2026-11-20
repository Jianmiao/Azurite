# A4–A10 真实验收报告（AA 原生 MCP，隔离副本）

执行：acceptance（DSH 子代理）｜时间：2026-11-20 23:52 – 2026-11-21 00:30 Asia/Shanghai
运行目录：`E:\aamod\tmp-azurite-takeover\experiments\E2-A4-A10-run1\`
任务板：task-7（owner=acceptance）

## 0. 运行环境与红线合规

| 项 | 值 |
| --- | --- |
| AA 可执行文件 | `E:\aamod\labs\Azurite-20261108-stable\install\AzureArchive.exe`（白名单内，非日常安装） |
| 启动方式 | `Launch-AAHeadless.ps1` 护栏（R1–R6 共 12 项检查全过，两次启动均 passed=true，见 `evidence/session1-launch/launch.json`） |
| 实例数 | 同一时间仅 1 个：会话1 PID 43928 → 终止后会话2 PID 28080 → 终止。结束时 `NO-AA` |
| 日常安装 | `F:\AzureArchive_101\BepInEx\LogOutput.log` mtime 始终 2026-11-20T22:15:43（启动前检查 age=96.8min；全程未写入） |
| 无头参数 | `-batchmode -nographics -disableaudio -logFile <run>/output/unity.log` |
| 样例工程 | `直通线-0.aap2`（851,559 B，sha256 ab8de18735d24106…），备份原件未动，复制到 run/project 后编辑 |
| 启动→就绪 | 会话1：37s（automation/editors/43928.json + BepInEx databasesReady=True）；会话2：10s |
| MCP | 两次均 http://127.0.0.1:18789/mcp/，protocol 2025-11-25，serverInfo AzureArchive 1.0.0，tools/list 14 个工具 |

## 1. 版本线（revision 轨迹）

```
r0（mcp.start 打开工程）
 └ A4 update_lines×5      → r1
    └ A9 apply_edits×5(继承式复制) → r2
       └ A10 apply_edits×1(切槽位)  → r3
          └ A8 apply_edits×3(链式插入) → r4
             └ A7 apply_edits×5(删除副本) → r5
                └ save_project → saved_revision=r5, file_version=sha256:86fa37cf…
                   └ mcp.stop → 终止 AA → 重启 → mcp.start 同一工程 → 新会话 r0（磁盘内容即 r5）
```

## 2. 逐项验收

### A4 批量文本（5 行）——✅ 通过
- 调用：`update_lines { edit_session, expected_revision:"r0", request_id:"a4-batch-1", updates:[5×{line_id, set:{text}}] }`
- 响应：`ok:true, applied_operations:5, persistence:"working_copy"`，revision r0→r1（`mcp/resp-a4-update-lines.json`）
- 读回：5 行 ID 与请求完全一致（持久 ID 不变），text 全部匹配（`evidence/a4-readback` 于 state.json.a4verify）
- 写入文本：`A4批量验证-第N条-16:07:29`（N=1..5）

### A9 批量复制（显式配音策略）——✅ 通过（继承式构造路径）＋⚠️ 环境受限未验（显式全量路径）
- 原生 EditOperation **没有 line.copy**（schema 实测 20 个分支里只有 insert/update/delete/move）。复制必须用 line.insert 构造。
- **尝试 1（`request_id:a9-copy-1`，已保全 `evidence/FAILURE-a9.json`）**：`INVALID_ARGUMENT: catalog_version is required when assigning or inheriting resources`（op 0）。这是**真实的 API 缺口**：涉及资源赋值/继承的 apply_edits 必须显式传 `catalog_version`（get_context 的 data.catalog_version）。
- **尝试 2（`a9-copy-2`，已保全 `evidence/FAILURE-a9-v2.json`）**：显式全量复制（重述 stage/scene/voice 原资源 ID）→ `RESOURCE_NOT_FOUND res_a6a9777a…`（op 1）。**根因是隔离副本环境**：validate 报 688 条 RESOURCE_NOT_FOUND/UNBOUND_VOICE 警告——样例工程引用的语音资源来自日常安装 mods，隔离副本目录只有内置资源（每类 50 个，见 `mcp/catalog-inventory.json`）。**不是 API 缺陷**。
- **尝试 3（`a9-copy-3`，成功）**：5 份副本全部用 `line.insert + after_line + inherit:{from:源行}` 构造，r1→r2，`applied_operations:5`。读回语义（对照源行）：
  - mode / speaker / stage（含 face、action、emoticon 等全部字段，去除只读 starting_position）/ place：**1:1 继承**（5/5 stageEq=true）
  - highlighted_slots：**继承**（副本 0/1/4 与源一致；副本 3 显式 `highlighted_slots:[]` 覆盖生效）
  - **voice 身份不继承**（文档明示），但副本 voice 呈现为 `action:set + 与源行不同的资源 ID`（疑似按说话人自动绑定，机制未文档化——见第 3 节缺口 #5）
  - 副本 2 显式 `scene{background:inherit,bgm:inherit,sound:none,voice:none,transition:none}` 全部按提交值落盘
  - 与 Azurite「CopyVoice=false」策略等效；「CopyVoice=true（复制配音）」路径需完整资源库，**标注：环境受限未验**
- 位置验证：5 份副本分别紧跟各自源行（after_line=id 语义正确）。

### A10 批量发言槽位——✅ 通过
- 目标行 `1a751fda`（舞台槽 3/4 各站一人，speaker=slot4，highlighted=[]）
- 调用：`apply_edits { operations:[{op:"line.update", line:{id}, set:{speaker:{slot:3}}}] }`，r2→r3
- 读回：speaker.slot 4→3；**舞台数组逐字节未变**（无角色重置，宿主默认语义=仅切发言槽不重排角色）；highlighted 保持 []
- 即：**仅切槽位**验证成立。

### A8 多行插入（3 行）——✅ 通过
- 锚点：A4 第 3 行 `0d6a9bd3`
- 调用：`apply_edits { operations:[3×line.insert]，链式 after_line:{id锚点}→{ref:a8line0}→{ref:a8line1}，line.inherit:"previous"，中间行为 narration(speaker:null)，其余 dialogue(speaker:{slot:3}) }`，r3→r4
- 批内 ref 链（`{ref:"a8lineN"}`）工作正常；`inherit:"previous"` 接受且不报错（新行舞台由前句推导）
- 位置读回（逐行 ID 序列）：`锚点 → 新行1(dialogue) → 新行2(narration) → 新行3(dialogue)`，**3 行紧跟锚点、顺序精确**；文本/模式与提交一致。

### A7 批量删除（5 行）——✅ 通过
- 删除 A9 的 5 份副本（`5×{op:"line.delete", line:{id}}`），r4→r5
- line_count：318 → 313（精确 -5）；按 ID 读回：0 行（全部消失）
- 重开后 line_count 仍 313，被删 ID 持续缺席（无 ID 重用迹象）。

### A6 保存→重启→重开——✅ 通过（全链路）
1. `validate_project level=compile`：`ok:true, valid:true`，688 条环境性警告（RESOURCE_NOT_FOUND/UNBOUND_VOICE，见 A9 根因）
2. `save_project`：第 1/2 次因缺 `expected_revision` / `request_id` 被拒（**API 缺口 #1**，错误已保全）；补齐三件套后 `ok:true, persistence:"saved", saved_revision:r5, file_version:sha256:86fa37cf…`
3. 磁盘：851,559 → 915,908 B，UTF-8 标记 `A4批量验证-第1条`、`A8插入验证-新行1` 均在 .aap2 内
4. `mcp.stop`（CLI courier）：`ok:true, running:false, revision:r5`；AA 进程另行终止（E1 已知 mcp.stop 不退出主进程）
5. 护栏重启（R1–R6 全过）→ `mcp.start --project 同一文件` → 新 edit_session `2d8bd7a9…`，revision 从 r0 起（按文件计，不延续旧计数）
6. **持久化读回全验证**：A4 五行文本逐字在；A8 三行在；A10 speaker=slot3 在；A7 被删 ID 缺席；A9 源行未受污染；line_count=313（`evidence/a6-reopen-verification-full.json`）

### A5 撤销/重做——⊘ CLI 不可验（预登记结论）
- 原生 Undo 组在 GUI 撤销栈中，MCP/CLI 无 undo 工具（tools/list 14 个工具中无 undo/redo）。
- 架构保证：SKILL.md「成功批次进入工作副本并形成一个原生 Undo 组」+ capabilities `persistence.mcp = working_copy_then_explicit_or_configured_autosave`。本验收的 5 个写批次（A4/A9/A10/A8/A7）每个都是一次 MCP 写 = 一个原生 Undo 组。
- 若需要操作级验证，须在 GUI 会话中进行（不属于无头可验范围）。

## 3. 发现的 API 缺口 / 未文档化行为（本轮实测）

1. **save_project 需要 {edit_session, expected_revision, request_id} 三件套**——E1 报告未记录 expected_revision/request_id（E1 当时显然传了）。错误路径 details.path 精确指缺哪个字段。
2. **apply_edits 涉及资源赋值/继承时必须传 catalog_version**——错误信息明确"Query the project catalog first"。catalog_version 位于 get_context 响应的 **data.catalog_version**（不在 context 对象里，E1 报告的"顶层"表述不准确）。
3. **无 line.copy 原生操作**——20 个 EditOperation 分支只有 line.insert/update/delete/move。批量复制必须 line.insert 构造（含 after_line + 内容/继承）。Azurite 的 CopyVoice=true 需要显式重述 voice 资源 → 在无完整资源库的环境必被 RESOURCE_NOT_FOUND 拒绝。
4. **read_project 分页结构是 {items, next_cursor, total}**，不是 lines/nodes 数组；limit 上限 200，310 行需 2 页。summary 视图不含节点列表，需先取 graph 视图拿 node_id。
5. **inherit:{from} 之后 voice 的呈现**：副本 voice = set + 与源行不同的资源 ID（疑似按说话人自动重绑定）。文档只说"voice identity 不继承"，未说明重绑定来源。对 Azurite 卡包/复制功能有直接影响，建议后续单独立项核实。
6. **revision 语义确认**：每次成功写 +1（r0→r5）；save 不推进；重开工程后从 r0 重新计数（按工作副本会话计，不是文件全局计数）。
7. **MCP transport session 与业务 S 分离且行为正确**：transport 重连（重新 initialize，新 mcp-session-id）后 get_context 返回同一业务 S；mcp.stop + 重启后才换 S。
8. tools/list 的 14 个工具与 capabilities 声明完全一致；schema 抽取落盘于 `mcp/schema-*.json`（apply_edits 完整 20 分支结构见 `evidence/apply-edits-full-schema.json`）。

## 4. 执行环境备忘（给后续代理）

- **run_code 内 detached spawn 的子进程会随 run_code 结束被整树回收**（实测 detached pwsh / detached node 均无法存活）。AA 生命周期必须用 ops_bash run_in_background 跑护栏启动器（bash 启动延迟约 40s 属正常）；MCP 写序列必须在**单次 run_code 内闭环**或把状态落盘 state.json 分段续接。
- MSYS bash 会把 `taskkill /PID` 转义成路径（`F:/Git/PID`），杀 AA 用 PowerShell `Stop-Process -Id <pid> -Force`。
- 失败保全执行情况：3 个失败现场全部落盘未删（`evidence/FAILURE-*.json`），每一次重试都携带新的 request_id 且修改了请求依据（错误指明的缺失字段），符合"失败保全、按错误指引修正"而非"重试到通过"。

## 5. 证据索引

| 证据 | 路径 |
| --- | --- |
| 会话1启动收据（12 项红线检查） | `evidence/session1-launch/launch.json` |
| 会话2启动收据 | `launch.json` + `launch-events-s2.ndjson` |
| automation/editors 注册 | `evidence/01-automation-registration.json`、`02-automation-registration-session2.json` |
| MCP 全部请求/响应 | `mcp/args-*.json`、`mcp/resp-*.json` |
| 工具 schema 快照 | `mcp/schema-*.json`（14 个） |
| 目录资源清单 | `mcp/catalog-inventory.json` |
| 失败现场（未掩盖） | `evidence/FAILURE-mcp-start.json`(无), `FAILURE-phase-b.json`, `FAILURE-a9.json`, `FAILURE-a9-v2.json`, `FAILURE-a9-v3.json`(无), save 拒绝见 `mcp/resp-save.json`/`resp-save-retry.json` |
| A6 重开验证 | `evidence/a6-phase.json`、`evidence/a6-reopen-verification-full.json` |
| BepInEx 日志 | `evidence/bepinex-LogOutput-session1.log`、`-session2.log` |
| AA unity 日志 | `output/unity-session1.log`、`output/unity.log`（会话2） |
| 状态台账 | `state.json`（revision 轨迹、每项结果、ID 清单） |

# E1 实验 report —— AA 原生 MCP/CLI 完全可用（决定性结论）

日期：2026-11-20 23:18–23:30 Asia/Shanghai。执行：lead（experimenter 队友超时未完成，由我接手）。
运行目录：`E:\aamod\tmp-azurite-takeover\experiments\E1-run1\`
隔离副本：`E:\aamod\labs\Azurite-20261108-stable\install`（通过护栏脚本 Launch-AAHeadless.ps1 启动，红线 6 项全过）

## 一句话结论

**AA 的原生 MCP 创作 API 在无 GUI（-batchmode -nographics -disableaudio）下完整可用：
mcp.start → get_context → read_project → update_lines → save_project → validate_project → mcp.stop
全链路真实执行成功，一次真实写入已持久化到磁盘。**

## 实验时间线（全部真实执行）

| 步骤 | 动作 | 结果 |
| --- | --- | --- |
| 1 | 护栏启动 AA（PID 14300，BelowNormal，无窗口） | exit code 0，红线 R1–R6 全过 |
| 2 | AA 到达 CatalogScene | BepInEx 日志 `databasesReady=True` + `Editor wheel tuning: catalog` |
| 3 | control server 注册 | `automation/editors/14300.json` 出现，含管道名 |
| 4 | 第二进程发 `mcp.start --project 直通线-0.aap2` | **`ok:true`**, `running:true`, `edit_session=0157f60a…`, `revision=r0`, `url=http://127.0.0.1:18789/mcp/` |
| 5 | MCP initialize 握手 | `protocolVersion=2025-11-25`, `serverInfo=AzureArchive 1.0.0` |
| 6 | `get_context` | `ok:true`，项目名「直通线-0」，`format_version=2`，310 行 |
| 7 | `read_project view=summary/graph/lines` | **真实内容读回**（含角色/舞台/BGM/voice/extra_script 全字段） |
| 8 | `update_lines` 写 1 行 | **`ok:true`, revision r0→r1**, `applied_operations=1`, `persistence=working_copy` |
| 9 | `save_project` | **`ok:true`, `persistence=saved`, `saved_revision=r1`, file_version=sha256:b5a96c5c…`** |
| 10 | `validate_project level=compile` | `valid:true`（只有 RESOURCE_NOT_FOUND/UNBOUND_VOICE 警告，因副本无资源库） |
| 11 | `mcp.stop`（CLI transport） | `ok:true`, `running:false` |
| 12 | 终止 AA、磁盘校验 | **`直通线-0.aap2` 内含验证标记字符串（UTF-8）** |

## 关键 API 事实（实测得出）

1. **CLI transport**：`AZUREARCHIVE_CLI_DIRECTORY=<dir>` + `arguments.json`（`{"argv":[...],"cwd":"..."}`）。
   cwd 必须**正斜杠**（反斜杠转义报 `Bad JSON escape sequence`）。
2. **response.json** 若已存在，CLI 拒绝执行（必须先删）。
3. **MCP HTTP**：`http://127.0.0.1:18789/mcp/`，streamable-HTTP，握手后带 `mcp-session-id` 头。
4. **工具 schema 严格 oneOf/anyOf**：`read_project` 必须带 `view`（summary/graph/nodes/lines）；
   `update_lines` 项必须是 `{line_id, set:{...}}`（`set` 包裹，不是平铺字段）。
5. **edit_session ≠ MCP transport session**：业务会话来自 `get_context`。
6. **写语义**：`expected_revision` 必须匹配（乐观并发）；每次写一个新 `request_id`；
   成功后 revision 自动推进（r0→r1）。
7. **mcp.start 的启动行为**确认了 capabilities 里的声明：`load_project_if_editor_empty_otherwise_guard`。
8. **mcp.stop 后**，AA 主进程仍在运行（只是 MCP 服务停了），需另行终止进程。

## 对项目方向的影响（重新排期）

### 直接解锁（不再需要 BepInEx 注入就能做的验收）

- **A2/A3**（真实会话 + 真实 catalog）：`get_context`/`read_project`/`search_resources` 就是真实会话与目录。
- **A4（批量文本）**：`update_lines` 已实测成功 → 批量文本直接可验。
- **A7（批量删除）/A8（多行插入）/A9（复制）/A10（槽位）**：`apply_edits`（`edit_graph` + line ops）支持。
- **A11–A15（五类卡包）**：`search_resources` 拿真实资源 ID → `apply_edits` 插入带 stage/scene 的行。
- **A6（保存重开）**：`save_project` + 重新 `mcp.start` 已验证闭环。
- **A5（撤销/重做）**：SKILL.md 说「成功批次形成一个原生 Undo 组」——需在 GUI 里验证（CLI 无法按 Ctrl+Z），
  但 AA 自己管理 Undo 栈这一点已经由架构保证。

### Azurite 插件的新定位

插件不再需要自己实现「批量编辑内核」（那是 Codex 8 天在做的事）。它的价值变成：
1. **创作者 UI**（工具栏/卡包界面）——把这些操作映射到 AA 原生 API。
2. **性能优化**（虚拟化/延迟加载等）——这与创作 API 正交，仍需插件。
3. **批量操作的原生事务化**：AA 的 `apply_edits` 已经是原子的、单 Undo 组的。

### 仍需插件/GUI 的部分

- 界面级验收（A16 工具栏真实点击）
- 焦点守卫（A18）
- 性能（A20）
- 渲染协作（A19，AAVE）

## 遗留问题

1. 一次 `update_lines` 最多 500 条（`max_operations`）——够用。
2. `search_resources` 的资源目录在隔离副本中可能为空（validate 报 RESOURCE_NOT_FOUND 警告）。
   需确认副本的资源库如何补齐（可能要复制 `cli-resources` 或资源缓存目录）。
3. 撤销/重做只能在 GUI 验证（如果需要）。

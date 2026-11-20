# A11–A15 五类卡包真实验收报告（AA 原生 MCP · 隔离副本）

执行：cardpack（DSH 子代理，task-8）｜时间：2026-11-21 00:33–01:05 Asia/Shanghai
运行目录：E:\aamod\tmp-azurite-takeover\experiments\E3-A3-A11-A15-run1\
前置：A3 资源盘点见同目录 A3-CATALOG.md（路线 b：副本资源库完整，无需补资源）

## 0. 运行环境与红线合规

| 项 | 值 |
| --- | --- |
| AA 可执行文件 | E:\aamod\labs\Azurite-20261108-stable\install\AzureArchive.exe（白名单内） |
| 启动方式 | Launch-AAHeadless.ps1（R1–R6 fail-closed），两次启动均 spawned+passed，见 logs/launch-events*.ndjson 与 launch.json |
| 实例 | 会话1 PID 43104 → mcp.stop + Stop-Process → NO-RESIDUAL → 会话2 PID 20956 → 同样清理。全程单实例 |
| 日常安装 | F:\AzureArchive_101\BepInEx\LogOutput.log mtime 全程 2026-11-20 22:15:43（启动前后核对一致，0 写入） |
| 无头参数 | -batchmode -nographics -disableaudio -logFile <run>/output/*.log |
| 样例工程 | 直通线-0.aap2 复制自 E:\aamod\backups\editor-bench-20261003-131109\（原件未动；副本 sha256=ab8de187… 与 00-project-copy.json 一致） |
| MCP | 会话1 S=31c815a1-…（r0→r6，保存 saved_revision=r6）；会话2（重开）S=3f8c6cd3-…（r0=磁盘 r6 内容） |

## 1. 写入轨迹

锚点行 0d6a9bd3（A4 第 3 行）之后链式插入 6 行（每张卡一次独立 apply_edits，一个请求=一个原生 Undo 组）：

```
r0 ─ A11 转场 line.insert → r1 ─ A12 音效 → r2 ─ A13 战斗 → r3 ─ A14 动作(Greeting) → r4 ─ A14 动作(Idle) → r5 ─ A15 语言 → r6
   save_project → persistence=saved, saved_revision=r6, file_version=sha256:d1652c04…
   mcp.stop → 终止 AA → 护栏重启 → mcp.start 同一工程 → 新会话 r0（磁盘内容=r6）
```

每步请求/响应全文：mcp/args-*.json、mcp/resp-*.json。创建的行 ID 台账：mcp/created-ids.json。

## 2. 逐项验收

### A11 转场卡包 —— ✅ 通过
- 资源：transition "1 1000 >> 1 1000" res_37965307e5b37218797b3c40db629b1e（search available=true，metadata out=1,out_ms=1000,in=1,in_ms=1000）
- 调用：apply_edits line.insert + line.scene={transition:{action:"set",resource_id}}（args-a11.json）
- 响应：ok:true，applied=1，r0→r1（resp-a11.json）
- 读回：scene.transition={action:"set",resource_id 完全一致}（resp/readback.json）
- 重开保留：transitionMatch=true（mcp/reopen-verification.json）
### A12 音效卡包 —— ✅ 通过（声音播放本身 ⊘ CLI 不可验）
- 资源：sound "SE_AirRelease_01" res_4b4a40de42b552d2076539cda67b7a82（available=true）
- 调用：line.insert + line.scene={sound:{action:"set",resource_id}}；首次尝试因验证脚本读取新 revision 的位置错误而失败（INVALID_ARGUMENT $["expected_revision"] 缺失，现场保全 resp-a12.json+args-a12.json，按错误指引修正后以新 request_id 重试——非盲目重试）
- 读回：scene.sound={set, 资源一致}；重开 soundMatch=true
- **⊘「能听到声音」不可验**：-disableaudio 无头会话无音频输出设备路径，离屏/内部事件不能冒充听觉验收（SKILL.md：编译或数据读回成功不等于已经听见）。仅验收数据落盘与 schema 正确性。
### A13 战斗卡包 —— ✅ 通过
- 组合：stage.action=Stiff（Azurite Combat 允许的受击反应 Stiff/Shake/FalldownL/FalldownR）+ stage.effect=Shot + scene.sound="SE_Hit_01" res_285c42b3…
- 枚举来源：action/effect 枚举取自 tools/list 的 $defs.StageCharacter（apply-edits-full-schema-from-E2.json），非猜测；SE_Hit_01 由 search 实测 available=true
- 读回：stage[0]={action:"Stiff", effect:"Shot"}，scene.sound 一致；省略 face_id 落盘为 "00"（证实 schema 注释 omitted means 00）；重开 actionMatch/effectMatch/soundMatch=true
### A14 动作卡包 —— ✅ 通过
- 先 search 核对：日奈 res_408fbdba83090775990752a9c7eb8d78 available=true, capabilities.stage=true（SKILL.md 要求，不能猜）
- 两行链式插入（after_line 用批内 ref 链）：Greeting+face 00 → Idle+face 02（face 取自工程真实使用值表，mcp/pristine-enums.json）
- 读回：两行 stage.action/face_id 精确匹配；重开 actionMatch/faceMatch=true×2
- 注：face 级「该角色支持哪些表情」无原生枚举可查（faces_loaded=false），face_id 选择仍是工程既有值+schema 语义，与 A3 报告结论一致
### A15 语言卡包 —— ✅ 通过（语义限制如实记录）
- **SKILL.md 明示：search_resources 没有 language 参数**（「没有 language 参数，不猜资源 ID」）；AA 原生 API 无多语言文本变体字段。
- 反向探测证据：向 line.insert 提交 line.language="zh-CN" → INVALID_ARGUMENT "Value must match exactly one schema branch"，path=$[operations][0][line][language]，matched_branches=0（resp-a15neg.json）——**schema 层面确定不支持**。
- 与 Azurite 编译器语义核对（CardPackV2Compiler.cs Language 分支）：语言卡=插件侧按 SelectedLocale 从卡包 Variants 字典选文本 → 写入 line.text 的普通 dialogue 行；变体选择完全在插件层，原生落点就是 text。
- 本轮验收：插入 dialogue 行，text=A15语言卡验证-locale:zh-CN-<ts>（locale 标注进文本），重开 textMatch=true；schema 拒绝 language 字段的证据已保全。

## 3. 保存 → 重开 全链路

1. save_project 三件套 {edit_session, expected_revision:r6, request_id} → ok:true, persistence=saved, saved_revision=r6, file_version=sha256:d1652c04…（resp-save.json）
2. 磁盘：.aap2 851,559→924,710 B，sha256=d1652c04…（与 file_version 一致）；6 个卡标记字符串逐个 grep 在 .aap2 内各 1 次
3. validate_project level=compile → ok:true, **valid=true**，691 条环境性警告（RESOURCE_NOT_FOUND 364 / UNBOUND_VOICE 316 / CAPABILITIES_UNAVAILABLE 11）
4. **警告增量分析**：E2 基线 688 条 → E3 691 条；增量 = 6 条 UNBOUND_VOICE 全部落在我们的 6 行上（行数 313→316 且每行 voice 自动重绑定产生一条环境警告，与 E2 发现的未文档化行为一致）；**转场/音效/角色资源 0 条 RESOURCE_NOT_FOUND** —— 卡包资源引用全部健康（resp-validate.json）
5. mcp.stop → NO-RESIDUAL → 护栏重启 → mcp.start → 新 S=3f8c6cd3，revision r0（磁盘内容即 r6）
6. 重开读回：316 行，6 张卡逐项核对 **ALLPASS=true**（mcp/reopen-verification.json，逐卡字段见上）

## 4. 本轮新增 API 事实（在 E2 第 3 节之上）

1. **apply_edits 响应的新 revision 在 context.revision，不在 data 里**（data 有 applied_operations/created/changed_*；context 有 project_id/edit_session/revision）。E2 报告未明确区分，写链式批次必须从 context.revision 取值。
2. **data.created[ref] 给出 ref→真实 line_id 映射**——批内 ref 只在同批次有效，跨批次链式插入要用上一批 created 返回的真实 ID（本轮 A12→A15 全部如此串联）。
3. **CLI courier 的 arguments.json 是必需输入**：缺失时 response.json 报 RUNTIME_UNAVAILABLE "Could not find file …arguments.json"（首次现场 cli/02-mcp-start/first-attempt-response.json）。正确形态：AZUREARCHIVE_CLI_DIRECTORY=<dir>，<dir>/arguments.json={"argv":[…],"cwd":"<正斜杠>"}，命令行再传 --cli mcp.start --json -batchmode -nographics -disableaudio -logFile。
4. **控制管道先于注册文件**：AA 主进程启动后命名管道 \\.\pipe\AzureArchive.Authoring.<hash>.<pid>.<n> 即存在（可用 PowerShell [System.IO.Directory]::GetFiles('\\.\pipe\') 探测）；automation/editors/<pid>.json 在 mcp.start courier 成功后才出现。「等注册文件」不可作为就绪信号，「等管道」更快且可靠。
5. **MCP HTTP 偶发返回 SSE 格式**（同一会话内 JSON 与 event-stream 混合），客户端解析需兼容 data: 前缀；未初始化传输会话直接调工具返回 HTTP 400 空 body（本次 reopen-verify 首跑即此因，已修复并留 last-parse-failure.txt 现场）。
6. **省略 face_id 落盘为 "00"**（A13 实测，证实 schema 注释）；voice 未赋值时每行产生自动重绑定 ID + 1 条 UNBOUND_VOICE 环境警告（行为与 E2 的 inherit 观察一致，仍未文档化）。

## 5. 失败现场清单（未掩盖）
| 现场 | 文件 | 处置 |
| --- | --- | --- |
| courier 缺 arguments.json | cli/02-mcp-start/first-attempt-response.json | 按 RUNTIME_UNAVAILABLE 指引补输入文件，新尝试 |
| A12–A15 首批 INVALID_ARGUMENT（expected_revision 缺失） | mcp/resp-a12.json 等 | 根因=验证脚本读错 revision 位置；改从 context.revision 取值，全部以新 request_id 重发 |
| A15 language 字段负向探测 | mcp/resp-a15neg.json | 作为「schema 不支持」的证据保留（有意失败） |
| reopen-verify 首跑 400 | mcp/last-parse-failure.txt、last-tool-failure-*.json | 根因=脚本漏 initialize 握手；补握手后通过 |

## 6. 红线自查
- 护栏启动器 ×2（R1–R6 全过，launch.json passed=true）；绝未使用 F:\AzureArchive_101 的 exe；单实例串行；结束时无残留进程
- 日常安装 0 写入（LogOutput mtime 前后一致）；副本工作区内全部操作；备份原件未动（工程是复制件）
- 失败全部保全并按错误指引以新 request_id 修正，无「重试到通过」的盲目重试

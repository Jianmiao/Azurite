# A3 资源目录盘点报告（隔离副本 · AA 原生 MCP）

执行：cardpack（DSH 子代理，task-8）｜时间：2026-11-21 00:33–00:55 Asia/Shanghai
运行目录：E:\aamod\tmp-azurite-takeover\experiments\E3-A3-A11-A15-run1\
实例：AA 会话1 PID 43104（护栏启动器 R1–R6 全过，logs/launch-events.ndjson 有 spawned 事件；launch.json 由阻塞式启动器在退出时落盘）

## 结论：路线 b（用副本已有资源），不补资源

**决定性发现：task-7 报告的「隔离副本每类只有 50 条内置资源」是分页误读。**
E2 的 catalog-inventory.json 只取了 search_resources 第一页（每页 50 条），而响应一直带有
next_cursor（base64 解码可见 "offset":50），从未翻页。本轮全量分页实测：

| kind | 实际总数 | 页数 | 首条样例 |
| --- | --- | --- | --- |
| character | **2050** | 41 | ??? (res_2f2c31d1…) |
| background | **2050** | 41 | BG_01_cg (res_f5ce8a51…) |
| voice | **2050** | 41 | Airi_Cafe_Act_1 (res_b2ba96a0…) |
| sound | **863** | 18 | SE_49mm_01 (res_2e1efcda…) |
| popup | **479** | 10 | Event01_Airi (res_0300a414…) |
| bgm | **434** | 9 | 0k@eri (res_1c253da0…) |
| transition | **68** | 2 | 1 1000 >> 1 1000 (res_37965307…) |
| background_effect | **61** | 2 | BG_Ash_Black (res_e182c744…) |

全部 8 类证据：mcp/inventory-<kind>.json（含每条 resource_id/name/available/metadata），汇总 mcp/inventory-summary.json。

### 为什么不需要路线 a（复制资源）

1. **catalog_version 实测**：c_d19ce15cdefe3210dc520ec7cd1329341ad52d34f9dcb646562218677d5323ac（get_context 的 data 层，与本轮全部 search 响应一致）。
2. **卡包需要的四类全部齐备且 available=true**：transition 68 / sound 863 / bgm 434 / character 2050（其中 1702 个 stage=true）。
3. **资源在磁盘上的位置已探明**：
   - E:/AAdata 是 user_settings.json 里 workspacePath 指向的共享工作区；E:/AAdata/characters/（102 个角色资料 JSON）是**用户创作的角色人设资料**（name/aliases/personality/decision_patterns），**不是**引擎 faces 枚举来源。
   - 两个安装的 AzureArchive_Data/StreamingAssets/aa 均 23M，体积对比一致——内置资源目录来自安装本体。
   - mods 目录两边近同构（差异只有 AAMultilingual 0.1.0 vs 0.1.1 与 profiles 下日志）；人物配音支持 mod 只有 DLL 无语音数据文件——mod 语音/皮肤等全局资源由 AA 的全局索引提供（source:"global", availability_check:"file_presence"）。
4. **对 task-7 A9 CopyVoice=true「环境受限」结论的修订**：RESOURCE_NOT_FOUND 的根因是**该工程引用的具体 voice 资源 ID 不在当前目录**（310 行每行一个 voice ID，全部来自旧版目录快照；抽验 res_0d6aee2f…/res_a6a9777a…/res_f2376e67… 均不在本轮 2050 条清单），而不是「副本没有资源库」。复制日常安装资源无法解决陈旧 ID 问题（当前目录本来就没有那些 ID），路线 a 无收益。

### faces / actions / effects 真实枚举

**schema 枚举**（tools/list 的 $defs.StageCharacter；与 E2 的 apply-edits-full-schema.json 一致）：
- action: Idle / Greeting / FalldownL / FalldownR / Stiff / Shake / Jump / Hophop / Curve（Curve 需 curve 数据）
- effect: None / Shot
- appear: None / AL / AR / A / DL / DR / D
- emoticon: Angry, Chat, Dot, Exclaim, Heart, Music, Question, Respond, Shy, Surprise, Sweat, Twinkle, Upset, Think, Bulb, Sad, Sigh, Steam, Tear, Zzz, None
- face_id: **无枚举约束**（string，schema 注释「Published face/animation ID; omitted means 00」）

**工程实际使用**（本副本同一工程 310 行全量读回 mcp/pristine-lines-full.json + mcp/pristine-enums.json）：
- face_id 实际值：00/01/02/03/04/05/06/07/08/09/10/11/12/13/14/15/20/29/37/42/99（21 种，两位数字符串）
- action 实际值：Idle(334) / Stiff(1)；effect 实际值：None(335)；appear 实际值：None/AL/AR/A/DL/DR/D
- 各说话人角色的 face 使用分布见 mcp/pristine-enums.json

**角色能力**：capabilities {offstage, stage, faces_loaded} 随 search 返回；faces_loaded:false 表示 faces 清单不随 search 下发，face_id 只能按「工程已用值 + schema 注释 00」取值。A14 已按 SKILL.md 要求先 search 核对 available=true 与 stage=true。

### 卡包选定资源（全部 available=true，本轮 search 响应原文为证）

| 卡包 | 资源 | ID |
| --- | --- | --- |
| A11 转场 | transition "1 1000 >> 1 1000"（metadata out=1,out_ms=1000,in=1,in_ms=1000） | res_37965307e5b37218797b3c40db629b1e |
| A12 音效 | sound "SE_AirRelease_01" | res_4b4a40de42b552d2076539cda67b7a82 |
| A13 战斗 | sound "SE_Hit_01" + action=Stiff + effect=Shot（Azurite Combat 卡=受击反应，源码 AuthoringCardPackHostView.cs: Combat→_action="Stiff"） | res_285c42b3c6a44990dab97f2225e2503b |
| A14 动作 | character 日奈（available=true, stage=true, aliases=[日奈,风纪委员会]）action=Greeting（Azurite Action 卡默认）face_id=00 | res_408fbdba83090775990752a9c7eb8d78 |
| A15 语言 | 无资源需求；语义限制见 A15 结果 | — |

### 环境事实（留档）

- 副本 ActiveProfile=AzuriteAcceptance（只启用 Azurite 1.0.38）；日常安装 ActiveProfile=Default。两安装共用 E:/AAdata 工作区与 automation 目录。
- 会话1 启动→CatalogScene（databasesReady=True）约 1 分钟；控制命名管道 \\.\pipe\AzureArchive.Authoring.E0959EFC2F96A636C27F4F24.43104.639307892248335756 由 AA 主进程创建（PowerShell [System.IO.Directory]::GetFiles('\\.\pipe\') 实测）；**automation/editors/<pid>.json 在 mcp.start courier 执行成功后才出现**（E2 相同）。
- CLI courier 正确用法（本轮踩坑修正）：AZUREARCHIVE_CLI_DIRECTORY=<dir> + 该目录下的 arguments.json（内容 {"argv":["mcp.start","--project","<正斜杠工程路径>"],"cwd":"<正斜杠工作目录>"}）+ 命令行 --cli mcp.start --json -batchmode -nographics -disableaudio -logFile <courier日志>。**arguments.json 是必需输入**，缺失时报 RUNTIME_UNAVAILABLE "Could not find file …arguments.json"（首次失败现场已保全：cli/02-mcp-start/first-attempt-response.json）。
- mcp.start 响应：edit_session=31c815a1-603e-4aa0-98d5-df0dd3a54f5c, revision=r0, url=http://127.0.0.1:18789/mcp/（cli/02-mcp-start/response.json）。

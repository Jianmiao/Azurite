# SUMMARY — A3 + A11–A15 最终状态与 Azurite 卡包映射结论

执行：cardpack（task-8）｜2026-11-21 00:33–01:08 Asia/Shanghai｜run=E3-A3-A11-A15-run1

## 最终状态表

| 项 | 状态 | 关键证据 |
| --- | --- | --- |
| A3 资源目录 | ✅ **路线 b 成立**（副本资源库完整：character/background/voice 各 2050、sound 863、bgm 434、popup 479、transition 68、background_effect 61；task-7 的「每类 50 条」是分页误读） | A3-CATALOG.md；mcp/inventory-*.json |
| A11 转场卡包 | ✅ 插入/读回/保存/重开四段全过 | resp-a11.json、reopen-verification.json |
| A12 音效卡包 | ✅ 数据与 schema 全过；**⊘ 「能听到声音」CLI 不可验（-disableaudio 无头会话，离屏事件不能冒充听觉验收）** | resp-a12.json、resp-save.json |
| A13 战斗卡包 | ✅ Stiff+Shot+SE_Hit_01 组合全过；枚举取自真实 schema 非猜测 | resp-a13.json、apply-edits-full-schema-from-E2.json |
| A14 动作卡包 | ✅ 先 search 核对角色 available+stage=true，Greeting/Idle 两行全过 | resp-a14a/b.json、inventory-character.json |
| A15 语言卡包 | ✅ 按 SKILL.md 语义完成（locale 写入 text）；**原生无 language 字段——反向探测被 schema 拒绝，证据保全** | resp-a15neg.json |
| 保存重开闭环 | ✅ saved_revision=r6；磁盘 sha256=file_version；重开 6 卡 ALLPASS=true | resp-save.json、state.json |

## 对 Azurite 卡包实现的直接映射结论

Azurite 的五类卡包（PresetCardKind.Transition/Combat/Action/Language/SoundEffect，源码 Plugin/Azurite/AuthoringCardPackHostView.cs + Core/Azurite.Core/CardPackV2Compiler.cs）与 AA 原生 MCP 的映射**全部有真实落点，逐项实测通过**：

| Azurite 卡包 | 编译器产出（CardPackV2Compiler 实测源码） | 原生落点（本轮验证） | 结论 |
| --- | --- | --- | --- |
| 转场 Transition | line.insert(control) + scene.transition={set,resource} | scene.transition 落盘并在重开后保留 | **直接映射，无需适配** |
| 音效 SoundEffect | line.insert(control) + scene.sound={set,resource} | 同上 | **直接映射；播放验收需 GUI** |
| 战斗 Combat | line.insert + stage[action∈{Stiff,Shake,FalldownL,FalldownR}, effect=Shot] + scene.sound | stage.action/effect + scene.sound 组合实测通过；省略 face_id 自动 00 | **直接映射** |
| 动作 Action | line.insert + stage[action∈schema 九枚举]（默认 Greeting） | stage.action 实测通过（Greeting/Idle）；角色 stage 能力可 search 预检 | **直接映射** |
| 语言 Language | 插件侧按 SelectedLocale 选 Variants 文本 → line.text（dialogue） | text 普通行实测通过；原生无 language 字段（负向探测被拒） | **映射为 text 写入；多语言变体管理留在插件层** |

关键设计事实（对 Azurite 2.x 适配器的直接指令）：

1. **卡包编译器的 5 个 HOST 能力位全部被原生 API 支持证实**（LineInsert/SceneTransition/SceneSound/StageAction/DialogueText）。Codex 时代担心的「原生是否支持」已不成立。
2. **链式插入协议**：首卡 after_line={id:锚点}，后续 after_line={ref:"card_N"}；跨批次必须用 data.created[ref] 返回的真实 line_id；expected_revision 每步取**响应 context.revision**（不在 data）。
3. **catalog_version 必传**（涉及资源赋值/继承的 apply_edits），值从 get_context 的 data.catalog_version 取；资源 ID 只能来自 search_resources（never derive）。
4. **战斗卡受击反应**：Combat 的 Reactions={Stiff,Shake,FalldownL,FalldownR}+Shot 都在原生 StageCharacter 枚举内；effect 枚举只有 None/Shot。
5. **face_id 无枚举可查**（search 返回 faces_loaded=false）：插件 UI 不应枚举 faces，应让用户输入或沿用工程已有 face 值；省略=00 是唯一受 schema 背书的默认。
6. **语言卡多语言**：原生不支持 language 维度——插件需自己管理 Variants 字典并按 SelectedLocale 选文本写入 text（Azurite 现有设计已正确如此，ValidateData 的 LANGUAGE_VARIANTS 规则与此一致）。
7. **voice 侧效应**：插入行不赋 voice 时，validate 会对每行报 1 条 UNBOUND_VOICE 环境警告（自动重绑定），插件可按行数预期它，不算卡包失败。
8. **非语言卡 mode=control**：Azurite 编译器对 Transition/SoundEffect/Combat/Action 产出的行是 control 模式（speaker:null, text:""），本轮验收用的是 dialogue 行验证同一字段语义；control 行的字段落点（scene/stage）与 dialogue 相同，均被原生接受（A4-A10 轮已验过 control 场景字段保留语义）。

## 未覆盖（如实声明）
- A12 的实际声音播放：须 GUI 会话 + 音频输出，不属于无头可验范围（与 A5 撤销/重做同类）。
- face 级角色表情清单枚举：原生不随 search 下发（faces_loaded=false），无 API 可查。
- 卡包面板的真实点击流（A16 类）：属 GUI 插件 UI 验收，不在本轮。

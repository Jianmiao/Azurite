# Azurite 插件 × AA 原生 API：架构澄清与 1.0.41 适配评估

日期：2026-11-21 03:10。作者：lead。性质：只读源码分析，无代码改动。

## 1. 关键澄清：两条 API 是互补的，不是竞争的

深入读创作分支源码（AuthoringCardPackHost.cs）后确认：

- **插件内路径（已有）**：插件在 AA 进程内通过 IL2CPP 反射调用 `AuthoringEditorSession.Apply(operations, catalogVersion)`（AuthoringCardPackHost.cs:211）。
  这走的是 **AA 进程内原生会话**——和 MCP 是同一套底层操作，但**在进程内直接调用**。
- **外部路径（本轮 E1–E4 验证的）**：CLI transport / MCP HTTP，供 **AA 进程外的工具**（如 AI 助手、CI）操作工程。

**所以 task-8 的映射结论要修正一处表述**：插件不需要「改造成调用 AA 原生 API」——它的 `Session.Apply` 调用**已经是 AA 原生 API 的进程内形态**。E1–E4 验证的外部 API 证明了：
1. 这些操作语义（line.insert/update/delete、EditOperation 500 上限、原子批次、原生 Undo 组）是 AA 官方支持且行为可复验的
2. 插件的 20 个 EditOperation 分支设计（CardPackV2Compiler）与 AA 原生 schema 对齐
3. 之前「所有写入接口返回失败」的根因是**从未进入编辑器会话**——现在会话路径已打通（A2/A3）

## 2. 阻塞插件真实验收的真正前置

AuthoringCardPackHost.ReadState()（:307-330）要求：
- `StudioCommon.instance` 活跃 + `AuthoringEditorSession.Current` 非 disposed（**需要编辑器界面打开 + 工程加载**）
- `OperationManager` 空闲、无 save/compile 进行中
- 完整的 fingerprint/revision 一致性校验

这就是为什么 8 天来批量编辑无法验收——不是事务代码错，而是**宿主状态从未满足**（编辑器没打开）。E1 已证明 MCP `mcp.start --project` 能让 AA 加载工程（`ScenarioEditorScene`）。

## 3. 1.0.41 真实验收的可行路径（无需写新代码）

**A2' 实验：MCP 加载工程后，验证插件内编辑器会话是否激活**

1. 护栏启动 AA（无 GUI 不可行——编辑器 UI 需要 GUI 模式？待验）
   - 或：`-batchmode`（离屏 GPU）+ `mcp.start --project`，看 `AuthoringEditorSession.Current` 是否非空（通过 BepInEx 日志的 editor-work 行 `session=<id>` 判断）
2. 若会话激活：插件的批量编辑 UI（Alt+1–5 / 工具栏）在无 GUI 下无法点击，但可通过 **MCP 触发等价操作 + 插件日志观察** 验证事务边界
3. 关键验收点：`editor-work` 日志的 `session=` 与 `revision=` 字段在 MCP 写入后的变化

**需要 GUI 的部分**（如实标注）：工具栏点击、框选、快捷键（A16/A18）。

## 4. 对发布决策的影响

- v1.0.40（稳定基线）与批量编辑/卡包是**独立的交付物**——批量功能在创作分支，需要独立的候选构建
- 1.0.41 的正确路径：把创作分支代码（已通过 33 组可移植回归 + 204 项 bridge 检查）接入 MCP 加载工程的会话环境做真实验收，然后合入构建
- 发布 A（立即发 1.0.40 基线）与 1.0.41 开发互不阻塞

## 5. 修正 task-12 描述

原 task-12 说「把内核从自己实现事务改为调用 AA 原生 API」——**不准确**。修正为：
「把创作分支的批量/卡包代码接入 MCP 加载工程后的真实会话环境，完成 A4'–A10' 真实验收，然后构建 1.0.41 候选包」。
事务代码本身已经正确对齐 AA 原生语义（204 项 bridge 检查 + 本轮 E 系列外部验证双重佐证）。
# A20 判定书（逐项）

判定规则（用户批准原计划）：收益未超重复测量波动 → 保持默认；其它关键指标可重复退化 >5% → 不默认启用；数据不足 → 如实标注。

## 1. Adaptive Rendering（[Adaptive Rendering] Enabled，当前默认 true）

**判定：保持默认启用。**

依据（n=3×2 状态，CPU% 稳态中位数）：
- 关闭后 CPU 一致上升：AR-off 组 11.4/11.0 vs AR-on 组 10.3/9.8（方向在冷热一致，幅度 ~0.7–1.3pp）。
- 关闭带来的唯一"收益"候选（内存 -46MB / 启动 lag）均小于组内极差。
- 结论：开启有可观测的 CPU 节省方向，关闭无可重复收益 → **保持默认 true**。
- 保留意见：AR-off-cold 组内 CPU 极差 5.9pp（离群轮）大于组间差，按严格标准该收益"未明确超过波动"；但两状态 CPU 区间零重叠（AR-off 全部 ≥11.0，AR-on 全部 ≤10.4）支持方向性结论。

## 2. CPU Power Saving（[CPU Power Saving] Enabled，当前默认 true）

**判定：保持默认启用。**

依据（最强信号）：
- 关闭后 CPU 零重叠上升：CPS-off 6 轮全部 ≥10.9（中位 11.2/11.6），CPS-on 6 轮全部 ≤10.4（中位 10.3/9.8）。相对差 ~12–14%。
- CPS-off 时 gt100/maxGap 无改善（3/3 vs 2/6 合计），samples 反而略高（=未降档的 player-loop 满速运转）。
- 关闭无任何一项补偿性收益 → **保持默认 true**（关闭=纯浪费 CPU）。
- 附注：本轮无头空载场景下 CPS 的 idle 降档逻辑未实际触发（target=-1 恒定），其真实收益场景是"confirmed quiet editor"下的前台闲置，需要 GUI 焦点态才能完整触发——本轮测得的是"关闭时代价确定存在、开启时无代价"。

## 3. 深层性能补丁（虚拟化 VirtualizeDialogueList / 渐进加载 ProgressiveEditorLoading / 复用行/文本布局 / 延迟选择器等）

**判定：数据不足（不可测）——不在本副本。**

这些补丁实现在创作分支（codex/azurite-authoring-20261108，e8dddb9 及其后 WIP），本副本安装的是 1.0.38 稳定基线，cfg 项为默认 false 且 DLL 无对应实现。按要求不把创作分支 DLL 塞进副本凑数。已列入后续候选构建（1.0.41+）的 A20 复验清单。

## 4. ProfileEditor

**判定：不适用对照（只读诊断）。** 关闭仅删除验收数据来源，无性能意义；acceptance profile 保持 true。

## 5. 其他cfg可调项（创作类/实验类，本轮未测）

DialogueBoxSelection（创作功能）、WheelTuning（输入手感）、Preview 系列实验项（默认 false 或已默认优化的 IdleEditorBloomReduction/CacheStaticPreview 等）——均非性能验收对象，维持现状。

## 6. 建议的默认配置

**维持现状不变**：
- [Adaptive Rendering] Enabled = true（默认）
- [CPU Power Saving] Enabled = true（默认）
- 深层补丁 = 待创作分支合入后另测（当前 false 不动）
- 结束时副本 cfg 已恢复基线 SHA256 0d3ca42a…（与实验前逐字节一致）

## 7. 证据索引

- 本报告 + A20-REPORT.md：`tmp-azurite-takeover/experiments/A20-run1/`
- 18+1 轮原始记录：`launches/<config>-<cold|hot>-<i>/{record.json,launch.json,launch-spec.json,events.ndjson,bepinex-LogOutput.log,unity.log,output/unity.log}`
- 聚合：`aggregates.json`
- 启动器自测：`selftest/`（4 dry-run 用例）
- 启动器：`experiments/tooling/Launch-AAHeadless.ps1`（SHA256 d48205ed…；原版备份 .bak-pre-a20 SHA256 b65dfd8d…）

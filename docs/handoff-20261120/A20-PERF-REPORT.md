# A20 性能对照验收报告（离屏图形模式）

- 执行者：perf（task-11）
- 时间：2026-11-21 01:56 – 02:47 Asia/Shanghai
- 对象：隔离副本 `E:\aamod\labs\Azurite-20261108-stable\install`（Azurite 1.0.38 + ModTheAzureArchive，profile AzuriteAcceptance）
- 启动方式：护栏启动器 `experiments/tooling/Launch-AAHeadless.ps1`（本任务扩展后），argv = `-batchmode -disableaudio -logFile <run>/output/unity.log`（**不带 -nographics**，离屏图形模式，spec.allowGpu=true）
- 证据根：`E:\aamod\tmp-azurite-takeover\experiments\A20-run1\`

## 0. 启动器扩展（本任务第一项工作）

`Launch-AAHeadless.ps1` 变更（原版备份：`Launch-AAHeadless.ps1.bak-pre-a20`，SHA256 `b65dfd8d…`；新版 SHA256 `d48205ed308c1172cf468981fa3d0f5db62096c02e34c0c41809557b3c5abab1`）：

1. **spec.allowGpu 开关**：`allowGpu=true` 时 R4 改为要求 argv **不含** `-nographics`（arg_nographics_absent），其余红线（R1 白名单 / R2 无进程 / R3 日常安静 / R5 单实例 / R6 低优先级）全部保留；state 记录 `graphicsMode=offscreen-gpu`。
2. **R7 新增**：运行期间若日常安装日志 mtime 变新（≤10 分钟，=用户恢复前台 AA 活动），立即终止本轮并标记 `pausedByDailyActivity=true`。
3. **采样增强**：每 2.5s 样本增加 cpuPercent（TotalProcessorTime 差分 / 逻辑核数）。

自测（`A20-run1/selftest/`，dry-run 4 用例全过）：
| 用例 | 期望 | 实测 |
| --- | --- | --- |
| allowGpu=true + 无 -nographics | 通过 11 检查 | ✅ exit 0, passed=true |
| allowGpu 缺省 + 无 -nographics | REDLINE[arg_nographics] 拒绝 | ✅ exit 2 |
| allowGpu=true 但含 -nographics | REDLINE[arg_nographics_absent] 拒绝 | ✅ exit 2 |
| 日常安装 exe | REDLINE[exe_not_daily_install] 拒绝 | ✅ exit 2 |

## 1. 测量设计（实际执行）

- 3 配置 × 冷/热 × 3 = **18 轮有效启动**（另有 1 轮链路冒烟）：
  - `ar-on-cps-on`（两开关均 true = 当前默认）
  - `ar-off-cps-on`（Adaptive Rendering.Enabled=false）
  - `ar-on-cps-off`（CPU Power Saving.Enabled=false）
- 冷启动 = 删除 `BepInEx/cache` 后启动；热启动 = 连续第二次启动。
- 每轮 130s：护栏启动 → 等待 databasesReady=True → 稳态窗口采样 → 杀进程收尾。
- cfg 切换仅改副本内 `profiles/AzuriteAcceptance/configs/halocue.azurite.cfg`，每轮记录前后 SHA256（三组哈希各异且组内一致，见 §4）。
- 每轮保留：launch.json（11 项红线检查全过）、BepInEx 日志副本、unity.log、events.ndjson、record.json。

## 2. 指标定义与已知局限

- **logReadyLagSeconds**（主指标）：BepInEx 日志内首条 editor-work（boot）→ 首条 databasesReady=True 的时间戳差。日志时间戳是插件单调钟，不受轮询影响。
- **readyWallSeconds（作废）**：进程启动→就绪的墙钟轮询值，发现轮询会读到上一轮日志残留（全部为 0.4s），该列不可信，**作废不用**。冷/热真实墙钟总时长仅能从 logReadyLag + BepInEx 头部时间间接推断。
- **Update gaps 稳态窗口**：databasesReady=True 后 12s 起、samples>100 的 5s 窗口（每轮 18–23 个）；取 samples 中位数、gt16.7 率中位数、gt100 合计、max gap。
- **CPU%**：进程 TotalProcessorTime 差分 / (墙钟×逻辑核数)，稳态期（ready+10s 后）中位数。
- **峰值内存**：进程 WorkingSet64 峰值。
- **GPU/显存**：离屏 batchmode 下无可用 GPU 计数器，**未覆盖**（与"证据边界"一致，不冒充）。

## 3. 原始数据（18 轮）

| config | c/h | i | lag(s) | samp/5s | gt100 | maxGap(ms) | cpu% | peakMB |
|---|---|---|---|---|---|---|---|---|
| ar-off-cps-on | C | 0 | 2.55 | 10592 | 0 | 28.9 | 11.7 | 1317.9 |
| ar-off-cps-on | C | 1 | 3.29 | 9373 | 2 | 127.3 | 11.4 | 1311.4 |
| ar-off-cps-on | C | 2 | 1.83 | 5228 | 14 | 323.4 | 5.8 | 1367.0 |
| ar-off-cps-on | H | 0 | 4.97 | 10655 | 1 | 581.4 | 11.0 | 1315.5 |
| ar-off-cps-on | H | 1 | 2.74 | 10101 | 4 | 972.2 | 11.7 | 1384.0 |
| ar-off-cps-on | H | 2 | 3.75 | 10243 | 1 | 163.2 | 11.0 | 1370.2 |
| ar-on-cps-off | C | 0 | 1.78 | 10316 | 4 | 318.7 | 10.9 | 1276.9 |
| ar-on-cps-off | C | 1 | 3.54 | 10767 | 0 | 29.7 | 12.2 | 1274.8 |
| ar-on-cps-off | C | 2 | 2.65 | 10132 | 1 | 121.2 | 11.2 | 1397.2 |
| ar-on-cps-off | H | 0 | 2.76 | 10313 | 0 | 82.7 | 11.0 | 1270.4 |
| ar-on-cps-off | H | 1 | 2.92 | 10459 | 0 | 68.1 | 12.1 | 1423.0 |
| ar-on-cps-off | H | 2 | 1.63 | 10156 | 3 | 116.0 | 11.6 | 1404.5 |
| ar-on-cps-on | C | 0 | 3.52 | 9581 | 1 | 103.2 | 10.4 | 1364.1 |
| ar-on-cps-on | C | 1 | 1.80 | 9667 | 3 | 186.5 | 10.3 | 1357.0 |
| ar-on-cps-on | C | 2 | 1.93 | 7974 | 19 | 354.1 | 7.9 | 1404.1 |
| ar-on-cps-on | H | 0 | 1.81 | 9915 | 1 | 172.1 | 9.8 | 1362.5 |
| ar-on-cps-on | H | 1 | 1.73 | 10561 | 1 | 120.0 | 10.3 | 1392.1 |
| ar-on-cps-on | H | 2 | 8.42 | 10017 | 0 | 65.7 | 7.7 | 1270.1 |

已知离群轮：ar-off-cps-on C2（cpu 5.8、samples 5228、gt100=14）与 ar-on-cps-on H2（lag 8.42）——系统级噪声（无 AA 以外进程被本任务启动；后台有常驻服务），保留不剔除，判定用中位数。

## 4. cfg 切换哈希证据

| 组 | cfg SHA256（每轮启动后实测，组内一致） |
|---|---|
| ar-on-cps-on（默认） | `0d3ca42ae3af63c8e147fc2657747f8e6a166cc4f472df797c0ad43c6249bf38` |
| ar-off-cps-on | `d152b11d514e7cbd7fcb4e5a16299d02747998fde41a42b0320b10be0ddff4d3` |
| ar-on-cps-off | `dd04b2722e6b4e11113f531914d7b82c8770c2eaf781aaf901eae808a119aaeb` |
| 结束后恢复 | `0d3ca42a…`（与实验前基线逐字节一致 ✅） |

## 5. 组聚合（中位数 / 极差，n=3）

| 组 | lag med(s) | lag 极差 | samp/5s med | samp 极差 | cpu% med | cpu 极差 | peak med(MB) | gt100 合计 | maxGap max |
|---|---|---|---|---|---|---|---|---|---|
| ar-on-cps-on 冷 | 1.93 | 1.72 | 9581 | 1693 | 10.3 | 2.5 | 1364 | 23 | 354.1 |
| ar-on-cps-on 热 | 1.81 | 6.69 | 10017 | 646 | 9.8 | 2.6 | 1363 | 2 | 172.1 |
| ar-off-cps-on 冷 | 2.55 | 1.46 | 9373 | 5364 | 11.4 | 5.9 | 1318 | 16 | 323.4 |
| ar-off-cps-on 热 | 3.75 | 2.23 | 10243 | 554 | 11.0 | 0.7 | 1370 | 6 | 972.2 |
| ar-on-cps-off 冷 | 2.65 | 1.76 | 10316 | 635 | 11.2 | 1.3 | 1277 | 5 | 318.7 |
| ar-on-cps-off 热 | 2.76 | 1.29 | 10313 | 303 | 11.6 | 1.1 | 1405 | 3 | 116.0 |

## 6. 观测

1. **CPU Power Saving 关闭 → CPU 一致上升**：CPS-off 6 轮 cpu% 全部 ≥10.9；CPS-on 6 轮全部 ≤10.4。两组零重叠，关闭使稳态 CPU 中位数上升约 **1.2–1.4pp（相对 ~12–14%）**，方向在冷/热两种状态下一致 → 这是可重复的 CPU 收益（开启状态）。
2. **Adaptive Rendering 关闭 → CPU 同样一致上升**：AR-off 6 轮 cpu% 中位数 11.0–11.4（6 轮中 5 轮 ≥11.0）；AR-on 9.8–10.4（除 1 轮离群 7.9/7.7 外全部 ~10）。方向一致、幅度 ~0.7–1.3pp，但因 AR-off-cold 组内极差 5.9pp（离群轮），**收益未明确超过组内波动**。
3. **启动延迟（logReadyLag）**：各组中位数 1.8–3.8s，组间差异小于组内极差 → 无可重复的启动收益/代价。
4. **Update gaps**：稳态 samples/5s 各组 ~9.4–10.6k（player-loop 未被封顶的证据：target=-1 vsync=1；CPS 的"confirmed quiet editor"降频在无头空载 CatalogScene 下不触发逻辑降档）。gt100 合计与 maxGap 差异均在噪声范围。
5. **内存**：峰值 1270–1423MB，各组中位差 <5%，无 >5% 的可重复退化。
6. **红线执行**：18 轮全部经护栏启动（11 项检查全过，graphicsMode=offscreen-gpu）；全程无日常安装写入（F:\AzureArchive_101\BepInEx\LogOutput.log mtime 始终为 2026-11-20 22:15:43）；无残留 AA 进程；单实例互斥全程生效；R7 未触发（期间用户无前台 AA 活动）。

## 7. 未覆盖 / 范围缩减（如实）

| 项 | 状态 |
|---|---|
| GPU / 显存 / P95-P99 GPU 帧时间 | 未覆盖——离屏 batchmode 无 GPU 计数器，插件报告 unsupported counters（不冒充） |
| 5000 行大稿 | 未测——本副本与 backups 无现成大稿；未用 MCP 生成（避免在性能样本外引入额外真实会话风险） |
| 中稿（数学必修二总结-0.aap2） | 未测——本轮仅用 CatalogScene 空载 + 默认状态；稿内编辑态对照留待后续 |
| 虚拟化/渐进加载/延迟选择器等深层补丁 | **在创作分支（e8dddb9），不在本副本 1.0.38 内**，本副本 cfg 对应项为默认 false 且 DLL 不含实现 → 未测，列入后续候选构建验收清单 |
| ProfileEditor 开/关 | 不做对照——只读诊断开关，关闭只会丢失 editor-work 数据；保持现状（acceptance profile 内 true） |
| A5 撤销重做 / 真实焦点流畅度 | 与 A20 无关，沿用既有验收结论（⊘ CLI 不可验 / 需 GUI） |

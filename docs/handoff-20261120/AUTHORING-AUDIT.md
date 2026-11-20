# 创作分支批量编辑检查点 — 独立复核与状态清点

- 审计身份：`authoring-audit`（DSH 团队 task-5）
- 时间：2026-11-20 22:0x–22:4x Asia/Shanghai
- 被审对象：`C:\Users\21294\.codex\worktrees\azurite-authoring-20261108\Azurite-github-publish-1.0.38`
- HEAD：`e8dddb9c10ab525f9f6a5c62edc622cf5aec3b20`（branch `codex/azurite-authoring-20261108`）
- 工作树状态：**22 个已修改 tracked 文件 + 474 个未跟踪文件**（git 汇总为 100 个顶层条目）
- 本报告性质：**只读**审计。原工作树没有 commit / stash / reset / clean，没有任何文件被修改。
- 复跑全部在拷贝出来的干净副本 `E:\aamod\tmp-azurite-takeover\authoring-copy`（删除 .git/.superpowers/.team-board/所有 bin/obj）中进行。

---

## 0. 一句话结论

**10 个已提交检查点的源码级回归基本成立（32/32 portable 项目全绿），但"源码级通过"被高估了两处，而且没有任何一项在真实 AA 里验收过。**
本轮独立复核**抓到 2 个 Codex 交接时没记录的缺陷**：

1. **真回归**（e8dddb9 引入）：`Test-AuthoringPluginWiring.ps1` 15 项里 **1 项 FAIL** —— `compiled Tick services authoring before existing early returns`。父提交 0d9b889 同一测试 **15/15 PASS**，差异就是 e8dddb9 在 `Tick` 第一行插入了 `DeferredSelectorPolicy.Pause(...)`。
2. **测试夹具是字节钉子**：未跟踪的 `NormalEditor.Loader.Routes.Tests` 有 `CacheBindingRules.CoreSha256` 硬编码 SHA256，只匹配一个**未跟踪的旧 build 产物** `Azurite.CacheRoot.Core.LabOnly.dll`。从 tracked 源码重新构建得到的字节不同（`a9a04029…` vs 钉子 `32ac8c18…`），142 项里 1 项必然 FAIL。把那个旧 DLL 拷进输出目录后 **142/142 PASS** —— 证明这是产物不可复现问题，不是逻辑错误，但"干净副本复跑"这条方法论在这类钉子上会假失败。

---

## 1. 复跑实测结果（干净副本）

### 1.1 Portable 套件（`tools/Test-Source.ps1 -Suite Portable` 的 32 个 job）

环境：`dotnet 10.0.203`，完整 Windows 环境变量，工作目录 `E:\aamod\tmp-azurite-takeover\authoring-copy`（无 .git、无 bin/obj、无 artifacts）。

| 项目 | 退出码 | 结果 |
| --- | --- | --- |
| Core（build） | 0 | **0 警告 0 错误** |
| Tests (HostProfile) | 0 | RESULT: 81 passed; 0 failed |
| ScrollTests | 0 | RESULT 16/16 passed |
| PreviewTests | 0 | RESULT: 35 passed; 0 failed |
| PreviewLeaseTests | 0 | RESULT: 49 passed; 0 failed |
| ObserverTests | 0 | RESULT: 41 passed; 0 failed |
| AnimationTests | 0 | RESULT 11/11 |
| RowReuseTests | 0 | RESULT 31/31 |
| BloomTests | 0 | 49 checks passed |
| InteropTests | 0 | RESULT 21/21 |
| CadenceTests | 0 | RESULT 14/14 |
| CharacterTests | 0 | RESULT 56/56 |
| ProgressiveTests | 0 | RESULT 10/10 |
| RowCacheTests | 0 | RESULT 10/10 |
| VirtualizationTests | 0 | RESULT 54/54 |
| BackgroundGalleryTests | 0 | passed |
| WheelTuningTests | 0 | 13/13 passed |
| WheelSettingsControlTests | 0 | passed |
| AuthoringSpeakerShortcutTests | 0 | passed |
| HostActivityAuthoringTests | 0 | 8/8 passed |
| BoxSelectionTests | 0 | RESULT 16/16 passed |
| BatchEditingTests | 0 | NativeSetTextTests 15/15 |
| PresetCardPackTests | 0 | RESULT: 10 passed; 0 failed |
| CardPackIntegrationTests | 0 | 1 check（PASS native card pack mapping） |
| CardPackV2Tests | 0 | RESULT: 100 passed; 0 failed |
| **AuthoringHostBridgeChecks** | 0 | **RESULT: 204 passed; 0 failed** |
| AuthoringHostContentChecks | 0 | RESULT 14 passed |
| AuthoringHostSelectionChecks | 0 | RESULT 20 passed / 0 failed |
| AuthoringHostResourceChecks | 0 | RESULT 25 passed / 0 failed |
| AuthoringHostViewChecks | 0 | RESULT 56 passed / 0 failed |
| AuthoringHostRootChecks | 0 | RESULT 33 passed / 0 failed |
| AuthoringPluginWiringChecks | 0 | RESULT 53 passed, 0 failed |
| AuthoringHostCopyChecks | 0 | RESULT: 21 passed; 0 failed |

**合计：32/32 job 退出码 0，212 秒，0 项 FAIL。日志在 `logs/`，汇总在 `portable-results.json`。**
这复现了交接记录的"Portable 全绿"。

> ⚠️ **证据边界**：这 32 个 job **全部是源码/离线层**。每个 log 自己都写了
> `sourceOnly=true / nativeExecuted=false / hostExecuted=false / observationsAreFixtures=true`。
> 其中大量是"实际 Host 源码 + 外部 native/Unity stub 链接"——即真代码配假 native，**不是 AA 里跑过**。

### 1.2 交接记录**没提**、也不在 Portable 里的两个未跟踪测试项目

| 项目 | 结果 | 说明 |
| --- | --- | --- |
| `AcceptanceHarness/NormalEditor.Loader.Tests` | **101 passed / 0 failed** | 需要传一个"全新绝对路径目录"参数。`RESULT passed=101 failed=0; sourceOnly=true; nativeExecuted=false; hostExecuted=false; observationsAreFixtures=true` |
| `AcceptanceHarness/NormalEditor.Loader.Routes.Tests` | **142 checks / 1 failed**（→ 换入旧 DLL 后 142/0） | 失败的 1 项见第 3.2 节 |

### 1.3 发布链静态测试（`tests/release/`）

| 测试 | 结果 |
| --- | --- |
| `Test-AuthoringBuildModes.ps1` | **RESULT: 7 passed; 0 failed** |
| `Test-AuthoringPluginWiring.ps1` @ HEAD e8dddb9 | **14 PASS / 1 FAIL → 抛异常退出** ❌ |
| `Test-AuthoringPluginWiring.ps1` @ 父提交 0d9b889 | **15 compiled wiring checks passed** ✅ |

### 1.4 源码构建（AuthoringSource 模式）

| 对象 | 命令 | 结果 |
| --- | --- | --- |
| HEAD e8dddb9 | `tools/Build-AuthoringSource.ps1` | **PASS**，Plugin=`Azurite.AuthoringSource.dll`、Core=`Azurite.Core.dll`，`releaseEligible=False`、`launchesAA=False` |
| 父提交 0d9b889 | 同上 | **PASS** |
| Original 模式（默认） | `dotnet build Plugin/Azurite.csproj` | **0 警告 0 错误** |
| SourceStaging 模式 | `-p:AzuriteCoreMode=SourceStaging` | **失败（设计如此）**：`SourceStaging requires the producer-generated SourceStagingBuildId` |
| AuthoringSource 模式直接 msbuild | `-p:AzuriteCoreMode=AuthoringSource` | **失败（设计如此）**：必须走 `--artifacts-path` / 脚本 |

---

## 2. 10 个已提交检查点逐项盘点

**所有 10 项的"真实 AA 验收状态"一律为：未验收（nativeAcceptance = not-performed）。** 下表"源码级"列是本次实测。

| # | 提交 | 实现的功能 | 生产代码落点 | 测试覆盖 | 本次实测 | 真实 AA |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `9af9f45` | 批量文本 → 原生 `line.update` 计划（保留原文与持久 ID，≤500 条，超限整批拒绝） | `Core/AuthoringBatchNativePlan.cs`（新 87 行） | `BatchEditingTests/NativeSetTextTests.cs`（240 行） | ✅ Portable PASS（15/15 原生计划检查） | ❌ 未验收 |
| 2 | `db5e18f` | 共享事务入口：一次 Check → 一次 Apply，批量文本纳入同一提交生命周期 | `Plugin/AuthoringCardPackHost.Batch.cs`(138) / `Host.cs`(+127) / `Core/AuthoringBatchHost.cs` | `AuthoringHostBridgeChecks/BatchSetTextChecks.cs`(182) + `AttemptRevocationChecks.cs`(89) | ✅ 204/204 PASS | ❌ 未验收 |
| 3 | `b97c6e5` | 批量发言槽位（精确 9 文件，画外音重置提示） | `Host.Speaker.cs`(174) / `Core/AuthoringBatchSpeakerPlan.cs` | `SpeakerBatchChecks.cs`(152，22 用例) | ✅ PASS | ❌ 未验收 |
| 4 | `df16d21` | 批量删除（保留节点与连接，可清空行列表） | `Host.Delete.cs`(173) / `Core/AuthoringBatchDeletePlan.cs` | `DeleteBatchChecks.cs`(168，28 用例) | ✅ PASS | ❌ 未验收 |
| 5 | `f3eea0c` | 多行插入（按原生 previous 规则继承/清理退场角色） | `Host.Multiline.cs`(325) / `Core/AuthoringBatchMultilinePlan.cs`(94) | `MultilineBatchChecks.cs`(184，31 用例) | ✅ PASS | ❌ 未验收 |
| 6 | `9e02491` | 批量复制 + 显式配音策略（CopyVoice 布尔） | `Host.Duplicate.cs`(261) / `Core/AuthoringBatchDuplicatePlan.cs`(116) | `DuplicateBatchChecks.cs`(165，34 用例) | ✅ PASS | ❌ 未验收 |
| 7 | `88c72c8` | 创作准入与渲染安全关停分离；清理失败继续阻止编辑 | `Plugin.Authoring.cs`(+36) / `Plugin.cs`(+91) / `FpsController.cs` / `EditorScrollTuning.cs` / `Core/RenderIntervalLease.cs` | `AuthoringPluginWiringChecks/CleanupChecks.cs`(47) + `NativeCleanupBoundaries.cs`(110) + `tests/release/Test-AuthoringPluginWiring.ps1`(161) | ✅ Portable 53/53；release 测试 **HEAD 上 14/15 FAIL** ❌ | ❌ 未验收 |
| 8 | `d1bae90` | 批量工具栏接线（真实 launcher/View + guarded selection + 共享 workflow） | `Core/AuthoringBatchWorkflow.cs`(164) / `HostBatchToolbar.cs`(308) / `HostRoot.cs`(+191) / `HostSelection.cs` | `AuthoringHostViewChecks`(+约 500 行：BatchToolbarChecks/RootToolbarChecks/SelectionSurfaceChecks/RootViewBoundaries)、`AuthoringHostRootChecks/BatchRootChecks.cs`、`AuthoringHostBridgeChecks/UiStatusChecks.cs` | ✅ 56/56 + 33/33 PASS | ❌ 未验收 |
| 9 | `0d9b889` | 源码暂存构建与包校验（canonical source-built Core/plugin 配对、非发布 staging 归档） | `SourceStaging.props`、`tools/SourceStaging.psm1`(188)、`Build-SourceStaging.ps1`、`Stage-SourceCandidate.ps1`、`Tests/release/Test-SourceStaging*.ps1`、`Plugin/Azurite.csproj` | 33 portable + 29 pipeline cases + 25 review controls（提交讯息自述） | ⚠️ **本次未单独复跑 pipeline/review**（需 producer 生成的 build receipt 与 fresh artifacts 目录；直接 msbuild 被设计性拒绝）。Original/AuthoringSource 两种模式构建均 PASS | ❌ 未验收 / 未发布 |
| 10 | `e8dddb9` | 延迟选择器独立开关（`[Large Projects] DeferEditorSelectors=false` 默认关） | `Plugin/DeferredSelectorPolicy.cs`(33) / `Plugin.cs`(+10) | `SelectorTests/SelectorPolicyChecks.cs`(75) + `SelectorWiringChecks.cs`(36) + `docs/performance/deferred-selector-toggle.md` | ❌ **引入 Tick 顺序回归**（见 3.1）；SelectorTests 属 Host 套件，本次未跑（需要真实 AA interop） | ❌ 未验收 |

### 检查点链的共同特征（重要）

- 6 个批量编辑检查点（1–6）**共用同一个提交接缝**：`AuthoringCardPackHost.Submit(...)`。每个新功能只是往这个函数加一个 `else if` 分支 + 一个 `Prepare*` 方法。设计上是干净的（一次 Check → 一次 Apply，失败进 Uncertainty 并要求显式 Recover），但**再干净也只有 stub 验证过**。
- 测试里 `NativeStubs.cs` 是从 52 行长到几百行的**假 native**。所有 batch/speaker/delete/multiline/duplicate 的"通过"都是对着这套 stub。
- 没有任何提交记录里出现真实 AA 进程、真实 `AuthoringEditorSession`、真实 `OperationManager` 历史。

---

## 3. 本次复核新发现的两个缺陷（交接记录里没有）

### 3.1 【真回归】e8dddb9 破坏了 Tick 首动作契约

**证据链（可复现）：**

- `tests/release/Test-AuthoringPluginWiring.ps1:89-92` 断言 `Plugin.Tick` 的第一条非 Nop 指令必须是 `Ldarg_0` 且紧接着调用 `TickCardPackAuthoring`。
- `Plugin/Azurite/Plugin.cs:547-553`（HEAD）：
  ```csharp
  private void Tick(double now)
  {
      DeferredSelectorPolicy.Pause(_deferredSelectors);   // ← e8dddb9 新增，成为第一条指令
  #if AZURITE_AUTHORING_SOURCE
      if (!TickCardPackAuthoring()) return;
  #endif
  ```
- `git show e8dddb9 -- Plugin/Azurite/Plugin.cs` 明确显示这行是 e8dddb9 新增（父提交 0d9b889 的 Tick 里没有）。

**实测对比：**

| 版本 | AuthoringSource 构建 | Test-AuthoringPluginWiring |
| --- | --- | --- |
| 0d9b889（父） | PASS | **15 compiled wiring checks passed** ✅ |
| e8dddb9（HEAD） | PASS | **14 PASS / 1 FAIL** → `Authoring is not the first Tick action.` ❌ |

**影响评估：**
- 这是**发布链静态门**失败，不是运行时崩溃。插件仍能编译、AuthoringSource 仍能构建。
- 但这条断言的意义是"authoring 必须在 Tick 的既有 early-return 之前被服务"。现在 `Pause` 抢在它前面。`DeferredSelectorPolicy.Pause` 本身只是读开关并暂停选择器，实际语义风险低，但**契约被破坏了，且没有任何一处测试或文档记录这个变更**。
- 提交讯息只写了"Add independently gated deferred selector configuration"，没提修改 Tick 顺序。

**建议**：要么把 `Pause` 移到 `#if AZURITE_AUTHORING_SOURCE` 块之后，要么显式更新测试断言并在文档记录新的 Tick 顺序契约。**在修复前，HEAD e8dddb9 不能作为"源码检查点全部通过"的基线**。

### 3.2 【测试夹具问题】Loader.Routes.Tests 钉住了一个未跟踪的旧二进制

**证据链：**

- `AcceptanceHarness/NormalEditor.Loader.Routes/CacheBindingRules.cs:9`：
  `public const string CoreSha256="32ac8c188e676779c371d628c96c493ec8f7866da04e17e014a92f97895c8ed7";`
- 断言在 `AcceptanceHarness/NormalEditor.Loader.Routes.Tests/CacheBindingChecks.cs:35`：对 `typeof(NativeStringReader).Assembly.Location` 的字节求 SHA256 并与该常量比较。
- `CacheRoot.Core` 的 tracked 源码是干净的（`git status` 空），但 `bin/` 被 `.gitignore` 的 `**/bin/` 忽略——**那个匹配钉子的 DLL 不在仓库里**。
- 干净副本从源码重建 → `a9a04029…` ≠ 钉子 `32ac8c18…` → 必然 FAIL。
- 把原工作树里的旧 DLL 拷进副本输出目录再跑 → **142/142 PASS**。

**影响评估：**
- 不是逻辑缺陷，是**构建不可复现 + 夹具钉字节**的组合。
- 但后果严重：任何"干净副本复跑"都会在这里假失败，从而让人误以为代码坏了；反过来，只要本地留着那个旧 DLL 就会假通过。
- 这类钉子在 `CacheRoot.Tests`、`rebuild-20261115/root/loader-fixture-*` 里也有同源模式，需要在真实 AA 验收前统一处理（改成钉源码树 + 允许重建，或把该二进制纳入版本控制并记录来源）。

---

## 4. 22 个已修改 WIP 文件逐项说明

**全部 22 个都不属于批量编辑/卡包功能线**，它们是 2026-11-13～11-15 的**另一条线：FullRestricted / 普通编辑器入口 + 无头启动恢复工具**。按 mtime 分成两簇。

### 4.1 第一簇：11 个 `AcceptanceHarness` csproj + 2 个 MetadataChecks（mtime 2026-11-15 16:45–16:48）

| 文件 | 改了什么 | 性质 | 是否可用 |
| --- | --- | --- | --- |
| `NormalEditor.Adapter.Core.csproj` | `<Reference>`+`HintPath` → `<ProjectReference>`（Policy、EditorIsolation.Policy） | 构建接线 | ✅ 语义正确，但**未提交** |
| `NormalEditor.Adapter.Host.csproj` | 同上 + `RequireExplicitHost` 目标从"检查一条固定路径"改成"遍历 `%(Reference.HintPath)` 逐个检查存在" | 构建接线 + 更严的门 | ✅ 同上 |
| `NormalEditor.Adapter.Tests.csproj` | 同上 | 构建接线 | ✅ |
| `NormalEditor.Entry.Evidence.csproj` / `.Tests.csproj` | 关键：原本引用 `../../artifacts/normal-editor-entry-20261113/snapshot/binaries/*.dll`——**那个 artifacts 目录在 11-15 被删了**，改成 `ProjectReference` 到 `NormalEditor.Entry.Core` | **灾后重建接缝** | ✅ 这正是"artifacts 被清理后重建"的直接证据 |
| `NormalEditor.Entry.Host.csproj` | 4 个 `<Reference>` → `ProjectReference` + 新 HintPath 门 | 构建接线 | ✅ |
| `NormalEditor.Entry.Shutdown.Tests.csproj` | 同上 | 构建接线 | ✅ |
| `NormalEditor.Entry.Tests.csproj` | 同上 | 构建接线 | ✅ |
| `NormalEditor.Policy.csproj` | 1 行 | 构建接线 | ✅ |
| `NormalEditor.Tests.csproj` | 1 行 | 构建接线 | ✅ |
| `ShutdownHostMetadataChecks.cs` | 2 行（±1） | 断言微调 | ✅ |
| `MetadataChecks.cs` | 2 行（±1） | 断言微调 | ✅ |

**判读**：这一簇是**纯构建接线修复**，起因就是 2026-11-15 的 artifacts 清理。不修这些，`AcceptanceHarness` 里一半项目根本编不过。**它们是必要且应当提交的**，但至今仍在工作树里裸着。

### 4.2 第二簇：11 个 headless 工具 / FullRestricted（mtime 2026-11-12 20:43 – 11-14 03:10）

| 文件 | 改了什么 | 功能线 | 性质 |
| --- | --- | --- | --- |
| `tools/headless/RestrictedCliFullRestricted.cs` | +273 行（本批最大） | FullRestricted CLI 本体 | 生产工具代码 |
| `tools/headless/FullRestrictedStageTransition.psm1` | +115 | 阶段转换 | 生产工具代码 |
| `tools/headless/FullRestrictedCapabilitiesRuntime.psm1` | +87 | capabilities 运行时 | 生产工具代码 |
| `tools/headless/New-AzuriteFullCapabilitiesInputs.ps1` | +39 | capabilities 输入装配 | 生产工具代码 |
| `tools/headless/FullRestrictedCapabilitiesPlan.psm1` | +36 | capabilities 计划 | 生产工具代码 |
| `tools/headless/RestrictedCliNative.cs` | 1 行 | native 声明 | 生产工具代码 |
| `tools/headless/CurrentAccountCli.psm1` | 2 行 | CLI 入口 | 生产工具代码 |
| `Tests/headless/FullRestrictedOwnership.Tests.cs` | 14 行 | 对应测试 | 测试 |
| `Tests/headless/Test-FullCapabilitiesInputs.ps1` | 13 行 | 对应测试 | 测试 |
| `Tests/headless/Test-FullRestrictedStageTransition.ps1` | 2 行 | 对应测试 | 测试 |

**判读**：这是"**打通无头启动**"这条主线的 WIP（`AZURITE-TAKEOVER.md` 第 5 节把这件事列为第一卡点，已卡 8 天）。**与批量编辑/卡包完全无关**，也**没有被本次 Portable 套件覆盖**（Portable 不包含 FullRestricted / headless）。
**结论：这批是真在用的开发中代码，既不能丢也不能当"功能已可用"——它连一次成功启动都没跑通。**

---

## 5. 100 个未跟踪顶层条目（实际 474 个文件）分类

按"文件真实用途"分四类。完整清单在 `evidence-untracked-files.txt`。

### A. 生产源码（可提交）— 7 个项目 + 1 个 doc，共 56 文件

| 路径 | 文件数 | 说明 | 重建过？ |
| --- | --- | --- | --- |
| `AcceptanceHarness/NormalEditor.Loader/` | 2（含 csproj） | BepInEx 插件入口 `NormalLoaderPlugin.cs` | ✅ 11-15 后重建 |
| `AcceptanceHarness/NormalEditor.Loader.Core/` | 10（8 .cs + csproj + PREPARATION-SCHEMA.md） | 准入/生命周期/准备/路由契约/启动窗口 | ✅ 重建 |
| `AcceptanceHarness/NormalEditor.Loader.Guard/` | 3 | Guard bootstrap + startup guard | ✅ 重建 |
| `AcceptanceHarness/NormalEditor.Loader.Routes/` | 18（16 .cs + csproj + README） | 6+1 路路由钩子、cache 绑定、native jump、写哨兵 | ✅ 重建 |
| `AcceptanceHarness/NormalEditor.Loader.Routes.Tests/` | 8 | 上述 142 项检查 | ✅ 重建 |
| `AcceptanceHarness/NormalEditor.Loader.Tests/` | 8 | 上述 101 项检查 | ✅ 重建 |
| `AcceptanceHarness/FullRestricted.FreshPreflight/CachePrerequisites.csproj` | 1 | 单独一个 csproj（同目录其它文件为 12 文件的 fixture） | 部分 |
| `docs/superpowers/plans/2026-11-14-readable-canary-cleanup-qualification.md` | 1 | 计划文档 | — |

**这是全仓库最值钱的一块**：它就是 `AZURITE-TAKEOVER.md` §6 P1 说的"普通编辑器入口/加载/关闭驱动"。**它已重建完成且能编译、能跑 101+142 项检查**，但 **HEAD 之前一直是未跟踪状态**——一次 `git clean` 就没了。

### B. 生产工具脚本 — 14 文件（`tools/headless/`）

`AzuriteCapabilitiesSequence.psm1`、`Invoke-AzuriteCapabilitiesSequence.ps1`(+Child)、`Capture-AzuriteFullCapabilitiesIdentityShared.ps1`、`FullRestrictedClosedFailedRecovery.ps1`、`FullRestrictedPreparationRecovery.psm1`、`FullRestrictedReadonlyTraceContinuation.ps1`、`Preserve-AzuriteClosedFailedRuntime.ps1`、`Preserve-AzuriteFailedCapabilitiesRun.ps1`、`Preserve-AzuriteFailedCapabilitiesRun9c.ps1`、`Recover-AzuriteFullPreparation.ps1`、`Resume-AzuriteReadonlyTraceRecovery.ps1`、`Test-NormalEditorSource.ps1`、`ArchivedReadonlyTraceDelete.cs`。

**判读**：无头启动/失败保全/恢复的驱动脚本。与 §4.2 的 11 个修改文件同一条线。

### C. 测试 — 44 文件（`Tests/headless/`）

5 个 `.Tests.cs`（FullMonitorManagedHost、FullOuterDrainTrace、FullRestrictedAcquisitionBoundaries、FullRestrictedCreationOwnership、FullRestrictedPreservedQueries）+ 39 个 `Test-*.ps1`。

**判读**：覆盖 capabilities 序列、失败保全、恢复身份、启动异常、prepared hash、readonly trace 等。**这些是 FullRestricted 的回归网，但 Portable 套件完全不含它们**（Portable 只跑根目录的 `*Tests/`、`*Checks/` 项目）。

### D. 一次性实验夹具 / 诊断产物 — 约 350 文件（可归档，**不要提交**）

35 个 `AcceptanceHarness/FullRestricted.*` 目录，全部是**某一次具体实验的现场**：

| 类型 | 目录 | 文件数 | 性质 |
| --- | --- | --- | --- |
| Native 探针夹具 | `NativeFixture`(50)、`CdbOwnedWaitFixture`(39)、`CdbOwnedWaitNativeControl2`(14)、`ObserverDraft`(7)、`WaitFixture`/`WaitRpcFixture`/`WaitWindowFixture`(20) | 130 | 一次性的调试器/等待/观察器实验 |
| Bound-run 现场 | `BoundRun1eba`(18)、`BoundRun1ebaFix1`(19)、`BoundRun72f8`(14)、`BoundRunBfdd`(13) | 64 | 已失败运行的现场快照（**必须保留为证据，但不应进仓库**） |
| 资格/交接 | `ActualAaDebuggerHandoff1`(15)、`ActualAaNoTraceQualification1`(14)、`CanonicalBuildReceipt1`(3) | 32 | 一次性资格现场 |
| 安全/错误语义 | `NtErrors*Fixture`(4×7=28)、`ComSecurityFixtureA/B`(12)、`Hkcr*Fixture`(14)、`WriteProtectionFixture`(7)、`ReadRestriction*/`ReadBudget*`(24)、`Ps7Io*`(17) | 102 | 被测行为的最小复现夹具 |
| 恢复基线 | `PreBootstrap*`(20)、`FreshPreflight`(12 减 1 csproj) | 32 | 恢复流程夹具 |

### E. 不可提交 / 需单独处理 — 3 文件

`.team-board/board.db`、`board.db-wal`、`board.db-shm` —— **sqlite 看板，一致性未核实**（交接记录写明 3 个活动任务板数据库一致性未知）。**不要打开写，也不要提交。**

### 5.1 缺口标注

- ✅ **已重建**：`NormalEditor.Loader*` 全部（56 文件）——即 §6 表 P1 的"普通编辑器入口/加载/关闭驱动"。
- ⚠️ **部分缺口**：`NormalEditor.Entry.Core`（在 `AcceptanceHarness/NormalEditor.Entry.Core/`，**本次未在未跟踪清单里出现**，说明它是 tracked 的）——但它的 csproj 被 §4.1 改了引用方式。
- ❌ **仍缺**：`AZURITE-TAKEOVER.md` §6 P1 列的 `EditorEntryStage.cs`、`EditorEntryCases.cs`、Loader `CompilerAttributes.cs` 及若干类型声明。本次在未跟踪清单里**看到了 `NormalEditor.Loader.Routes/CompilerAttributes.cs`**（属于 Routes 项目），但 `EditorEntryStage/EditorEntryCases` 仍未出现——**这两个文件还是缺失状态**。
- ❌ **仍缺**：缓存诊断工具（§6 P1"只补选定的一套"）——`tools/headless/` 里没有对应的 cache 诊断入口。

---

## 6. 五类卡包（转场/战斗/动作/语言/音效）当前各缺什么

先说结构事实：**五类卡包共有两套并行实现，都只到源码层。**

| 层 | 位置 | 状态 |
| --- | --- | --- |
| **v1 PresetCardPack**（旧） | `Core/PresetCardPack.cs`、`PresetCardPackCatalog.cs`、`PresetCardPackNativePlan.cs`、`PresetCardPackValidator` | 只有 `NativePresetCardPackMapper.TryMap` → 一套 `NativeScriptData` 纯数据。**且 mapper 显式拒绝 combat/action/language**：默认分支写 `"step ... is unsupported by native ScriptData (combat, action, language and arbitrary effects are rejected)"` |
| **v2 CardPackV2**（新，推荐路径） | `Core/CardPackV2.cs`、`CardPackV2Compiler.cs`、`CardPackV2Workflow.cs`、`CardPackV2Json.cs` | 五族齐全，compiler 产出 `CardPackNativePlan.OperationsJson`；Workflow 有 preview/confirm/cancel/recover + 一次性 token + 不确定性恢复 |

### 逐族状态

| 族 | v2 编译 | 原生操作 | 测试 | 真实 AA 缺什么 |
| --- | --- | --- | --- | --- |
| **转场 transition** | ✅ `CardResourceKind.Transition` + `AddScene(line,"transition",...)` | `line.insert` with `scene.transition.resource_id` + `action:"set"` | CardPackV2Tests 100 项内含 | **缺**：真实 AA catalog 里 `transition.fade` 之类的 **native key → uint 映射**。`NativePresetResourceCatalog` 要求调用方从"已验证的当前 catalog"取 ID，**这个 catalog 在真实 AA 里从未取到过** |
| **战斗 combat** | ✅ `PresetCardKind.Combat`，强制 `Reactions = Stiff/Shake/FalldownL/FalldownR` + `effect:"Shot"` + 必需 sound | `line.insert` with `scene.sound` + character effect | 同上 | **缺**：AA 侧 reaction 枚举的真实取值、Shot 效果的触发时序、sound key 解析。**v1 mapper 直接拒绝 combat** |
| **动作 action** | ✅ `PresetCardKind.Action`，需 TargetSlot + Action 枚举 | `line.insert` with character `action` 字段 | 同上 | **缺**：AA 角色 action 枚举（`Actions` + `Curve`）的真实整数映射、slot 与 face/emoticon 的联动校验。**v1 mapper 直接拒绝 action** |
| **语言 language** | ✅ `PresetCardKind.Language`，强制 `Variants` 非空、locale 合法、mode=`dialogue`、必须选 speaker slot | `line.update` 文本替换 | 同上 | **缺**：AA 的语言/本地化真值表。**v1 迁移显式拒绝**："A language code alone contains no story dialogue" |
| **音效 sound** | ✅ `PresetCardKind.SoundEffect` | `line.insert` with `scene.sound` | 同上 | **缺**：AA sound catalog 的 key 解析与播放通道；**音频要能听到**这件事整个仓库没有任何验证入口（`docs` 里 normal argv 强制 `-disableaudio`） |

### 五类共同缺的三件事（这才是真卡点）

1. **资源目录从未在真实 AA 里枚举过**。五族全都依赖"调用方提供已从验证 catalog 解析出的 resourceKey"。`NormalRoutes.cs` 的 `RequireResourceEntry`/`RequireCatalogClosure` 到现在还是 **throw 未实现**，所以普通 Catalog 根本到不了 ready，也就永远拿不到 catalog。
2. **一次真实 `Apply` 从未发生**。`Host.Submit` 里 `checkedSession.Apply(operations, CatalogVersion)` 的 `AuthoringEditorSession.Current` 在测试里是 stub。真实 AA 里 `Current` 何时非空、`CatalogVersion` 是什么、`Apply` 返回什么 receipt——**全部未知**。
3. **Undo/Redo/Save/Reopen 全部零验证**。`OperationManager.Undo()/Redo()` 只做过 Cecil 元数据确认（见 `team/next-normal-editor-acceptance-map.md` §5），没有任何执行记录。

---

## 7. 「哪些功能真的能用，哪些只是源码级通过」

### 7.1 真的能用（在真实 AA 里已经跑过的）—— **答案是：一项都没有**

必须诚实说清楚：**本轮审计覆盖的全部 10 个检查点 + 5 类卡包 + 全部批量编辑功能，在真实 AA 中一次都没有执行过。**
证据是仓库自己的声明，不是我推断的：

- `docs/source-staging-gates.json`：`releaseEligible: false`、`runtimeAcceptance: "not-performed"`
- `docs/candidate-packaging.md:19`：BUILD.json 固定写 `runtimeAcceptance: "not-performed"`
- `rebuild-20261115/product-e8/REPORT.md:10`：`nativeExecuted=false, releaseEligible=false, runtimeAcceptance=not-performed`
- 每个 Portable 测试 log 自己打印 `nativeExecuted=false / hostExecuted=false / observationsAreFixtures=true`
- `AZURITE-TAKEOVER.md` §5「完全未完成」第 1 条：真实 AA 无头普通编辑器**从未跑通**，卡在 BepInEx/IL2CPP 预加载异常

**所以"用户最关心的问题"的直接答案是：目前没有任何一个功能可以被称作"真的能用"。** 唯一有真实运行证据的是**日常安装里已有的 1.0.37/38/39 老功能**，那是用户本来就在用的，不在这批检查点里。

### 7.2 源码级通过 + 工程质量可信（可以进入真实 AA 验收队列）

| 功能 | 源码证据强度 | 备注 |
| --- | --- | --- |
| 批量文本修改（→原生 line.update） | 强：计划层 + 事务层 + 204 项 bridge 检查 | 上了 AA 之后最该第一个验 |
| 批量发言槽位 | 强：22 用例 + 精确 9 文件边界 | 依赖真实 selection |
| 批量删除 | 强：28 用例，含 EmptiesNode 语义 | |
| 多行插入 | 强：31 用例，含原生 previous 继承/退场清理 | 复杂度最高，风险也最高 |
| 批量复制（含配音策略） | 强：34 用例 | |
| 创作/渲染关停分离（清理失败继续阻止编辑） | 中强：53 项 + IL 级接线检查 | **但 Tick 顺序契约被 e8dddb9 破坏** |
| 批量工具栏接线 | 中强：56 + 33 项 UI 边界检查 | 全是对 NGUI stub，真实 raycast/点击零覆盖 |
| 五类卡包 v2 | 中：100 项编译/工作流检查 | 缺资源目录 + 缺真实 Apply（见 §6） |
| 源码暂存构建/包校验 | 中：构建模式 7/7；pipeline 未复跑 | |
| 延迟选择器开关 | **弱**：引入回归且未修 | 见 §3.1 |

### 7.3 只是源码级通过、**风险被低估**的部分

1. **所有 "Host" 测试都是"真代码 + 假 native"**。`AuthoringHostBridgeChecks/NativeStubs.cs` 就是假的 `AuthoringEditorSession`/`OperationManager`/`Studio.*`。204 项全过只能证明"我这边的逻辑对着假 native 是对的"。
2. **e8dddb9 的 Tick 回归说明"最后一次提交没跑全 release 测试"**。这条恰好是最容易被漏的（Portable 不含 release 测试）。
3. **Loader.Routes 的字节钉子说明"干净副本复跑"会给我们假信号**。后续任何人在干净副本里跑这个测试都会看到 1 个 FAIL 然后去查不存在的 bug。
4. **`NormalEditor.Loader.Routes` 的路由 6+1 类还没凑齐**，`RequireResourceEntry`/`RequireCatalogClosure` 仍是 throw。这是**普通编辑器进不去的直接原因**，比批量编辑更上游。

---

## 8. 真实 AA 验收的最小测试清单（按依赖排序）

前提：**必须先打通"无头起 AA 并加载 Azurite"**（当前卡点，`AZURITE-TAKEOVER.md` §5.1）。以下每一条都依赖它。

| # | 依赖 | 在 AA 里要做的操作 | 看到什么算通过 | 当前阻塞 |
| --- | --- | --- | --- | --- |
| **A0** | — | 无头启动 AA + 加载 Azurite（Original 模式先，AuthoringSource 后） | 进程存活 ≥ N 秒；BepInEx `LogOutput.log` 里出现 `Azurite <version>` 且无异常；`AzuriteDriver` 完成 Tick/LateTick/GuiTick 绑定 | ❌ **当前卡在这里**：BepInEx/IL2CPP 预加载异常 |
| **A1** | A0 | 打开普通编辑器 Catalog → 开一个样例工程 | `NormalRoutes` 的 7 类路由全部发布；`catalog-ready` 真实落盘；`RequireCatalogClosure` 不再 throw | ❌ 路由未齐（6+1 中的 cache 类没通） |
| **A2** | A1 | 一次 `OpenProject` 打开样例 `.aap2` | 真实 `StudioCommon.Start` 完成；`AuthoringEditorSession.Current` 非空且非 disposed；读出样例的 ProjectId/dialogue node/LineId 三个已知 ID（`b394b423…`/`213c0bc5…`/`26cf5110…`） | ❌ 缺 opener 驱动 |
| **A3** | A2 | 记录真实 catalog：枚举 transition/sound/action/reaction 的实际 native key | 拿到真实 key → uint/string 映射表，能填进 `NativePresetResourceCatalog` | ❌ 五类卡包的**总前置** |
| **A4** | A3 | **批量文本**：选 2 行 → 改文本 → 确认 | `Check`+一次 `Apply` 返回非空 receipt；`OperationManager.saveDelta` 只 +1；历史里出现**一个**快照单元；行持久 ID 不变 | ⏳ 源码就绪 |
| **A5** | A4 | 对 A4 那笔编辑做 **Undo → Redo** | Undo 后 fingerprint 回到编辑前；Redo 回到编辑后；history/future 计数正确 | ⏳ 零验证 |
| **A6** | A5 | **Save → 关闭 → 重开** | 磁盘文件 hash 变化；重开后 project/node/line 数据保留（尤其是 A4 改的文本） | ⏳ 零验证 |
| **A7** | A4 | 批量**删除** ≥2 行 | 行数减少且 ID 不重用；节点与连接保留 | ⏳ 源码就绪 |
| **A8** | A4 | **多行插入** 3 行 | 3 个新行紧跟在锚点后；退场角色/一次性动作按原生 previous 规则被清理 | ⏳ 源码就绪 |
| **A9** | A4 | 批量**复制**（分别测 CopyVoice=true/false） | true：配音一并复制；false：新行 `voice.action="none"` 且无 resource_id | ⏳ 源码就绪 |
| **A10** | A4 | 批量**发言槽位** | 仅切换已有对白行的槽位；被改行的 `characters[0]` 重置为宿主默认 | ⏳ 源码就绪 |
| **A11** | A3 | **转场卡**插入 | 真实 `transition` 字段变成目标 uint；编辑器里看到实际转场；保存后重开仍有 | ❌ 缺 catalog |
| **A12** | A3 | **音效卡**插入 | `scene.sound` 设成真实 key；**能听到声音**（内部采样或人工确认）；不是静音 | ❌ 缺 catalog + 缺 audio 通道 |
| **A13** | A3 | **战斗卡**插入 | reaction（Stiff/Shake/FalldownL/R）+ Shot 效果 + 音效三者同时发生 | ❌ 缺 catalog + 缺 reaction 枚举 |
| **A14** | A3 | **动作卡**插入 | 目标 slot 角色做出指定 action；face/emoticon 联动正确 | ❌ 缺 catalog + 缺 action 枚举 |
| **A15** | A3 | **语言卡**插入 | 目标 slot 出现指定 locale 的文本变体；切语言能读到 | ❌ 缺 catalog + 缺 locale 真值 |
| **A16** | A4 | 批量工具栏**真实点击**：launcher → 预览 → 确认 / 取消 / 恢复 | 预览文本与最终结果一致；取消不改；确认后 token 失效不可重用 | ⏳ 全 stub |
| **A17** | A4 | **失败/取消/未知结果**路径 | 强制 Apply 抛异常后：状态进 Uncertainty，再 preview 被拒，Recover 能证明"未写"或"已写"之一 | ⏳ stub only |
| **A18** | A0 | **焦点与守卫**：切焦点、输入法、拖拽、导出进行中 | 每个 guard 都返回真实拒绝原因；导出期间创作全关 | ⏳ 私有桌面 focused 值未知 |
| **A19** | — | AAVE `RenderControlV1/v1` 绑定 + start/cancel/complete 生命周期 | 主线程 acquire/release 顺序正确；FPS/VSync 恢复；**v1 协议不变** | ⏳ 独立门 |
| **A20** | A0-A19 | 性能项逐项对照（5 轮开/关） | 收益超过重复测量波动，其它指标退化 ≤5%；否则不默认启用 | ⏳ 全部开关默认关 |

**排序理由**：A0 是一切的总闸门；A1/A2 决定能否拿到真实会话；A3 是五类卡包的唯一前置；A4 是 6 个批量检查点共用的接缝，先验它性价比最高；A5/A6 决定"改完能不能持久"；A11-A15 五族可以并行；A16-A18 是 UI 与守卫层；A19/A20 独立收口。

---

## 9. 给 lead 的建议（按优先级）

1. **先把 e8dddb9 的 Tick 顺序回归定案**。要么改代码，要么改断言 + 更新文档。在这之前，不要把 e8dddb9 当"10/10 检查点全绿"对外表述。
2. **把第 4.1 节的 11 个 csproj 修复单独提交**。它们是 artifacts 清理后的必要重建接缝，现在裸在工作树里，一次误操作就没了。
3. **把 §5.A 的 7 个 `NormalEditor.Loader*` 项目纳入版本控制**。这是 §6 P1 的成果，也是普通编辑器入口的唯一实现。
4. **处理 `CacheBindingRules.CoreSha256` 这类字节钉子**：改成钉 tracked 源码树，或把该二进制连同来源收据纳入版本控制。否则"干净副本复跑"永远给出误导信号。
5. **`FullRestricted.*` 的 35 个目录和 `.team-board/` 明确排除出提交范围**，但**保留在本地作为失败证据**（符合"失败证据一律保留"的原则）。
6. **真实 AA 验收从 A0 开始，不要跳步**。五类卡包再漂亮，A0/A1/A3 不通就永远是源码级玩具。

---

## 10. 本次审计的边界（必须说明）

- **没有启动 AA**，没有 CDB/debugger，没有 native 交互，没有焦点抢占，没有系统键鼠注入。
- **没有修改原工作树任何文件**；没有 commit / stash / reset / clean。
- **没有修改 `F:\AzureArchive_101`**（只读它作为 `AAInstallPath` 编译输入）。
- **没有 push / 发布 / 安装**。
- `Test-Source.ps1 -Suite Host`（DriverTests/SelectorTests/LoadingIntegrationTests/CpuTests 4 个）**未运行**——它们需要真实 AA interop 并且会触及宿主行为，超出本任务的只读离线边界。**因此 `e8dddb9` 的 SelectorTests（75+36 项）状态为"未验证"，不是"通过"。**
- `Test-SourceStagingPipeline.ps1` / `Test-SourceStagingReviewRegressions.ps1` **未复跑**（需要 producer 生成的 build receipt + fresh artifacts 目录）。0d9b889 这条检查点的"33 portable + 29 pipeline + 25 review"只有 Portable 部分被本次独立复现。
- `.team-board/board.db` 一致性**未核实**，按要求未打开写。

---

## 附：产物清单

| 文件 | 内容 |
| --- | --- |
| `REPORT.md` | 本报告 |
| `portable-results.json` | 32 个 Portable job 的退出码/耗时/摘要行 |
| `logs/<Project>.log` | 每个 job 的完整输出（32 个） |
| `evidence-git-status.txt` | `git status --porcelain -uall` 原始输出（496 行） |
| `evidence-git-log.txt` | 最近 12 个提交 |
| `evidence-checkpoint-diffs.txt` | 10 个检查点的 `git show --stat` |
| `evidence-modified-files.txt` | 22 个已修改文件清单 |
| `evidence-untracked-files.txt` | 474 个未跟踪文件完整清单 |

干净复跑副本：`E:\aamod\tmp-azurite-takeover\authoring-copy`（16 MB，1501 文件，无 .git/.superpowers/.team-board/bin/obj）
父提交对照副本：`E:\aamod\tmp-azurite-takeover\parent-copy`（`git archive 0d9b889` 解出，6.2 MB）

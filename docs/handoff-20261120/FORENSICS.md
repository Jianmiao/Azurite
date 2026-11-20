# Azurite 无头启动 / 进入普通编辑器 取证报告

- 任务：task-3「只读取证：分析现有 AA 无头启动失败的全部日志与现场（不启动 AA）」
- 取证者：forensics（DSH 子代理）
- 日期：2026-11-20
- 边界：**纯只读**。未启动任何 AA 进程，未修改 F:\AzureArchive_101，未 commit/stash/reset/clean，未写 tmp-azurite-takeover 之外的任何路径。
- **本报告推翻了交接备忘的前提，请优先读第 0 节。**

---

## 0. 执行摘要：三条必须传播出去的结论

### 结论 A：BepInEx 加载器早已跑通，Azurite 插件完整生命周期也已跑通

不是「卡在加载器早期启动异常」。铁证（本次实测哈希）：

| 项目 | 值 |
| --- | --- |
| 日志 | E:\aamod\labs\Azurite-20261108-stable\runs\restricted-5a3cebee4fa3440bb368187d536967ad\output\bepinex.LogOutput.log |
| SHA-256 | 5a0e2bb16e5de1d7d718aa30d66cd5e9b2a7d594f960c9240d7da67c848b3cd3 |
| 大小 | 3629 字节 |

该日志完整包含（逐行核实）：
Chainloader initialized → Loading [Azurite · 蓝铜矿 1.0.38] → Azurite generated binding contract verified: 409 type references, 1465 member references → AA host identity verified: 1.0.1 → Chainloader startup complete → Published 1 successfully loaded mod(s) to ModConfig。

同目录 controller-result.json：bootstrapExitCode=0、passed=true、windowViolation=false、cleanup.verified=true、remainingOwnedProcesses=0、aaExitCode=0。

**⇒ 任何「加载器起不来」的说法都不成立，应从所有后续计划中删除。**

### 结论 B：止步 CatalogScene 是三层原因叠加，其中最关键的一层是「测量假象」

1. **AA 自己的设计**：该次运行是 {"argv":["capabilities"]}（见 request/arguments.json）。capabilities 是**资源无关的 CLI 查询**，AuthoringCliHost.IsActive=true 会让 AuthoringWorkbench.Bootstrap 提前返回，根本不走 资源初始化 → CatalogScene → ScenarioEditorScene 这条正常链路。该次 unity.log 里**没有任何 CatalogScene / ScenarioEditorScene 字样**，直接进入内存分配器 dump 后退出。
2. **探针本身只采 2 个样本就停**（见 2.2 节）。**这是最重要的发现**：「studio=none; session=none」不是「没进编辑器」的证据，而是**探针在 N=2 就停止采样**这一设计的必然输出。
3. **真正的「进入编辑器」驱动根本没有实现**（见 3 节 H2）。EditorEntryStage.cs、EditorEntryCases.cs、Loader CompilerAttributes.cs 三个文件**当前不存在**；生产源码中**零处**调用 OpenProject，测试源码甚至**明令禁止**调用它。

### 结论 C：令牌 / COM 那条线是「受限启动实现自找的复杂度」，不是「进不了编辑器」的原因

- 真实的 AA 早期 COM 异常是 System.Runtime.InteropServices.COMException: 试图引用不存在的令牌。(0x800703F0)，出现在 **restricted-token 运行 85ab**（AA PID 34320，TID 42996，相对时刻 74.2489ms）。
- 根因链已在工作树内被完整测出：受限 STA 下 CoWaitForMultipleHandles 返回 0x8007000E / LastError 1008，托管侧把它转成 0x800703F0，落在 Doorstop 的 Global mutex WaitOne 上。
- 给 CoInitializeSecurity 加 LocalService（A/B 两套描述符，各 3 ACE / 4 ACE）**都没解决**：两套等待矩阵都停在 8/12。
- **日常 GUI（不受限）在同一台机器上完全正常。**

⇒ **用户的硬约束（不弹窗、不抢焦点、不注入全局键鼠、最多 1 实例、低优先级、音频不走向日常输出）没有一条要求 restricted token。** -batchmode -nographics -disableaudio + 独立 private desktop + Job 限 1 已经满足全部约束。restricted token 是 Codex 自行追加的额外隔离目标，正是它把 8 天耗掉的。

---

## 1. 时间线（每次已知运行的客观记录）

| # | 时间(本地) | runId | 命令 / 模式 | 结果 | 最后状态 | 证据位置 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 2026-11-08 10:49 | b3231d471fa74fbaa9b297a11efce707 | capabilities，CurrentAccount | **失败** bootstrapExitCode=1，passed=false，requiresExplicitRecovery=true | pinned input drift: halocue.azurite.cfg 与 contract 不符 | E:\aamod\labs\Azurite-20261108-stable\runs\restricted-b3231d47... |
| 2 | 2026-11-08 11:29 | 5a3cebee4fa3440bb368187d536967ad | capabilities，CurrentAccount | **成功** passed=true，aaExitCode=0 | Azurite scheduling disabled for this session: driver destroyed | 同上 restricted-5a3cebee... |
| 3 | 2026-11-08 11:32 | a3dfb0bd97094215a7105b4bbf8ed6c2 | schema，CurrentAccount | **成功** passed=true，responseBytes=128700 | 同上 | 同上 restricted-a3dfb0bd... |
| 4 | 2026-11-08 16:28 | 554d1fd1d6084c8ead7b247847479ca8 | FR1 原生 fixture（非 AA） | **成功一次** 21 条 receipt，exit 0，aaAdmission=false | n/a | SDD full-restriction-fr1-runtime-proof.md |
| 5 | 2026-11-08 23:15 | 0f29b21a15194a62890d797477c7ebfe | WU1 完整性覆盖层 fixture | **失败** exit 30 | receipt#6 overall_pass=false | SDD wu1-runtime-failure.md |
| 6 | 2026-11-12 10:49 | 777d996a0fca4add8d633cdc87c7ba9f | normal fix1 | **失败** launcher exit 1 | Private desktop window enumeration failed | SDD aa-fr-native-normal-fix1-runtime-failure.md |
| 7 | 2026-11-12 13:18 | 022de7b6af1f4f35be3860d906306a36 | monitor1 normal | **失败** exit 1 | Pinned empty-desktop implementation unavailable | SDD aa-fr-native-monitor1-runtime-failure.md |
| 8 | 2026-11-13 00:52 | 85ab… (R3df / 85ab) | AA startup diagnostic | **失败** AA exit -532462766 (0xE0434352) | Unity log 记 Doorstop fallback preloader log 写入被拒 | SDD aa-85ab-actual-diagnostic-c-review-20261113.md |
| 9 | 2026-11-13 | 250c044d62ab4b92ad82a830bb9b47c5 | full-capabilities runtime | **失败** bootstrap exit 1 | Utf16StringValidator TypeLoadException | SDD aa-fr-actual-runtime-250c-monitor-failure-audit.md |
| 10 | 2026-11-15 09:39 | 1ebaf8382f8c4424907a9150df495b46 | full-capabilities | **未完成** | state = recovery-in-progress；runDirectory **不存在** | F:\AzuriteLabs\Editor-20261108-normal\runs\full-capabilities.pending.json |

**第 10 条的注意事项**：pending.json（459B，2026-11-15 09:39）声明的 runDirectory 指向 F:\AzuriteLabs\Editor-20261108-normal\runs\restricted-1ebaf8382f8c4424907a9150df495b46 —— **实测不存在**（ls 报 No such file or directory）。同级的 secrets 目录也不存在。**这是孤立残留标记，不是一次有现场的运行。** task-1 重建时**不需要恢复它**，但必须显式处理（见 6 节步骤 0）。

---

## 2. 差异分析：正常启动 vs「失败」运行

### 2.1 关键澄清：什么才是「正常」

| 场景 | 有无 Azurite 日志 | 有无探针行 | 到达的场景 | 是否进编辑器 |
| --- | --- | --- | --- | --- |
| 日常 GUI（F:\AzureArchive_101，Azurite **1.0.39**，Profile=Default，14 mods） | 有，完整 | **有**（仅 1.0.39 含该字符串） | Loading → CatalogScene（每次启动都如此，2026-11-20 22:14 实测复现） | 用户可以手动点 Edit |
| E 盘 lab（**1.0.38**）capabilities CLI | 有，完整 | **无**（1.0.38 DLL 不含该字符串） | 只有 Loading 雏形即退出 | 设计上就不进 |
| F 盘 lab（**1.0.38**） | 有，被截断 | 无 | 未观测 | 事实上从未 |

**实测：native authoring probe 字符串只存在于 F:\AzureArchive_101\mods\Azurite\1.0.39\Azurite.dll（UTF-16 编码）；1.0.37 / 1.0.38 的 Azurite.dll 与全部 Azurite.Core.dll 均不含。**

⇒ **lead 引用探针输出的那条证据，来源是「用户日常 GUI 里那次真实正常启动」，不是任何无头 lab 运行。** 这反而更有价值：它证明**日常 GUI 里 AA 在采样时刻也只停在 CatalogScene**——因为用户还没点开工程。

### 2.2 探针只采 2 个样本：决定性测量假象

F:\AzuriteLabs\Editor-20261108-normal\install\BepInEx\LogOutput\AzuriteNativeAuthoringProbe 现场：

- samples.ndjson：**40 行 = 20 次会话 × 2 样本**。场景分布：Loading 19 次、CatalogScene 21 次。含 Studio 非空对象的行数：**0**。
- 每次会话严格是 sample=1 scene=Loading 紧跟 sample=2 scene=CatalogScene，间隔 0.5–5 秒，之后**再无 sample=3**（2026-10-25 至 2026-11-02 约 20 次真实会话里从未出现 sample ≥ 3）。
- latest.json 同样只有 SceneName=CatalogScene、Studio=null、Session=null，且 ReadOnly=true、MutationAttempted=false。

**⇒ 探针是 2 发即停的只读采样器。它永远不可能观测到 Studio / Session，哪怕编辑器已经打开。** 任何「探针显示 studio=none 所以没进编辑器」的推论都是无效推理。

### 2.3 一次 capabilities 运行的真实生命周期（unity.log）

restricted-5a3cebee.../output/unity.log（18301B）：
Initialize engine version: 2023.2.22f1 → Forcing GfxDevice: Null → 大量「Shader ... not supported on this GPU」（Null 设备下必然）→ **然后直接进入 ALLOC_* 内存分配器峰值 dump**。

**没有 CatalogScene 加载、没有 ScenarioEditorScene、没有 AuthoringWorkbench 的 native bootstrap 副作用。** 与结论 B-1 一致：capabilities 是资源无关查询，这就是设计。

---

## 3. 根因假设清单

> 每条给出：支持证据 / 反对证据 / 最小证实-证伪实验。
> **H1–H4 回答「为什么没进编辑器」；H5–H6 回答「受限启动为什么会自己失败」。**

### H1（高置信，直接可行动）— AA 原生 mcp.start 是没被用过的、官方支持的进编辑器入口

**主张**：AA 自带 CLI 命令 mcp.start，可直接「在编辑器为空时加载指定 .aap2」。Codex 从未把它当作进编辑器的手段，只把它当 schema 校验对象。这可能是**绕开全部逆向 Harmony 补丁工作的最短路径**。

**支持证据**（全部取自真实运行产出的 response.json：E:\aamod\labs\Azurite-20261108-stable\runs\restricted-a3dfb0bd97094215a7105b4bbf8ed6c2\request\response.json，SHA B97D7BB5…）：

- data.cli_commands["mcp.start"].description 原文：
  > Start MCP in one existing GUI process. Idempotent while running: retain credentials and edit_session. **With project and no currently open project, load that .aap2 or legacy .aap project without a format-conversion prompt**; an already open different project is never replaced.
- 入参 schema：pid（integer，可选）+ project（string，可选，.aap2 或 .aap 路径）。
  > Optional .aap2 or .aap path: guard an open project, **or load it only when no project is open**.
- data.capabilities.cli_mcp_control：
  commands = [mcp.start, mcp.status, mcp.stop]；
  transport = current_user_named_pipe；
  instance_selection = optional_pid_required_if_multiple_gui_instances；
  no_instance_error = EDITOR_NOT_READY；
  multiple_instances_error = EDITOR_AMBIGUOUS；
  start_idempotent = true；
  **start_project_behavior = load_project_if_editor_empty_otherwise_guard**；
  status_stop_project_behavior = guard_only；
  returns_bearer_token = false。
- 语料内 mcp.start 仅出现在两处，**都不是实际调用**：
  · native-schema-authoring-check.md:24（「/data/cli_commands 是 map，纠正旧的 data.commands 假设」）
  · tools/headless/RestrictedCliCore.psm1:96（仅断言名字存在）

**反对证据 / 风险**：

- 描述写的是「**one existing GUI process**」，走 current_user_named_pipe，发现目录是 LocalApplicationData\AzureArchive\automation\editors。**-batchmode -nographics 的 AA 是否会创建并注册该 control server 尚未证实。**
- AuthoringWorkbench.Bootstrap（token 100695326 / RVA 0x94fc90）在 CLI 激活路径上**提前返回**，不创建 AuthoringControlServer。所以带 AZUREARCHIVE_CLI_DIRECTORY 启动的进程一定没有 pipe。
- 因此该路线要求：**第一个 AA 进程必须以「无 CLI transport」的方式启动**（这样 AuthoringCliHost.IsActive=false，走正常 Bootstrap），第二个 AA 进程带 CLI transport 发 mcp.start --project X。

**最小实验**：见 6 节实验 E1（第二优先）。

### H2（高置信）— 「进入编辑器」驱动源码根本不存在，且被测试规则明令禁止

**主张**：不是「驱动跑失败了」，而是**驱动压根没写**。当前工作树是「观察器」架构，观察器被设计为**不得**驱动原生生命周期。

**支持证据**：

- rebuild-20261115\entry-recovery\current-source-presence.json 实测（2026-11-15 快照）：
  · NormalEditor.Entry.Core/EditorEntryStage.cs → exists: **false**
  · NormalEditor.Loader/CompilerAttributes.cs → exists: **false**
  · NormalEditor.Loader.Tests/EditorEntryCases.cs → exists: **false**
  本次实测复核：这三个文件**至今仍不存在**。
- 全工作树 grep OpenProject：**生产源码 0 处命中**；仅 3 处命中且全在**测试**里，语义是「禁止」：
  · NormalEditor.Entry.Shutdown.Tests/ShutdownHostMetadataChecks.cs:51 →
    Need(!allCalls.Any(m => m.Name is "Quit" or "Seal" or "Unpatch" or "UnpatchAll" or "OpenProject" or "Begin"), "shutdown drove native lifecycle/terminal/patch teardown")
  · NormalEditor.Entry.Tests/MetadataChecks.cs:51 →
    check(!callsites.Any(m => m.Name is "OpenProject" or "OpenControlled" or "Begin" or "StartCoroutine" or "LoadSceneAsync" or "il2cpp_runtime_invoke" or "il2cpp_object_new" or "il2cpp_gc_wbarrier_set_field" ...), "Forbidden lifecycle/entry/native write call")
  · NormalEditor.Loader.Routes.Tests/MetadataChecks.cs:66 →
    need(!calls.Any(m => m.Name is "Initialize" or "OpenProject" or "Begin" or "CreateAndApply" or "il2cpp_object_new"), "Typed consumer adapter cannot drive native lifecycle or claim hook installation.")
- AcceptanceHarness\NormalEditor.Entry.Host\CatalogEntryHost.cs（114 行）文件头注释原文：
  > No plugin/automatic registration or permitting lease. **A separately admitted integration may register this observer; it never drives Start/Begin/Update.**
  其全部行为是对 Catalog.Start 与 AuthoringWorkbench.Update 打 Harmony prefix/postfix/finalizer **只读观察**，并在 Observe() 里读 AuthoringEditorSession._Current_k__BackingField 等字段。注册是单次性的：
  state is not null 时直接 throw new InvalidOperationException("Catalog observer registration is single-use.")
- AcceptanceHarness\NormalEditor.Loader\NormalLoaderPlugin.cs（73 行）里 Load() 只做：取 Guard session → 建 NormalEntryAdmission → RequirePreNativeInstallation → 建 CatalogEvidenceWriter → new NormalLoaderLifetime(...) → created.Register()。**没有一行触发场景切换或打开工程。**

**反对证据**：无。这是源码事实。

**最小实验**：不需要跑 AA。CatalogEntryHost 提供的全部 API 中没有一个能改变场景。

### H3（中高置信）— Addressables 远程目录必须可达，否则资源初始化失败 → 永远到不了 Catalog 就绪

**主张**：正常（非 CLI）启动必须完成 ScenarioResourceManager.Initialize → Addressables 目录加载。目录 hash 走**远程 HTTP**，本机是 http://127.0.0.1:27510/aares/win/catalog_2024.08.17.15.07.45.hash。若该本地资源服务器未起，就是 502 Bad Gateway，资源初始化失败，编辑器不可达。

**支持证据**：

- F:\AzuriteLabs\Editor-20261108-normal\install\BepInEx\LogOutput\AzuriteNativeAuthoringProbe\player.log（114693B）原文：
  Exception raised in Addressables:
  RemoteProviderException : TextDataProvider : unable to load from url :
  http://127.0.0.1:27510/aares/win/catalog_2024.08.17.15.07.45.hash
  UnityWebRequest result : ProtocolError : HTTP/1.1 502 Bad Gateway
  并伴随 ScenarioResourceManager:OnAddressablesExceptionRaised、CheckCatalogsOperation failed。
- F:\AzureArchive_101\AzureArchive_Data\StreamingAssets\aa\settings.json：
  m_InternalId = {ScenarioResourceManager.AddrLoadUrl}/catalog_2024.08.17.15.07.45.hash
  m_Keys = [AddressablesMainContentCatalogRemoteHash]
- SDD normal-editor-current-account-next-gate.md：内置 catalog 有 **13,085 个唯一 .bundle 路径**；--aap-offline-resources 的 ValidateDirectory 会逐个 File.Exists 校验；缺失即拒绝（且拒绝会走 error popup，无头下更要命）。

**反对证据**：

- 那次 502 是 2026-11-02（从 F 盘 lab 复制的 player.log），当时是 F:\AzureArchive_101 的真实 GUI 会话，资源服务器可能有也可能没有。
- 本次 capabilities 运行根本没走资源初始化，所以这个失败**不在** capabilities 路径上。
- 日常 GUI 现在能正常工作 ⇒ 该服务器在用户正常使用时会起来（大概率由 AA 本体或某 mod 拉起，端口 27510）。

**最小实验**：无头启动前先探测该 URL 是否 200；若 502 / 拒连，判定「正常编辑器路径不可达」，必须改用 --aap-offline-resources <完整离线目录>。**但离线目录需要 13,085 个 bundle 全部到位**，这是真实工作量，要单列任务。

### H4（中置信）— Settings / Workspace / Cache 路由未建立，正常启动会写用户真实目录

**主张**：正常（非 CLI）启动会读写 Application.persistentDataPath\data\settings\user_settings.json、data.visitedFiles、CachePath（日常是 E:\AAcache）、WorkspacePath、LocalLow\foxxlight\AzureArchive、HKCU，以及 LocalApplicationData\AzureArchive\automation\editors 的 named pipe + 发现 JSON。**未做路由就等于污染用户日常状态**，违反用户的隔离约束。

**支持证据**：SDD normal-editor-current-account-next-gate.md 给出了逐 RVA 的调用链：
UserSettings.Awake (100689258 / 0x7f5710) → <InitTask>d__36.MoveNext (100689374 / 0x7f1f60) → <ReadUserSettings>d__43.MoveNext (100689386 / 0x7f24e0)；
并明确指出 get_CachePath (100689255 / 0x7f6a20) **总是**重算 persistentDataPath，改 settingFilePath / data.cachePath **不足以**重定向 cache。

**反对证据**：AcceptanceHarness\NormalEditor.Policy\NormalSettingsLifecycle.cs 与 SettingsSnapshot.cs 已存在，说明这部分有源码基础，不是从零。

**最小实验**：不需要实验——先做基线。完整快照 C:\Users\21294\AppData\LocalLow\foxxlight\AzureArchive、...\data\settings\user_settings.json、C:\Users\21294\AppData\Local\AzureArchive\automation\editors、HKCU Software\foxxlight\AzureArchive。跑完后逐项 diff = 0 才符合隔离要求。

### H5（已基本坐实，属「已知复杂」而非「根因」）— restricted token 让 CLR 自身的 COM wait 失败

**主张**：restricted token（flags 1，restricting SIDs = 实际 Logon SID + World + Users）下，显式 STA 线程的 CoWaitForMultipleHandles 返回 0x8007000E、LastError=1008；.NET 6 把它包成 COMException 0x800703F0；该异常落在 Doorstop 的 Global mutex WaitOne 上，导致 CLR preloader 起不来。

**支持证据**：

- 实际 trace（85ab，2071B，SHA ddc995d9a8f08fe8f79e32af582b3bd3e981d43223978aacc26d0e0678302a11）：
  ExceptionStart ID80, PID34320, thread42996, 74.2489ms, System.Runtime.InteropServices.COMException, 「试图引用不存在的令牌。(0x800703F0)」；
  raw UTF16 同时含 WaitHandle.WaitOneNoCheck、Doorstop.Entrypoint.Start、二次 Unauthorized。
- wait1 受限实测（run de305633…，result SHA b40f864d…）：
  自然 MTA 6/6 成功；显式 STA 仅 raw 2/6 成功；CoWait 两次 0x8007000E / 1008；
  托管 seed1008 → 0x800703F0，seed0 → 0x80004005。
- wait4 A/B（加 LocalService）：A（3 ACE）与 B（4 ACE）都 CoInitializeSecurity → S_OK，但**矩阵都还是 8/12**。

**反对证据**：

- 日常 GUI（非受限）完全正常。
- .NET 6.0.7 的 CoWait 具体路径未在同字节 runtime 下复现过（C 盘只有 6.0.36）。

**最小实验**：**不要修它，绕开它。** 见 6 节实验 E0。

### H6（低置信，仅登记）— bcrypt / KsecDD 设备访问在受限下失败

**主张**：受限下 LoadLibraryExW("bcrypt.dll") 返回 error 1114 ERROR_DLL_INIT_FAILED，静态分析指向 \Device\KsecDD 需要 0x00100003（含 FILE_WRITE_DATA）。

**支持**：WU1 run 0f29b21a… receipt#2–3 KsecDD 0xC0000022、#4–5 bcrypt error 1114、#6 exit 30。
**反对**：FR1 run 554d1fd1… 在**同一** flags-1 + Logon/World/Users 策略下 KsecDD 返回 0，且 bcrypt 七导出全部通过。⇒ 说明这不是 flags-1 的必然结果，可能是 IL / 完整性覆盖层差异。
**最小实验**：不在当前关键路径上。若走 H1 / E0 路线（unrestricted）则完全不相干。

---

## 4. 已知不可行 / 已排除路线（避免重复踩坑）

| # | 已试路线 | 为什么失败 | 证据 |
| --- | --- | --- | --- |
| X1 | 放宽读权限换取删除权限（integrity overlay） | WU1 覆盖层分支被明确**终止**：安全继续后 bcrypt 1114 直接短路 exit 30 | wu1-runtime-failure.md：「Stop further integrity-overlay trials and fallbacks」 |
| X2 | 允许写错误报告来拿首个异常 | Doorstop 的 fallback preloader log 写入被拒（Unauthorized），随后 Unity log 记录该拒写；**首个真实异常反而被后续异常掩盖** | aa-85ab-actual-diagnostic-c-review-20261113.md |
| X3 | 低权限 / 受限诊断探针 | 「Class::Init signatures have been exhausted」这类 Il2CppInterop 降级在其自身日志里可见，但不等价于失败根因；受限下更多是 H6 的加载失败 | LogOutput.2.log（6933B） |
| X4 | 用 CoInitializeSecurity 加 LocalService 修 COM | A/B 两套绝对描述符都 S_OK，但等待矩阵都仍是 8/12 | aa-wait4-processlocal-com-boundary-review-20261113.md |
| X5 | 直接跑 EditorRoute* 作为「进编辑器」手段 | **它不是编辑器驱动**。README 首句：It does not implement a normal Editor or graphical acceptance run. 只做 Positive / ProbeMissing / InvalidManifest 三种 CLI 路由自测，且「Native command is always capabilities」 | tools\headless\EditorRoute.README.md |
| X6 | 用探针输出判定「是否进了编辑器」 | 探针 2 发即停（2.2 节），永远看不到 Studio / Session | samples.ndjson 40 行 = 20×2 |
| X7 | 假设旧 lab 可恢复 | pending.json 指向的 runDirectory **不存在**；旧 recovery 执行句柄 48795 已 Unknown；artifacts 406 目录已删 | 1 节 #10；current-handoff.md |
| X8 | 用 1.0.38 复现探针输出 | 1.0.38 的 Azurite.dll **不含** native authoring probe 字符串（只有 1.0.39 有） | 2.1 节实测 |

---

## 5. 工具源码可复用性清点

### 5.1 可直接复用（当前工作树 tools\headless，共 50 个文件）

| 文件 | 大小 | 复用结论 |
| --- | --- | --- |
| Invoke-AzuriteRestrictedCli.ps1 | 24914 | ✅ **核心可复用**：全局锁、无 AA / 隔离闸门、单次 run 所有权、prepared hash、response / log 校验、drain 后状态检查。要改的只是「启动策略分支」 |
| RestrictedCliBootstrap.ps1 | 10345 | ✅ 可复用：原用户 bootstrap 身份 + child 前 Job 握手 |
| RestrictedCliCore.psm1 | 30719 | ✅ 可复用：bounded JSON、response / schema 校验、时间戳规范化、prepared pins、进程错误判定、quarantine |
| CurrentAccountCli.psm1 | 25266 | ✅ 可复用：真实 profile / KnownFolder 校验、bounded 前后状态快照 |
| RestrictedCliNative.cs | 17369 | ✅ **核心可复用**：ProcessPump.Run / Execute、OwnedJob、JobObserver、WindowProbe。**建议：保留 Job / desktop / stream / deadline 监控，把 token 构造换成原生 token** |
| RestrictedCliFullRestricted.cs | 103405 | ⚠️ 可复用但**含已知缺陷**：JavaScriptSerializer.DeserializeObject 在 monitor finally 里抛 Utf16StringValidator TypeLoadException（run 250c 就死在这）。要用必须改结构化反序列化 |
| headless\restricted-startup-contract.json | 117146 | ✅ 451 个文件 pin，可作只读基线参考 |
| Initialize-AzuriteTestAccount.ps1 | 10845 | ⚠️ 存在但**从未成功**：preparation-result.json 里 accountReady=false、accountCreationAttempted=false |
| HeadlessCore.psm1 | 39574 | ✅ 通用无头基础设施，可复用 |
| New-AzuriteFullCapabilitiesPlan.ps1 / FullRestrictedCapabilitiesPlan.psm1 | 3078 / 73844 | ⚠️ 仅当继续走 full-restriction 路线才需要；**建议暂时搁置** |
| FullRestrictedStageTransition.psm1 | 88889 | ⚠️ 同上，搁置 |
| EditorRoute*（14 个，约 120KB） | — | ❌ **不要用于进编辑器**（X5）；但 EditorRoute.Quiescence*.ps1 / EditorRoute.JobBoundaryFixture.cs.txt 的进程边界测试思路可借鉴 |

### 5.2 已删除、必须重写

依据 rebuild-20261115\entry-recovery\RECOVERY-REPORT.md + current-source-presence.json + 本次实测：

| 文件 | 状态 | 结论 |
| --- | --- | --- |
| AcceptanceHarness\NormalEditor.Entry.Core\EditorEntryStage.cs | **不存在**（exists:false） | ❌ 必须重写。历史 source2 身份：15140 bytes，SHA 83ef6542c4b1f77df06cf834057982688557b421533b7631b86c2f7462333e71（**只有身份，没有字节**） |
| AcceptanceHarness\NormalEditor.Loader.Tests\EditorEntryCases.cs | **不存在** | ❌ 必须重写 |
| AcceptanceHarness\NormalEditor.Loader\CompilerAttributes.cs | **不存在** | ❌ 必须重写（可参照同类 NormalEditor.Entry.Host\CompilerAttributes.cs 与 NormalEditor.Loader.Routes\CompilerAttributes.cs，二者都还在） |
| AcceptanceHarness\NormalEditor.Loader.Core\NormalReadOnlyShutdownDriver.cs | ✅ **已按字节恢复**，6098B，SHA dcaed1690158467fa767796850296d3cab19e0e13ae3ef9165fc365a16b15b42 | 位于 rebuild-20261115\entry-recovery\recovered-source，可直接取用 |
| AcceptanceHarness\NormalEditor.Loader.Core\NormalPreparedLaunchPlan.cs | ✅ **已按解码文本恢复**，3054B，SHA cf6338bb74215a7e4c094943bc5a8db11447ea1f20ff15c61c511c39dedee4d3 | 同上。**注意其 RuntimeAllowed => false，blockers = [CacheQualificationMissing, CurrentAccountNormalLaunchQualificationMissing, NormalExitQualificationMissing]** |

### 5.3 存在但从未被安装 / 使用

- AcceptanceHarness\NormalEditor.Loader（NormalLoaderPlugin.cs + csproj）—— 插件本体在，**但三个 lab 的 BepInEx\plugins 里都没有它**（E-stable、E-editor、F-normal 实测均无 Azurite.NormalEditor.* DLL）。
- AcceptanceHarness\EditorIsolation.*（Guard / Policy / Probe / HostBindingTests / Tests）—— **只装在 E:\aamod\labs\Azurite-20261108-editor\install\BepInEx\{plugins,patchers}\AzuriteEditorIsolation**，另外两个 lab 没有。

---

## 6. 给 task-2 的最小实验设计（按此顺序做）

> **前置**：每个实验用独立 runId + D:\AzuriteEvidence 持久目录；同一时间只允许一个 AA 实例；启动前确认无日常 AA 进程在跑。
> **统一红线**：不写 F:\AzureArchive_101；不覆盖 F:\AzuriteLabs\Editor-20261108-normal；任何失败都保全现场，不重试到通过。

### 步骤 0（必做，5 分钟）— 处理孤立 pending 标记

F:\AzuriteLabs\Editor-20261108-normal\runs\full-capabilities.pending.json 的 runDirectory **不存在**，secrets **不存在**。这是一个**没有现场的空标记**。

**动作**：把它原样**复制**到 D:\AzuriteEvidence\<date>-preflight 并记哈希，然后**不删不改**；在 task-1 的新 lab 里完全忽略它（新 lab 是全新副本，本来也不会继承）。若日后要清，必须先书面确认「该 runId 从未产生任何 run 目录」。

### 实验 E0（最高优先级）— 证明「不受限 token + 无头」能不能正常启动到 Catalog 就绪

**目的**：一击证伪 / 证实结论 C 的判断——restricted token 是不是纯粹在自找麻烦。

**做法**：在隔离副本里执行
  AzureArchive.exe -batchmode -nographics -disableaudio -logFile <run>\output\unity.log
**不带** AZUREARCHIVE_CLI_DIRECTORY、**不换 token**。

**对照**：与 capabilities 那次（有 CLI 变量）的 unity.log scene 序列做差异对比。

**通过判据**（任一）：
1. unity.log 出现 CatalogScene / LoadScene 字样；或
2. BepInEx 日志出现 Catalog.Start 相关行；或
3. 安装 1.0.39 的 Azurite 后出现 native authoring probe: sample=2; scene=CatalogScene。

**如果 E0 通过** ⇒ restricted token 立即降级为可选加固，H5 / H6 整条线封存，项目解封。
**如果 E0 失败** ⇒ 记录首个失败点（unity.log + BepInEx 日志 + 事件日志），再考虑 H3（资源服务器 / 离线资源）。

### 实验 E1（第二优先）— 验证 AA 原生 mcp.start 能否打开工程

**目的**：证实 / 证伪 H1。若成立，这是**最短路径**，可绕开所有 Harmony 逆向补丁。

**步骤**：

1. 用一个真实 AA 工程副本（**先复制到 run 目录，不要直接用 backups 原文件**）：
   · E:\aamod\backups\editor-bench-20261003-131109\直通线-0.aap2（851,559 B）
   · E:\aamod\backups\math-v2-benchmark-20261003-143650\【数学】必修二总结-0.aap2（1,805,601 B）
2. **先做静态探测（不需要启动）**：确认 C:\Users\21294\AppData\Local\AzureArchive\automation\editors 目录是否存在、内容是什么（这是 AuthoringControlServer.DiscoveryDirectory 的默认位置）。
3. 若 E0 已通过（AA 能以非 CLI 方式无头跑到 CatalogScene），则在**同一个** AA 进程存活期内，用**第二个** AA 进程带 CLI transport 发：
   arguments.json = {"argv":["mcp.start","--project","<run>\project\直通线-0.aap2"],"cwd":"<run>\workspace"}
   环境变量 AZUREARCHIVE_CLI_DIRECTORY=<run>\request
4. 观测：第二个进程的 response.json 是 ok:true 还是 EDITOR_NOT_READY；第一个进程的 unity.log 是否出现 ScenarioEditorScene。

**关键风险与预案**：

- 若第二个进程返回 EDITOR_NOT_READY ⇒ 说明第一个进程没注册 control server（可能 batchmode 不建，或 CLI-active 提前返回）。**记录确切的 error / 枚举值**，这本身就是高价值结论。
- 若 mcp.start 需要「GUI process」，则可能需要**无 -batchmode 但无可见窗口**的启动方式（Job + private desktop 隐藏窗口）。**这一点必须在实验前明确，且仍须满足「不弹可见窗口」。**

### 实验 E2 — 资源闭包（仅当 E0 因 Addressables 失败时）

先探测 http://127.0.0.1:27510/aares/win/catalog_2024.08.17.15.07.45.hash 可达性。
- 200 ⇒ 远程可用，H3 排除。
- 502 / 拒连 ⇒ 需准备 --aap-offline-resources <dir>，且该目录必须让 ValidateDirectory 对 **13,085 个 bundle** 全部 File.Exists 通过。这是**独立的一大块工作量**，应单列任务，不要塞进 task-1 / task-2。

### 实验 E3 — 隔离性回归（E0 / E1 通过后才做）

对 C:\Users\21294\AppData\LocalLow\foxxlight\AzureArchive、data\settings\user_settings.json、C:\Users\21294\AppData\Local\AzureArchive\automation\editors、HKCU Software\foxxlight\AzureArchive 做前后快照 diff。**零变化**才符合用户隔离约束。

### 明确不做

- ❌ 不修 restricted token / COM / bcrypt（除非 E0 明确证明不受限也不行）。
- ❌ 不重写 EditorEntryStage.cs（除非 H1 / E1 被证伪）。
- ❌ 不用 EditorRoute* 当编辑器驱动。
- ❌ 不用探针输出判定「有没有进编辑器」。

---

## 7. 证据清单（本报告引用的全部可核验现场）

| 证据 | 路径 | 关键量 |
| --- | --- | --- |
| 成功 capabilities BepInEx 日志 | E:\aamod\labs\Azurite-20261108-stable\runs\restricted-5a3cebee…\output\bepinex.LogOutput.log | 3629B，SHA 5a0e2bb1…3cd3 |
| 同 run controller 结果 | …\controller-result.json | passed=true，bootstrapExitCode=0，aaExitCode=0 |
| 同 run 请求 | …\request\arguments.json | {"argv":["capabilities"],"cwd":"…\workspace"} |
| 同 run unity.log | …\output\unity.log | 18301B，无 CatalogScene |
| 失败 run | …\restricted-b3231d47…\controller-result.json | passed=false，requiresExplicitRecovery=true |
| schema run 的 AA 自述能力 | …\restricted-a3dfb0bd…\request\response.json | 128700B，含 mcp.start 定义 |
| 探针采样 | F:\AzuriteLabs\Editor-20261108-normal\install\BepInEx\LogOutput\AzuriteNativeAuthoringProbe\samples.ndjson | 40 行 = 20×2，Studio 非空 0 次 |
| 探针最新值 | …\AzuriteNativeAuthoringProbe\latest.json | SceneName=CatalogScene，Studio=null |
| 探针 player.log（Addressables 502） | …\AzuriteNativeAuthoringProbe\player.log | 114693B |
| 日常正常日志 | F:\AzureArchive_101\BepInEx\LogOutput.log | **哈希易变，勿作固定基线**：22:05 时 120800B/SHA 7a13711c…，22:15 时 118746B/SHA 73faff20…。Azurite **1.0.39**，14 mods |
| 日常探针行 | 同上 | sample=1 scene=Loading / sample=2 scene=CatalogScene（2026-11-20 22:14 用户实时会话再次复现，见第 9 节） |
| 孤立 pending | F:\AzuriteLabs\Editor-20261108-normal\runs\full-capabilities.pending.json | 459B，runDirectory **不存在** |
| 源码存在性快照 | \.superpowers\sdd\azurite-authoring-20261108\rebuild-20261115\entry-recovery\current-source-presence.json | 3 个 exists:false |
| 恢复报告 | …\entry-recovery\RECOVERY-REPORT.md | 缺失契约清单 |
| 实际 trace 评审 | …\aa-85ab-actual-diagnostic-c-review-20261113.md | COMException 0x800703F0 / PID34320 |
| WU1 失败 | …\wu1-runtime-failure.md | bcrypt 1114 / exit 30 |
| 受限等待计划 | …\team\next-controlled-wait-607-plan.md | H1 / H2 两个可观测解释 |
| 编辑器入口静态证据 | …\editor-entry-next-gate.md | OpenProject token 100695341 / RVA 0x951670 |
| catalog 扩展 brief | …\normal-catalog-extension-brief.md | cache / discovery / Addressables 逐 RVA |
| NormalLoaderPlugin 源码 | <authoring-WT>\AcceptanceHarness\NormalEditor.Loader\NormalLoaderPlugin.cs | 73 行 |
| CatalogEntryHost 源码 | <authoring-WT>\AcceptanceHarness\NormalEditor.Entry.Host\CatalogEntryHost.cs | 114 行 |
| EditorRoute README | <authoring-WT>\tools\headless\EditorRoute.README.md | 「does not implement a normal Editor」 |

> <authoring-WT> = C:\Users\21294\.codex\worktrees\azurite-authoring-20261108\Azurite-github-publish-1.0.38

---

## 8. 我没有做到 / 未覆盖的部分（诚实声明）

1. **未启动任何 AA**，所以 E0 / E1 的结论是**预测**，不是实测。所有「会通过 / 会失败」的判断都必须由 task-2 实际运行证伪。
2. **mcp.start 在 -batchmode 下是否可用，未验证**。这是 H1 的最大未知，也是本报告最需要被实验检验的一点。
3. **未读到 artifacts 下 406 个已删目录的内容**——它们已被删除（2026-11-15 15:30–15:37），D:\_codex-fix-20261106\cleanup_20261115 的备份覆盖情况本次未逐项核对。
4. **未读完 830 个 SDD 文件**，而是按关键词（COM / token / BepInEx / OpenProject / EditorEntryStage / mcp.start / 0x8007 / probe）和重点目录（aa-fr-*、team\*、rebuild-20261115、recovery-investigation-20261115）定向取证。若有遗漏，应是我没命中的关键词。
5. **0x800703F0 的 native 后端 / apartment / SyncCtx 仍未在 6.0.7 同字节运行时下复现**（C 盘只有 6.0.36），与 SDD 里的记录一致，我未能推进。
6. **未验证资源服务器 27510 当前是否可起**——那是启动行为，属 task-2。
7. **本报告没有修改任何生产文件**：F:\AzureArchive_101、三个 git worktree、两个 lab 全部只读；唯一写入是本文件。

---

## 9. 取证期间新增的关键现场：日常 GUI 的实时复现（2026-11-20 22:14）

> 本节是取证过程中**意外获得**的证据，**不是**我启动的。我全程只读，未启动任何 AA。
> 事实：F:\AzureArchive_101\BepInEx\LogOutput.log 在我取证期间（22:05 → 22:15）**被重写并持续增长**，
> 说明**用户本人当时启动过日常 GUI 会话**。以下结论全部来自那一份实时日志与探针产物。

### 9.1 实测：日常 GUI 每次启动都精确停在 CatalogScene

新日志（118746B，2026-11-20 22:15；随后继续增长）中的探针行，逐字：

    sample=1; scene=Loading;      studio=none; session=none; project=none; revision=none; snapshot=none
    sample=2; scene=CatalogScene; studio=none; session=none; project=none; revision=none; snapshot=none

F:\AzureArchive_101\BepInEx\LogOutput\AzuriteNativeAuthoringProbe\latest.json：
CapturedUtc = 2026-11-20T14:14:31.8021154+00:00（= 本地 22:14:31），SceneName = CatalogScene，**Studio = null，Session = null**。

samples.ndjson 末尾三条（2026-11-20）：
- 22:12:14 SceneName=CatalogScene
- 22:14:28 SceneName=Loading
- 22:14:31 SceneName=CatalogScene

### 9.2 这一条现场同时证实了三件事

1. **日常 GUI 的启动链路确实是通的**：AA 正常起、BepInEx 正常起、Azurite 1.0.39 正常加载、正常走 Loading → CatalogScene。
   ⇒ 再次坐实**结论 A**：加载器从来不是问题。
2. **探针 2 发即停是硬设计**：即便这是一次**用户真实操作、可能随后真的点开了编辑器**的会话，探针依然只留下 sample=1/2 两条，Studio 永远 null。
   ⇒ 再次坐实**结论 B-2**：**探针输出在结构上无法区分「没打开工程」和「打开了工程」**。任何用它做验收判据的方案都无效，必须换证据载体。
3. **F:\AzureArchive_101\BepInEx\LogOutput.log 是易变文件，不能当固定基线**。
   实测同一文件两次取值：
   · 22:05 → 120800B / SHA 7a13711cbd54e26e1dbafac42247714ed533227142b2a2f25a690794b7eb2c1d
   · 22:15 → 118746B / SHA 73faff2030e2483047567060a64dea39d131a622fc8455c597cbbc478a288dc9
   **task-1 / task-2 若要把日常日志作为「正常启动对照基线」，必须在同一次采集内完成复制 + 哈希，不能跨时间引用。**

### 9.3 顺带确认：日常会话不做任何 batchmode

当前日常日志中 batchmode / nographics / AZUREARCHIVE_CLI_DIRECTORY 命中数均为 **0**；探针显示 focused=True、target=120 vsync=0。
⇒ 日常 GUI 是**前台、有窗口、有焦点**的正常会话，与无头 lab 完全不同的运行形态。**不能用它的日志直接替代无头验收。**

### 9.4 对 task-2 的直接含义（新增一条硬约束）

> **在启动任何无头 lab 之前，必须先确认日常 AA 不在运行，并在同一瞬间复制 + 哈希日常日志作为对照。**
> 本次取证期间日常 AA 在 22:14 前后处于活动状态——这正是交接备忘里「有日常 AA 正在运行时不得启动测试」那条红线要防的情况。
> task-2 的启动脚本必须在 preflight 里**同时**检查：tasklist 无 AzureArchive.exe **且** 日常 LogOutput.log 的 mtime 距今超过阈值（例如 10 分钟）。

---

## 10. 修正记录

- 2026-11-20 22:15：发现日常日志在取证期间被重写（用户启动了日常 AA）。据此：
  · 第 7 节「日常正常日志」行补注哈希易变；
  · 第 2.1 节表格补注「每次启动都如此，2026-11-20 22:14 实测复现」；
  · 新增第 9 节记录该实时现场与其对 task-2 的新增硬约束。
  · **未修改**任何此前基于 F:\AzuriteLabs / E:\aamod\labs 静态产物的结论——那些产物在本轮取证期间 mtime 未变（见 evidence-hashes.txt）。

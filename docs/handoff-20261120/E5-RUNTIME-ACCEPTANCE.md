# E5：Azurite v1.0.40 实机加载验收（runtime acceptance）

日期：2026-11-21 02:56–03:00 Asia/Shanghai。执行：lead。
运行目录：`E:\aamod\tmp-azurite-takeover\experiments\E5-runtime1040\`

## 结论

**v1.0.40 候选包在真实 AA 无头环境中加载成功——`runtimeAcceptance: not-performed` 这一发布缺口已闭合。**

## 实验步骤（全部在隔离副本内）

1. 将候选包（`D:\AzuriteEvidence\20261120-candidate-1.0.40\dist\Azurite-1.0.40.zip`，SHA256 `0ef76d7b…`）解压安装到副本 `mods\Azurite\1.0.40\`
2. 新建 profile `Azurite1040`（modconfig 只启用 Azurite 1.0.40）
3. 切换副本 `ActiveProfile.txt` → `Azurite1040`（修改前快照留证：`evidence\activeprofile-before.txt` = AzuriteAcceptance）
4. 护栏启动器启动（R1–R6 全过，PID 39512，-batchmode -nographics -disableaudio）
5. 观察 BepInEx 加载链路 → 240s 超时由护栏终止（预期行为：无 CLI 目录时 AA 会话常驻）
6. 回滚：ActiveProfile 恢复为 AzuriteAcceptance

## 加载证据（BepInEx 日志，evidence\bepinex-log-1040-session.log）

```
Enabled 'Azurite' version '1.0.40' from ...mods\Azurite\1.0.40
Discovered 1 plugin(s) in selected mod directory ...mods\Azurite\1.0.40
Loading [Azurite · 蓝铜矿 1.0.40]
Azurite generated binding contract verified: 414 type references, 1491 member references
AA host identity verified: 1.0.1 (render handoff, FPS and scrolling)
Azurite 1.0.40: measured cadence enabled=True
Chainloader startup complete
Published 1 successfully loaded mod(s) to ModConfig
```

- **0 ERROR / 0 Exception**（与 1.0.38 基线会话同等干净）
- 绑定契约 414 类型/1491 成员引用（1.0.38 是 409/1465 → 1.0.40 新增类型按预期增长）
- 会话结束时正常 `Published`，无加载后异常

## 红线合规

- 日常安装 `F:\AzureArchive_101` 全程 **0 写入** ✅
- 修改仅限隔离副本：mods 加 1.0.40 目录、新 profile 目录、ActiveProfile.txt（已回滚）✅
- 护栏启动器 R1–R6 全过、单实例 ✅
- 结束无残留进程 ✅

## 对发布决策的影响

Release Notes 中「runtimeAcceptance=not-performed」需更新为：
**runtimeAcceptance: headless-load-verified**（隔离副本无头加载验证通过；实机 GUI 安装仍未验，如实标注）。
这消除了 v1.0.40 发布的最大诚实性顾虑——包本体已证明能被 AA 正常发现、加载、完成插件生命周期。

## 遗留

- GUI 模式实机加载（用户日常使用场景）仍未验，但无头加载是更强的自动化可复验证据
- 建议发布时 Release Notes 保留「首次安装请观察 BepInEx 日志」的提示
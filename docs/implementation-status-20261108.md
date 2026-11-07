# Azurite 分阶段实施状态 — 2026-11-08

本文件记录实施结果和下一道验收门槛。离线测试通过不代表 AA 原生功能已验收。

| 阶段 | 已有结果 | 尚未通过的门槛 |
| --- | --- | --- |
| 0：无头环境 | 混合安装恢复备份、完整隔离副本、独立 Profile、经复核的一次性账户准备脚本与受限 CLI 探测工具 | `AzuriteTest` 账户尚未创建；真实登录、能力/格式查询、普通编辑器会话及非空离屏画面均未运行 |
| 1：稳定基线 | 独立分支上的滚轮标题和单选快捷键修复；生产源码行为测试；版本、原始 Core、构建来源和逐文件校验；本地 1.0.40 候选包 | 原生显示、快捷键实际槽位、一次撤销/重做及保存重开未验收；快捷键默认关闭 |
| 2：创作功能 | 五类卡包的 v2 数据模型、离线编译和迁移规则；选择修订修复；重建 Core 的离线兼容性预检 | 原生会话归属与事务、批量工具栏、卡包预览/确认界面、真实资源效果和失败回滚；没有交付卡包功能 |
| 3：性能 | 保留现有证据和独立开关要求 | 尚未进行本计划的五轮开/关对照；没有新增默认开启的深层优化 |

## 稳定候选和恢复点

稳定候选目录：`E:\aamod\releases\Azurite-1.0.40-stable-baseline-1-candidate`。

- 源码提交：`9378eda98156255ae202fe1a1bda525c5bdac0cd`。
- `Azurite-1.0.40.zip`：SHA-256 `90a2e2d6cc15f7e79d0d295df8bc1b36338174d2ae6f2b8131e6ddf3be0764e1`。
- `Azurite-1.0.40-source.zip`：SHA-256 `998683b253115bfc747342c6924d22734d46fa11a6c391d5a300533890943721`。
- 候选包交付状态及回退说明见该目录的 `DELIVERY-STATUS.json` 和 `交付说明.md`。
- 安装恢复副本：`E:\aamod\backups\Azurite-before-staged-progress-20261108`。

恢复副本包含 86 个备份文件及注册表导出清单。完整隔离程序副本包含 1,059 个文件、3,521,642,657 字节，在初始化测试 Profile 前逐文件校验一致；测试 Profile 仅选择 1.0.38 基线。

该候选仅保存在本地，未切换日常安装、未上传新的 GitHub Release，也未覆盖 1.0.39 目录。创作分支的后续代码不包含在此候选中。

## 本轮验证

创作分支的完整可移植验证为 **25/25 作业通过**：Core 加 24 个已分类测试项目。结果位于 `artifacts/source-validation-20261107-192152-3cb4d3ee/result.json`；目录时间为 UTC。测试没有启动 AA。

稳定候选此前已通过独立分组验证：Core 加 19 个测试项目共 20/20，宿主静态组 6/6。轮控布局生产源码测试 14/14、快捷键行为 17/17、宿主编辑许可 7/7、HostProfile 77/77；CPU 协作测试在显式指定生产 `RenderControlV1` 源码后 181/181。宿主静态构建和接口检查不等于启动宿主。

受限 CLI 工具经过修复和独立复查，本轮范围内 Spec/Quality 均通过。完整无害测试、账户路径与错误诊断测试、真实零退出码加监测异常的专项回归均退出 0；原有后台工具回归亦通过。七个新增 PowerShell 文件解析通过，十二份受审文件哈希匹配。

另已验证 Git 暂存与原始字节导出的一致性，防止仓库换行规范化破坏固定合同摘要。合同内容、451 个文件固定值、10 个范围及 13 个证据摘要均未变化；规范化后合同 SHA-256 为 `8B27AB740452DCA80E78335E4A7512242EBCFD65B7C67832A8A6104B1D3DE06F`。这项修正及字节回归亦通过独立复查。

工具现在验证同一自有进程组全部退出；无法确认时保留私密阻断标记，禁止下一轮启动。账户路径在目标身份下验证真实目录及重解析点，父进程核对相关证据。启动或监测异常即使伴随退出码 0 也会失败。跨账户句柄交接仍未实测，交接验证失败时不会启动 AA。

最终只读预检仍为 `Ready=false / WillLaunch=false`，缺少账户清单、加密凭据和 runs 目录；实际账户查询仍不存在，AA 进程数为 0。无害测试不替代真实登录、查询、声音、画面或编辑器验收。本地独立复查记录保存在 `.superpowers/sdd/azurite-authoring-20261108/restricted-cli-fix2-review.md` 与 `restricted-cli-fix3-review.md`；最终运行逻辑复跑日志为同目录的 `restricted-cli-fix2-root-green.log` 和 `restricted-cli-fix2-root-focused.log`，源码字节验证为 `restricted-cli-fix3-root-staged.log`。

Core 的 ABI 和隔离运行比较见 `docs/core-migration-precheck.md`。插件仍使用保留的原始 Core，不能把兼容性预检理解为已部署新 Core。

## 原生契约的已知界限

当前宿主 token 重新映射后，已经静态确认 `StudioCommon.Start` 调用 `AuthoringEditorSession.Begin`，以及 OnDestroy、Refresh、CaptureProject、WriteHistory 的会话同步链。旧的“四个 ResourceCatalog 调用”结论使用了过期 token，已撤回。

原生批量操作使用 `line.insert/update/delete/move` 和真实持久 ID，不能直接提交指针型选择 ID。宿主单次上限为 500 个操作，因此在规划层 1,024 上限之内采用更严格的 500 有效上限；超限整批拒绝，不拆分事务。

本轮还静态确认目录页 Edit 回调调用原生 `AuthoringWorkbench.OpenProject(string)`，经资源预载和场景切换进入正常编辑器生命周期。该入口返回 void 且可能显示原生错误，调用返回不代表打开成功。普通启动会涉及资源、设置和控制管道，必须另行验证隔离范围，不能沿用资源无关查询的许可。尚未实现或运行此入口的自动化测试。

## 当前唯一人工前置操作

同一 Windows 用户需要手动打开管理员 PowerShell，按 `E:\aamod\labs\Azurite-20261108-stable\admin-setup\先读我.md` 完成一次账户准备。该脚本只建立低权限测试账户、目录权限和加密凭据，不启动 AA。无需提供密码。

完成后依次核对账户、权限、加密凭据和真实登录；再使用经审查的受限入口执行 `capabilities`，检查结果后单独查询 `schema`。用法与证据边界见 `docs/restricted-cli-probe.md`。

能力/格式查询成功后，仍需单独证明普通编辑器生命周期和事务归属，才能继续接入批量写入与五类卡包。图形、内部音频、导出和性能验证各有独立门槛；不会因纯逻辑查询通过而自动放行。

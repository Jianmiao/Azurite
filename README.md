# Azurite 1.0.40 稳定基线候选

构建标识：`stable-baseline-1`。基于已发布的 1.0.38，目标宿主为 AA 1.0.1。当前是待实机验收的开发候选，不代表完整创作功能或性能优化已经交付。

## 本版变化

- 修正设置页“滚动灵敏度”标题、百分比和滑块布局，保留 50%～200% 范围与当前 Profile 数值。
- 修复单选 `Alt+1`～`Alt+5` 的原生写回顺序：先执行修改，再登记一条撤销历史。输入框、拖拽、播放、弹窗、加载、保存、导出及失焦期间禁止修改；部分修改失败会停止该会话的快捷键。
- `Authoring / SingleSpeakerShortcuts` **默认关闭**，只在 AA 1.0.1 的明确能力检查下提供实验入口。实际 Undo/Redo、保存重开和后台原生验收通过前不默认开放。
- 保留帧率映射、滚轮、静止表面绘制调度及 `RenderControlV1/v1`。批量编辑、框选和五类卡包不在本稳定候选内；深层预览、渐进加载、对白虚拟化等补丁继续保持原有宿主限制。
- 运行包继续使用原始 `Azurite.Core.dll`，SHA-256 为 `76a46c4a896dfbdffd66c2be85e9a0e07d2d0523faaef0841763d997c65df1a5`。

## 构建与检查

```powershell
./Build.ps1 -AAInstallPath '<你的 AA 安装目录>'
./tools/Test-Source.ps1 -Suite Portable
./tools/Test-Source.ps1 -Suite Host -AAInstallPath '<你的 AA 安装目录>' -AaveSourceRoot '<AAVideoExport 源码目录>'
```

可移植检查、宿主静态引用检查与真实 AA 验收分别记录。详细命令和依赖边界见 `docs/source-validation.md`；当前账户无头测试要求见 `docs/headless-testing.md`。脚本不会把已有日志、仅退出成功或源码字符串匹配当作实机功能成功。

## 使用与恢复

仅从校验通过的包解压到 `mods/Azurite/1.0.40`，不覆盖旧版本目录；测试和安装必须在 AA 已关闭时进行。按已更新的要求，候选验证使用当前未提升权限的 Windows 账户及隔离副本，不创建新账户；共享用户设置和注册表状态须单独核对，日常 Profile 不会由测试工具自动切换。

尚待核验：真实 NGUI 字体及控件画面、原生快捷键/撤销/保存流程、AAVE 导出开始/取消/完成恢复，以及性能收益。无头测试不会冒充前台窗口、系统输入或用户可见流畅度验收。

来源及许可范围见 `docs/SOURCE.md` 和 `THIRD_PARTY_NOTICES.md`。

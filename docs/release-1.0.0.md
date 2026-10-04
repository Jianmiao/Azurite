# Azurite 1.0.0 发布范围

## 已实现

- Unity 按需绘制调度：无输入、无加载、无保存、无动画的已识别编辑表面进入静态 cadence；安全重绘和状态变化会唤醒。
- 背景、图片、音效、BGM、角色和 Mod 选择窗口的静态状态识别；未知窗口保持全帧保护。
- script 编辑预览区分“预览对象存在”和“预览有实际动态生产者”。自动播放、语音、场景/背景/文字动画、延迟推进、背景效果、滚动和加载继续保护。
- 显示器刷新率策略：低于 120Hz 使用半速/全速/∞，120Hz 及以上使用 60/120/∞；∞ 使用 VSync 跟随当前显示刷新率。
- 纯文本对白快速路径，保留原生文本块、换行和样式；不符合安全条件的文本回退原生路径。
- 增删对白事务门：Insert/Delete/SyncScriptList 保持 AA 原生同步，事务期和 settle 期关闭可能读取半重排状态的优化。
- 固定 AAVideoExport `RenderControlV1` 协作接口。

## 重要边界

Azurite 不控制 GPU 核心频率。Task Manager 的 3D 利用率不能单独证明 GPU 时钟变化或 CPU 瓶颈。未知窗口、未知动态源和原生增删重排仍保守处理；1.0.0 不宣称所有大稿增删已经加速。

`OnDemandRendering.renderFrameInterval` 是全局 Unity 调度，不是每个窗口独立的 GPU 开关。因此 1.0.0 只在可以证明整个当前编辑表面安全时降低 cadence，不能把 UI 和未知动态预览分开假设。

## 验证

- Plugin/Core Release 构建：0 warnings、0 errors。
- HostProfile、刷新率策略、静态生产者和 mutation state tests：通过。
- ScrollActivityGuard：通过 Update/LateUpdate、滚轮、惯性、活动视图、场景切换和高刷新率测试。
- HostInteropContract：类型、成员、继承、委托、帧率标签方法签名和缺失/变异拒绝测试通过。
- 安装包路径、PE、程序集版本、SHA-256 和禁止宿主内容检查通过。
- 未启动 AA 做完整 GPU/帧时间/大稿成片验收；实际体验仍需用户验证。

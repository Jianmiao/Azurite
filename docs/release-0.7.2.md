# 0.7.2 发布说明与验证

## 变更

- `ScrollActivityGuard` 将输入/事件观察与完整 viewport 运动采样分离。输入在 Update 期唤醒；完整的裁剪、位置、惯性与拖动状态在 LateUpdate 采样一次，不在同一帧重复读取全部滚动视图。
- ScrollActivityGuard 只保留活动 UIScrollView。场景切换、滚轮事件、新的按钮按下及每秒重发现都会更新活动视图集合。
- 连续滚动期间只在进入保护态时恢复间隔和重置 estimator/policy，不再逐帧重复执行唤醒。
- HostActivity 在滚动保护期间直接返回保守 blocked 状态；导出所有权仍在前置 HostCompatibility 检查中探测，滚动结束后下一次 Update 恢复完整活动检测。
- 精确校验的新 AA 独立启用两个 FPS 标签 postfix：`SettingPanel.UpdateWidgets()` 与 `OnFpsSliderChanged()`。签名及 label getter 变更会让新宿主接口校验失败关闭。Postfix 不修改 AA 档位或显示器设置，只把显示文字映射为 60/120/∞。挂钩注册失败时退回 0.5 秒兼容刷新。

## 验证

- Release 构建：0 警告、0 错误。
- HostProfile 与 FPS 标签数据转换：15 项通过。
- 生产 ScrollActivityGuard 源链接回归：16 项通过，含 Update 输入、LateUpdate 视口变化、晚到事件、拖动、活动目录过滤、一次/帧样本及 60/120/320/800 FPS 下保护间隔为 1。
- 生产 HostInteropContract 源链接变异与标签回调签名检查：21 项元数据变异加 5 项回调/label 签名检查通过。
- AA 1.0.0-fix 静态接口解析通过，最终检查引用类型/成员数量见构建验证记录。

## 限制

这些回归覆盖 Azurite 的托管调度和兼容边界，不执行 Unity/IL2CPP 原生逻辑，也不计量 AA 的总主线程耗时、真实 GPU 频率或 Task Manager 帧图。滚动改动减少 Azurite 自身重复调用；AA 原生对话列表滚动/布局处理仍可能构成较大稿件的主线程瓶颈，本次没有证明它已解决。本版尚未在 AA 实机验证；需要重启 AA 后确认真实滚动和帧率标签是否改善。

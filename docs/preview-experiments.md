# 编辑预览实验

这组开关用于本地候选验证，默认关闭。`ExperimentalPreviewOwnership=true` 只让已验证的 AA 1.0.0-fix 尝试取得独立编辑预览的所有权，不会绕过宿主校验，也不会启用全局 `RenderScale`。

```ini
[Preview]
ExperimentalPreviewOwnership = true
MatchDisplayResolution = true
LimitCameraCadence = false
RenderScale = 1
```

先单独验证纹理尺寸匹配。它保留原始纹理，按照预览控件的屏幕像素创建较小的独立纹理；保留源比例、格式、深度和采样设置，不做低于实际显示像素的模糊缩放。视口需稳定半秒，至少有 5% 尺寸收益才会替换；视口扩大时先恢复原纹理。退出编辑、关闭功能、绑定变化、插件卸载或导出接管都会恢复。

每次尝试仍须确认 `PreviewScene` 的 URP Base 相机、空 camera stack、原始 `Preview` 纹理、完整 viewport/UV、唯一匹配的 inspector `UITexture`。生命周期/描述符/引用不匹配时不接管；发现外部改写时停止该实验，不覆盖另一所有者的值。恢复失败会使 `RenderControlV1.BeforeAcquire` 抛出，阻止 Azurite 把未恢复的渲染状态交给导出方。

确认纹理路径和导出恢复后，可单独验证 `LimitCameraCadence=true` 与 `CameraFps=60`。它只调度已确认静止编辑场景的预览相机，保留上一次纹理内容；滚动、未知或动态工作、增删事务、导出均恢复相机。整页没有绘制的帧不会消耗预览绘制名额，避免相机节奏和全局低频节奏错相。它不是完整的内容脏标记缓存，也没有停掉模拟更新。

当前采样证据来自 2026-10-04 的本机 `LogOutput.log`：预览目标为 1280×720，显示区域约 1114×626，预览相机为 Base 且 stack=0。按源比例和像素对齐的替换预计为 1120×630，减少约 23.4% 的预览目标像素；这不能换算成同等比例的整页 GPU 降幅。相机 `renderPostProcessing=false`，关闭它不会节省工作。共享 `UIRenderFeature` 的 Bloom 当前启用，但其职责包含 AA 自有 UI 合成，因此这次没有禁用 Feature 或改共享管线资源。

`PreviewTests` 覆盖宿主门控、尺寸计划、迟滞及独立/全局绘制节奏配合。实际省电幅度、画质和 AAVideoExport 输出一致性仍需要本机体验验证；编译和可移植测试不代替这些结论。

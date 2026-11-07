# Azurite 推进状态

更新时间：2026-11-08。此文区分源码/离线检查与真实 AA 验收，不把默认关闭或拒绝写入计为功能完成。

## 当前已完成的准备

- 独立稳定分支：codex/azurite-staged-progress-20261108；基线 v1.0.38 / eb43469。
- 日常安装恢复副本：E:\aamod\backups\Azurite-before-staged-progress-20261108（86个备份文件及注册表导出清单）。
- 独立程序副本：E:\aamod\labs\Azurite-20261108-stable\install（1059文件，3521642657字节，初始化测试Profile前逐文件SHA一致）。
- 日常AA未启动、日常安装/Profile未修改；测试Profile独立且仅选择已验证的1.0.38基线。
- 稳定候选版本已统一为1.0.40/stable-baseline-1，仍用原始Core；未打包安装或发布。

## 源码验证

- 轮控布局生产源码测试：14/14；快捷键生产源码行为：17/17；宿主编辑许可：7/7；HostProfile：77/77。
- 完整可移植组：Core+19测试工程，20/20任务通过。
- 宿主静态组：插件构建、Driver/Selector/LoadingIntegration/CpuTests与实际DLL接口检查，6/6任务通过。
- CpuTests已移除错误的本机相对路径，显式传入生产RenderControlV1源码后181/181通过。
- 稳定代码和打包来源绑定已通过独立评审；不得将上述结果解释为原生Undo/保存、无头图形或性能验收。

## 新原生证据

重新映射当前宿主token后，确认StudioCommon.Start调用AuthoringEditorSession.Begin，OnDestroy/Refresh/CaptureProject/WriteHistory的会话同步静态链存在。旧四个ResourceCatalog调用结论使用了过期token，不再作为依据。

宿主操作使用line.insert/update/delete/move及真实持久ID；指针型选择ID不能直接提交。AuthoringService原生单次限制500个操作，因此在规划层1024上限内采用更严格的500有效上限，超限整批拒绝，不拆分成多次提交。

## 尚未通过的门槛

1. 专用Windows低权限测试账户尚不存在；当前控制账户不在Administrators组。账户准备工具需支持由另一个管理员执行，凭据不进入聊天/命令行/日志。工具复核与修复进行中。
2. 无头启动、进程音频隔离、真实Editor会话、离屏图形及运行结果尚未验证；AA启动目前保持阻断。
3. 新单选快捷键默认关闭，等待原生修改/撤销/重做/保存重开验收。
4. 批量编辑、五类卡包仍未交付；离线模型在单独authoring分支准备，没有混入1.0.40运行包。
5. 深层优化未新增启用，性能对比尚未运行。

没有新GitHub发布、没有覆盖已有版本；阶段完成必须以对应真实验收证据为准。
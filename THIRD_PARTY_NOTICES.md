# 第三方组件

安装包仅分发 Azurite 插件、原版 Azurite.Core、图标、manifest 和说明。以下依赖由兼容的 AA 模组环境或开发环境提供，不随安装包复制：

| 组件 | 用途 | 上游许可说明 |
|---|---|---|
| BepInEx 6 | 模组加载、日志和配置 | [LGPL-2.1](https://github.com/BepInEx/BepInEx/blob/master/LICENSE) |
| Il2CppInterop | Unity IL2CPP 托管桥接 | [上游 LICENSE](https://github.com/BepInEx/Il2CppInterop/blob/master/LICENSE) |
| Harmony | 历史宿主上的受限方法补丁 | [MIT](https://github.com/pardeike/Harmony/blob/master/LICENSE) |
| Mono.Cecil | 只读程序集与接口检查 | [MIT](https://github.com/jbevain/cecil/blob/master/LICENSE.txt) |
| .NET | 执行和构建托管代码 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| Unity、AA 及生成绑定 | 由用户自己的宿主提供 | 不在本仓库或安装包中重新分发 |

测试工程中的 Unity/NGUI/IL2CPP 替身是用于测试 Azurite 行为的最小自有实现，不是宿主实现源码。ILSpyCmd 用于恢复本模组代码，不随模组安装包提供。

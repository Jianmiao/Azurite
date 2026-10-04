# Azurite 0.7.1 来源与恢复说明

本仓库由已保留的 Azurite 0.7.0 .NET 程序经 ILSpyCmd 9.1.0.7988 反编译恢复，再添加 AA 1.0.0-fix 的受限适配和可移植验证。

- 原 Azurite.dll SHA-256：`A5AB09163853364F393E65818C23C80FDE47CE3F40312746605DE469E6BC415F`。
- 保留的原 Azurite.Core.dll SHA-256：`76A46C4A896DFBDFFD66C2BE85E9A0E07D2D0523FAAEF0841763D997C65DF1A5`。
- `Core/` 包含恢复后可构建的 Core 源码；本次运行包继续使用上列原 Core 文件。
- `Plugin/CompilerAttributes.cs` 补回原程序中已存在的 Nullable 编译元数据，以避开宿主同名不完整属性类型造成的编译冲突。
- 本仓库不是原始源码工程、注释、测试或开发历史的完整备份。现有测试、构建与打包脚本属于恢复后新增的工作。

AA、Unity、BepInEx、Il2CppInterop、Harmony 和 Mono.Cecil 为宿主或开发引用，不随 Mod 包分发。仓库中的宿主文件校验值及接口名称用于兼容性判断，不包含 AA 程序、生成绑定、作品或素材。

本仓库尚未指定开源许可证；第三方组件继续适用各自许可。公开源码不代表重新授予第三方组件的许可。

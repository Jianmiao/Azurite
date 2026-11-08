using Azurite.EditorIsolation;
using Mono.Cecil;
using Mono.Cecil.Cil;
if (args.Length != 1) throw new Exception("Pass the metadata-only AA install root.");
HostContract.VerifyFiles(args[0]);
Console.WriteLine("PASS exact pinned host/core hashes (read-only bytes; native code not loaded)");
IEnumerable<TypeDefinition> All(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(All));
foreach (var group in HostContract.Methods.GroupBy(m => m.Assembly))
{
    using var module = ModuleDefinition.ReadModule(Path.Combine(args[0], "BepInEx", "interop", group.Key + ".dll"));
    var types = module.Types.SelectMany(All).ToDictionary(t => t.FullName.Replace('/', '+'));
    foreach (var spec in group)
    {
        var type = types[spec.Type];
        var method = type.Methods.Single(m => m.Name == spec.Method);
        var field = type.Fields.Single(f => f.Name == spec.PointerField && f.FieldType.FullName == "System.IntPtr");
        var ctor = type.Methods.Single(m => m.IsConstructor && m.IsStatic);
        var instruction = ctor.Body.Instructions.Single(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference f && f.Name == field.Name);
        if (instruction.Previous?.Operand is not MethodReference target || target.Name != "GetIl2CppMethodByToken" || instruction.Previous?.Previous?.Operand is not int token || token != spec.Token)
            throw new Exception("Native token drift: " + spec.Id);
        if (!method.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == field.Name)) throw new Exception("Method/pointer mapping drift: " + spec.Id);
        Console.WriteLine($"PASS generated native binding {spec.Id}: {spec.Type}.{spec.Method} token={spec.Token}; runtime RVA verification pending");
    }
    if (group.Key == "Assembly-CSharp")
    {
        foreach (var (typeName, name, propertyType) in new[] { ("UserSettings", "settingFilePath", "System.String"), ("UserSettings", "_cachedWorkspacePath", "System.String"), ("UserSettings", "data", "UserSettings/SettingData"), ("UserSettings+_ReadUserSettings_d__43", "__1__state", "System.Int32"), ("UserSettings+_ReadUserSettings_d__43", "__4__this", "UserSettings"), ("UserSettings+_SaveTask_d__41", "__4__this", "UserSettings") })
        {
            var property = types[typeName].Properties.Single(p => p.Name == name);
            if (property.PropertyType.FullName != propertyType || property.GetMethod is null || property.SetMethod is null) throw new Exception("Native field accessor drift: " + name);
            Console.WriteLine("PASS native field accessor " + typeName + "." + name);
        }
        var expectedKeys = new[] { "stEnabled", "catalogUrlOverride", "masterVolume", "voiceVolume", "bgmVolume", "soundEffectVolume", "lastProfileIndex", "username", "goldText", "diamondText", "staminaText", "autoSave", "autoCompile", "lobbyBgmEnabled", "autoDelay", "fpsTier", "checkForUpdate", "isAutoPlay", "fontIndex", "visitedFiles", "favoriteBgmIds", "favoriteSoundNames", "completeResStructVer", "customSource", "cachePath", "workspacePath" };
        var keys = types["UserSettings+SettingData"].Properties.Where(p => !p.Name.StartsWith('_')).Select(p => p.Name).ToHashSet();
        if (!keys.SetEquals(expectedKeys)) throw new Exception("Native settings property schema drift: " + string.Join(",", keys));
        Console.WriteLine("PASS exact 26-property native settings seed schema");
    }
}
Console.WriteLine("PASS metadata contracts; no AA/Unity or IL2CPP native method executed");
using (var core = ModuleDefinition.ReadModule(Path.Combine(args[0], "BepInEx", "core", "BepInEx.Core.dll")))
{
    var type = core.Types.Single(t => t.FullName == "BepInEx.Bootstrap.BaseChainloader`1");
    var execute = type.Methods.Single(m => m.Name == "Execute" && m.IsPublic && !m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Void");
    Console.WriteLine($"PASS guardian managed Execute binding: token={execute.MetadataToken}, generic BaseChainloader<TPlugin>");
}
using (var preloader = ModuleDefinition.ReadModule(Path.Combine(args[0], "BepInEx", "core", "BepInEx.Preloader.Core.dll")))
{
    var type = preloader.Types.Single(t => t.FullName == "BepInEx.Preloader.Core.Patching.BasePatcher");
    var finalizer = type.Methods.Single(m => m.Name == "Finalizer" && m.IsPublic && m.IsVirtual && !m.IsStatic && m.Parameters.Count == 0);
    if (!preloader.Types.Any(t => t.FullName == "BepInEx.Preloader.Core.Patching.PatcherPluginInfoAttribute")) throw new Exception("Guardian patcher attribute drift.");
    Console.WriteLine($"PASS guardian managed preloader Finalizer and discovery attribute: token={finalizer.MetadataToken}");
}

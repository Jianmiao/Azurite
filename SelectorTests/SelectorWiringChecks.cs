using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class SelectorWiringChecks
{
    internal static void Run(string pluginPath, Action<bool,string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(pluginPath);
        var plugin = assembly.MainModule.GetType("Azurite.Plugin");
        var policy = assembly.MainModule.GetType("Azurite.DeferredSelectorPolicy");
        MethodDefinition Method(TypeDefinition t, string name) => t.Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition m, string type, string name) => m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.FullName == type && r.Name == name);
        int At(MethodDefinition m, string type, string name) => m.Body.Instructions.ToList().FindIndex(i => i.Operand is MethodReference r && r.DeclaringType.FullName == type && r.Name == name);
        var load = Method(plugin, "Load"); var tick = Method(plugin, "Tick");
        check(plugin.Fields.Any(f => f.Name == "_deferredSelectorsEnabled") && Calls(load,"Azurite.DeferredSelectorPolicy","Bind"), "compiled Plugin binds its independent selector setting");
        check(Calls(load,"Azurite.DeferredSelectorPolicy","Install") && load.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldfld && i.Operand is FieldReference f && f.Name == "_deferredSelectorsEnabled"), "compiled startup passes configured selector value to production install seam");
        int publish = load.Body.Instructions.ToList().FindIndex(i => i.OpCode.Code == Code.Stfld && i.Operand is FieldReference f && f.Name == "_deferredSelectors");
        check(publish >= 0 && publish < At(load,"Azurite.DeferredSelectorPolicy","Install"), "compiled startup publishes controller before hooks so export callbacks can pause it");
        var firstCall = tick.Body.Instructions.FirstOrDefault(i => i.Operand is MethodReference)?.Operand as MethodReference;
        check(firstCall?.DeclaringType.FullName == "Azurite.DeferredSelectorPolicy" && firstCall.Name == "Pause", "compiled Tick pauses new deferral before any early-return gate");
        int apply = At(tick,"Azurite.DeferredSelectorPolicy","Apply");
        check(apply > At(tick,"BepInEx.Configuration.ConfigFile","Reload") && At(tick,"BepInEx.Configuration.ConfigFile","Reload") >= 0 && apply > At(tick,"Azurite.HostCompatibility","Probe") && At(tick,"Azurite.HostCompatibility","Probe") >= 0, "compiled Tick applies live configuration after reload and fresh host probe");
        int probe = At(tick,"Azurite.HostCompatibility","Probe");
        var arguments = apply > probe && probe >= 0 ? tick.Body.Instructions.Skip(probe+1).Take(apply-probe-1).ToArray() : Array.Empty<Instruction>();
        check(arguments.Where(i => i.OpCode.Code == Code.Ldfld && i.Operand is FieldReference).Select(i => ((FieldReference)i.Operand).Name).SequenceEqual(new[]{"_deferredSelectors","_deferredSelectorsEnabled","_enabled","_disabledForSession","_host","_exportSuspended"}) && arguments.Count(i => i.Operand is MethodReference r && r.Name == "get_Value" && r.DeclaringType.Name == "ConfigEntry`1") == 2, "compiled Tick forwards the live independent/global values and session/export fields in order");
        var suspend = Method(plugin,"SuspendForExport");
        check(At(suspend,"Azurite.DeferredSelectorPolicy","Pause") >= 0 && At(suspend,"Azurite.DeferredSelectorPolicy","Pause") < At(suspend,"Azurite.Plugin","RestoreRendering"), "compiled export callback pauses selectors before restore callbacks");
        check(Calls(Method(plugin,"YieldToExporter"),"Azurite.DeferredSelectorPolicy","Pause"), "compiled late exporter ownership also pauses selectors");
        if (policy == null) { check(false,"compiled production selector policy exists"); return; }
        var bind = Method(policy,"Bind"); var call = bind.Body.Instructions.ToList().FindIndex(i => i.Operand is GenericInstanceMethod g && g.DeclaringType.FullName == "BepInEx.Configuration.ConfigFile" && g.Name == "Bind" && g.GenericArguments.Single().FullName == "System.Boolean");
        check(call >= 4 && bind.Body.Instructions[call-4].Operand as string == "Large Projects" && bind.Body.Instructions[call-3].Operand as string == "DeferEditorSelectors" && bind.Body.Instructions[call-2].OpCode.Code == Code.Ldc_I4_0, "compiled real BepInEx Bind call defaults Large Projects/DeferEditorSelectors to false");
        var install = Method(policy,"Install");
        check(At(install,"Azurite.DeferredSelectorPolicy","Apply") >= 0 && At(install,"Azurite.DeferredSelectorPolicy","Apply") < At(install,"Azurite.DeferredEditorSelectors","Install"), "compiled initial policy assignment precedes native hook installation");
        check(!Calls(Method(policy,"Apply"),"Azurite.DeferredEditorSelectors","Dispose") && !Calls(Method(policy,"Pause"),"Azurite.DeferredEditorSelectors","Dispose") && !Calls(Method(policy,"Pause"),"Azurite.DeferredEditorSelectors","AbandonForShutdown"), "compiled live policy retains pending controller state");
    }
}

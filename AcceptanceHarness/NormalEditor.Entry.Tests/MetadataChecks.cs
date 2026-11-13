using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

static class MetadataChecks
{
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    internal static void Run(string inputs,string hostPath,Action<string,Action> test,Action<bool,string> check)
    {
        // Read PE/IL as data, without loading target assemblies or running their
        // type initializers, Harmony patches, native getters or callbacks.
        check(Path.GetFullPath(inputs).StartsWith("C:\\",StringComparison.Ordinal)&&Path.GetFullPath(hostPath).StartsWith("C:\\",StringComparison.Ordinal),"Metadata checks are C-only");
        test("Cecil is the reviewed C metadata reader",()=>check(Hash(typeof(AssemblyDefinition).Assembly.Location)=="769a59793d4b8885bbbfbc5aee8f57a0d4e34d275c56c60c03994309b87f67e9","Cecil changed"));
        var csharpPath=Path.Combine(inputs,"BepInEx/interop/Assembly-CSharp.dll");
        var enginePath=Path.Combine(inputs,"BepInEx/interop/UnityEngine.CoreModule.dll");
        test("C host inputs match exact frozen hashes",()=>{check(Hash(csharpPath)=="d95062f3a49fba645986dc7fe0cee749197c9d0c84ff381fc3ca2d9d2823c013","CSharp changed");check(Hash(enginePath)=="fd65570bc81061e45fd9ce3bd162cd5839e0efb8f4d76f6902ead8232cd69e69","CoreModule changed");});
        using var csharp=AssemblyDefinition.ReadAssembly(csharpPath);using var engine=AssemblyDefinition.ReadAssembly(enginePath);using var host=AssemblyDefinition.ReadAssembly(hostPath);
        foreach(var spec in new[]{("Catalog","Start","NativeMethodInfoPtr_Start_Private_Void_0",100665977,false),("AzureArchive.Automation.AuthoringWorkbench","Update","NativeMethodInfoPtr_Update_Private_Void_0",100695327,false),("AzureArchive.Automation.AuthoringWorkbench","RequireCatalogIdleForControl","NativeMethodInfoPtr_RequireCatalogIdleForControl_Private_Static_Void_0",100695332,true)})test("exact native wrapper signature and token "+spec.Item1+"."+spec.Item2,()=>{
            var type=csharp.MainModule.GetType(spec.Item1);var method=type.Methods.Single(m=>m.Name==spec.Item2&&m.IsStatic==spec.Item5&&m.ReturnType.FullName=="System.Void"&&m.Parameters.Count==0);
            check(method.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldsfld&&i.Operand is FieldReference f&&f.Name==spec.Item3),"Wrapper does not invoke selected pointer");var il=type.Methods.Single(m=>m.IsConstructor&&m.IsStatic).Body.Instructions;var at=Enumerable.Range(0,il.Count).Single(i=>il[i].OpCode.Code==Code.Stsfld&&il[i].Operand is FieldReference f&&f.Name==spec.Item3);check(il[at-2].OpCode.Code==Code.Ldc_I4&&(int)il[at-2].Operand==spec.Item4&&il[at-1].Operand is MethodReference call&&call.Name=="GetIl2CppMethodByToken","Metadata native token differs");
        });
        foreach(var field in new[]{("Singleton`1","_Instance_k__BackingField"),("AzureArchive.Automation.AuthoringWorkbench","_Instance_k__BackingField"),("AzureArchive.Automation.AuthoringWorkbench","controlOpening"),("AzureArchive.Automation.AuthoringCliHost","_IsActive_k__BackingField"),("AzureArchive.Automation.AuthoringEditorSession","_Current_k__BackingField"),("UserSettings","_isReady_k__BackingField"),("ScenarioResourceManager","_AreDbsLoaded_k__BackingField"),("ScenarioResourceManager","_Preloading_k__BackingField"),("Loading","loadingCount")})test("observed field has no native method getter "+field.Item1+"."+field.Item2,()=>{
            var getter=csharp.MainModule.GetType(field.Item1).Properties.Single(p=>p.Name==field.Item2).GetMethod;var calls=getter.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();check(!calls.Any(m=>m.Name=="il2cpp_runtime_invoke"||m.Name=="il2cpp_object_new"),"Field accessor performs a native method/factory invocation");check(getter.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name.StartsWith("NativeFieldInfoPtr_",StringComparison.Ordinal)),"Not a generated native field accessor");
        });
        IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));var types=host.MainModule.Types.SelectMany(All).ToArray();var methods=types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();var callsites=methods.SelectMany(m=>m.Body.Instructions).Where(i=>i.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj).Select(i=>i.Operand).OfType<MethodReference>().ToArray();
        var callbacks=types.Single(t=>t.Name=="CatalogEntryHost");
        test("registration requires the caller's shared clock anchor",()=>{
            var registrations=callbacks.Methods.Where(m=>m.Name=="Register").ToArray();
            check(registrations.Length==1&&registrations[0].Parameters.Any(p=>p.Name=="clock"&&p.ParameterType.FullName=="Azurite.NormalEditor.Entry.CatalogClockAnchor"&&!p.IsOptional),"Registration can start an uncorrelated observer clock");
        });
        test("host stamps use the supplied anchor without starting another clock",()=>{
            check(!callsites.Any(m=>m.DeclaringType.FullName=="System.Diagnostics.Stopwatch"&&(m.Name=="StartNew"||m.Name==".ctor")),"Host creates an independent stopwatch");
            check(!callsites.Any(m=>m.DeclaringType.FullName=="Azurite.NormalEditor.Entry.CatalogClockAnchor"&&(m.Name=="Capture"||m.Name==".ctor")),"Host replaces the caller's clock anchor");
            check(callsites.Any(m=>m.DeclaringType.FullName=="Azurite.NormalEditor.Entry.CatalogClockAnchor"&&m.Name=="ElapsedMillisecondsAt"),"Host does not derive stamps from the shared anchor");
        });
        test("invalid clock is checked before native binding can initialize classes",()=>{
            var il=callbacks.Methods.Single(m=>m.Name=="Register").Body.Instructions;
            var clockAt=Enumerable.Range(0,il.Count).Single(i=>il[i].Operand is MethodReference m&&m.DeclaringType.Name=="CatalogClockAnchor"&&m.Name=="ElapsedMillisecondsAt");
            var bindAt=Enumerable.Range(0,il.Count).Single(i=>il[i].Operand is MethodReference m&&m.DeclaringType.Name=="CatalogNativeBindings"&&m.Name=="Bind");
            check(clockAt<bindAt,"Invalid clock can reach native binding");
        });
        test("failure evidence does not sample the clock that may have failed",()=>{
            var fail=callbacks.Methods.Single(m=>m.Name=="Fail");var last=callbacks.Methods.Single(m=>m.Name=="LastStamp");
            check(fail.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="LastStamp"),"Failure evidence does not preserve its last valid timestamp");
            check(!fail.Body.Instructions.Concat(last.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>().Any(m=>m.DeclaringType.Name is "CatalogClockAnchor" or "Stopwatch"||m.Name=="FallbackStamp"),"Failure reporting reads a failed clock again");
        });
        test("host has no automatic entry or permitting admission",()=>{
            check(!types.SelectMany(t=>t.CustomAttributes).Any(a=>a.AttributeType.Name.Contains("BepInPlugin"))&&!methods.SelectMany(m=>m.CustomAttributes).Any(a=>a.AttributeType.Name.Contains("ModuleInitializer")),"Automatic entry exists");check(!host.MainModule.Types.Single(t=>t.Name=="<Module>").Methods.Any(m=>m.Name==".cctor"),"Module initializer exists");check(!types.SelectMany(t=>t.Interfaces).Any(i=>i.InterfaceType.FullName=="Azurite.NormalEditor.Adapter.IAdapterAdmission"),"Observer grants its own admission");
        });
        test("host observes without invoking native lifecycle or writing readiness/session fields",()=>{
            check(!callsites.Any(m=>m.Name is "OpenProject" or "OpenControlled" or "Begin" or "StartCoroutine" or "LoadSceneAsync" or "il2cpp_runtime_invoke" or "il2cpp_object_new" or "il2cpp_gc_wbarrier_set_field" || (m.Name=="MoveNext"&&!m.DeclaringType.FullName.StartsWith("System.Collections.",StringComparison.Ordinal))),"Forbidden lifecycle/entry/native write call");check(!callsites.Any(m=>(m.Name=="Start"||m.Name=="Update")&&(m.DeclaringType.Name=="Catalog"||m.DeclaringType.Name=="AuthoringWorkbench")),"Observer manually drives Start/Update");check(!callsites.Any(m=>m.Name.StartsWith("set_",StringComparison.Ordinal)&&(m.DeclaringType.Namespace=="AzureArchive.Automation"||m.DeclaringType.Name is "UserSettings" or "ScenarioResourceManager")),"Host state setter found");
        });
        test("callbacks preserve original execution and finalizer exception",()=>{
            foreach(var name in new[]{"StartPrefix","UpdatePrefix"})check(callbacks.Methods.Single(m=>m.Name==name).ReturnType.FullName=="System.Void","Prefix can suppress original");
            foreach(var name in new[]{"StartPostfix","UpdatePostfix"})check(callbacks.Methods.Single(m=>m.Name==name).Parameters.Any(p=>p.Name=="__runOriginal"&&p.ParameterType.FullName=="System.Boolean"),"Missing original-run flag");
            foreach(var name in new[]{"StartFinalizer","UpdateFinalizer"}){var method=callbacks.Methods.Single(m=>m.Name==name);check(method.ReturnType.FullName=="System.Exception"&&method.Parameters.Any(p=>p.Name=="__exception"&&p.ParameterType.FullName=="System.Exception")&&method.Parameters.Any(p=>p.Name=="__originalMethod")&&method.Parameters.Any(p=>p.Name=="__state"),"Missing actual method/state/original exception");}
        });
        test("natural Start callbacks feed tested coordinator, ready check occurs after Update finalization",()=>{
            foreach(var pair in new[]{("StartPrefix","StartEntered"),("StartPostfix","StartReturned"),("StartFinalizer","StartFinalized"),("UpdateFinalizer","ObserveTick")})check(callbacks.Methods.Single(m=>m.Name==pair.Item1).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Azurite.NormalEditor.Entry.CatalogReadiness"&&m.Name==pair.Item2),"Callback bypassed readiness coordinator: "+pair.Item1);
            foreach(var pair in new[]{("UpdatePrefix","Enter"),("UpdatePostfix","Returned"),("UpdateFinalizer","TryComplete")})check(callbacks.Methods.Single(m=>m.Name==pair.Item1).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Azurite.NormalEditor.Entry.CatalogTickCallbacks"&&m.Name==pair.Item2),"Callback bypassed tested Update lifecycle: "+pair.Item1);
            check(!callbacks.Methods.Single(m=>m.Name=="UpdatePostfix").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Tick"),"Postfix accepted before native finalizer");
            check(!callbacks.Methods.Single(m=>m.Name=="UpdateFinalizer").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Observe"),"Host read native fields before the deferred observation bridge");
        });
        test("observation accesses only backing owners and ready fields",()=>{
            var observe=callbacks.Methods.Single(m=>m.Name=="Observe");var calls=observe.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();check(!calls.Any(m=>m.Name is "get_Instance" or "get_Current" or "get_IsActive" or "get_isReady" or "get_AreDbsLoaded" or "get_Preloading"),"Convenience/native getter could materialize observed state");check(calls.Any(m=>m.Name=="get__Current_k__BackingField")&&calls.Any(m=>m.Name=="get__Instance_k__BackingField")&&calls.Any(m=>m.Name=="get__isReady_k__BackingField"),"Actual backing observation absent");
        });
        test("single native idle guard is consumed by deferred guard readback",()=>check(callsites.Count(m=>m.DeclaringType.FullName=="AzureArchive.Automation.AuthoringWorkbench"&&m.Name=="RequireCatalogIdleForControl")==1,"Expected exactly one native guard invocation site"));
    }
}

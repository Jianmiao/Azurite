using Mono.Cecil;
using System.Text.Json;

static class ShutdownHostMetadataChecks
{
    internal static int Run(string entryPath,string adapterPath)
    {
        // PE/IL only. Never load the Host assemblies or invoke their methods.
        foreach(string path in new[]{entryPath,adapterPath})if(!Path.GetFullPath(path).StartsWith("C:\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("C-only compiled Host proof required.");
        using var entry=AssemblyDefinition.ReadAssembly(entryPath);using var adapter=AssemblyDefinition.ReadAssembly(adapterPath);
        var entryType=entry.MainModule.Types.Single(t=>t.Name=="CatalogEntryHost");var adapterType=adapter.MainModule.Types.Single(t=>t.Name=="NativeAdapterHost");
        IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
        var entryMethods=entry.MainModule.Types.SelectMany(All).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
        MethodReference[] Calls(MethodDefinition m)=>m.HasBody?m.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray():Array.Empty<MethodReference>();
        var allCalls=entryMethods.SelectMany(Calls).ToArray();
        var checks=new List<object>();void Need(bool c,string why){if(!c)throw new Exception(why);}
        void Test(string name,Action action){try{action();checks.Add(new{name,passed=true,error=(string?)null});}catch(Exception error){checks.Add(new{name,passed=false,error=error.Message});}}
        Test("actual Catalog Host exposes only explicit Close delegated to tested shutdown",()=>{
            var close=entryType.Methods.SingleOrDefault(m=>m.Name=="Close");Need(close is not null&&close.IsPublic&&close.IsStatic&&close.Parameters.Count==0&&close.ReturnType.FullName=="System.Boolean","explicit Close missing");
            Need(Calls(close!).Any(m=>m.DeclaringType.Name=="CatalogShutdown"&&m.Name=="TryClose"),"Host bypasses tested shutdown boundary");
            Need(allCalls.Any(m=>m.DeclaringType.Name=="NativeAdapterHost"&&m.Name=="Close"&&m.Parameters.Count==2),"Host does not close its binding/lease-specific native adapter");
        });
        Test("actual Adapter Host Close delegates real coordinator and checks binding lease identity",()=>{
            var close=adapterType.Methods.SingleOrDefault(m=>m.Name=="Close");Need(close is not null&&close.IsPublic&&close.IsStatic&&close.Parameters.Count==2&&close.ReturnType.FullName=="System.Boolean","adapter Close missing");
            var calls=Calls(close!);Need(calls.Any(m=>m.DeclaringType.Name=="AdapterCoordinator"&&m.Name=="Close"),"real AdapterCoordinator.Close not called");
            // Release lowers ReferenceEquals to object-reference bne.un, not a
            // method call. Check its argument/field operands and throw target.
            var il=close!.Body.Instructions;
            bool ReferenceGuard(Mono.Cecil.Cil.Code argument,string field)=>Enumerable.Range(0,Math.Max(0,il.Count-3)).Any(i=>
                il[i].OpCode.Code==argument&&il[i+1].OpCode.Code==Mono.Cecil.Cil.Code.Ldloc_0&&
                il[i+2].Operand is FieldReference f&&f.Name==field&&f.DeclaringType.Name=="State"&&
                il[i+3].OpCode.Code is Mono.Cecil.Cil.Code.Bne_Un or Mono.Cecil.Cil.Code.Bne_Un_S&&
                il[i+3].Operand is Mono.Cecil.Cil.Instruction target&&target.Next?.Next?.OpCode.Code==Mono.Cecil.Cil.Code.Throw);
            Need(ReferenceGuard(Mono.Cecil.Cil.Code.Ldarg_0,"Binding")&&ReferenceGuard(Mono.Cecil.Cil.Code.Ldarg_1,"Admission")&&calls.Any(m=>m.Name=="VerifyOwnership"),"close is not bound to exact registered binding/lease and patches");
        });
        Test("all actual Catalog hook bodies enter lifetime scope before native inspection",()=>{
            foreach(string name in new[]{"StartPrefix","StartPostfix","StartFinalizer","UpdatePrefix","UpdatePostfix","UpdateFinalizer"}){
                var method=entryType.Methods.Single(m=>m.Name==name);var calls=Calls(method);
                int enter=Array.FindIndex(calls,m=>m.Name=="EnterCallback");int verify=Array.FindIndex(calls,m=>m.Name=="Callback");
                Need(enter>=0&&verify>enter,"missing early scope in "+name);
                Need(!calls.Take(enter).Any(m=>m.DeclaringType.Namespace is "UnityEngine" or "AzureArchive.Automation"||m.Name=="get_Pointer"),"native access before scope in "+name);
                Need(method.Body.ExceptionHandlers.Any(h=>h.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Finally),"scope not retired on exception: "+name);
            }
            var helper=entryType.Methods.Single(m=>m.Name=="EnterCallback");Need(Calls(helper).Any(m=>m.DeclaringType.Name=="CatalogShutdown"&&m.Name=="TryEnter"),"helper bypasses actual production lifetime");
        });
        Test("complete registration marks lifetime only after patch verification",()=>{
            var calls=Calls(entryType.Methods.Single(m=>m.Name=="Register"));int verify=Array.FindIndex(calls,m=>m.Name=="Verify"),mark=Array.FindIndex(calls,m=>m.DeclaringType.Name=="CatalogShutdown"&&m.Name=="MarkRegistered");Need(verify>=0&&mark>verify,"registration marked before verification or not marked");
            Need(allCalls.Any(m=>m.DeclaringType.Name=="CatalogShutdown"&&m.Name==".ctor"),"Host does not own tested shutdown instance");
        });
        Test("shutdown adds no automatic entry native exit unpatch or journal seal",()=>{
            Need(!allCalls.Any(m=>m.Name is "Quit" or "Seal" or "Unpatch" or "UnpatchAll" or "OpenProject" or "Begin"),"shutdown drove native lifecycle/terminal/patch teardown");
            Need(!entry.MainModule.Types.SelectMany(All).SelectMany(t=>t.CustomAttributes).Any(a=>a.AttributeType.Name.Contains("BepInPlugin")),"automatic plugin entry added");
            Need(!entry.MainModule.Types.SelectMany(All).SelectMany(t=>t.Interfaces).Any(i=>i.InterfaceType.Name=="IAdapterAdmission"),"entry grants its own admission");
        });
        Console.WriteLine(JsonSerializer.Serialize(checks));int failed=checks.Count(x=>!(bool)x.GetType().GetProperty("passed")!.GetValue(x)!);Console.WriteLine($"RESULT passed={checks.Count-failed} failed={failed}; nativeExecuted=false; hostExecuted=false; metadataOnly=true");return failed==0?0:1;
    }
}

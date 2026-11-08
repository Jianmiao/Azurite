using Mono.Cecil;
using Mono.Cecil.Cil;
using Azurite.NormalEditor.Adapter;

static class BindingMetadataTests
{
    internal static void Run(string install,string hostPath,Action<string,Action> test,Action<bool,string> check)
    {
        // Cecil reads PE/IL as data. No Assembly.Load, target CLR type resolution,
        // RuntimeHelpers.RunClassConstructor or IL2CPP entry point is used here.
        check(Fixture.Hash(typeof(AssemblyDefinition).Assembly.Location)=="769a59793d4b8885bbbfbc5aee8f57a0d4e34d275c56c60c03994309b87f67e9","Cecil input differs");
        test("metadata input Assembly-CSharp is the pinned current host",()=>check(Fixture.Hash(Path.Combine(install,"BepInEx/interop/Assembly-CSharp.dll"))=="d95062f3a49fba645986dc7fe0cee749197c9d0c84ff381fc3ca2d9d2823c013","CSharp input differs"));
        test("metadata input CoreModule is the pinned current host",()=>check(Fixture.Hash(Path.Combine(install,"BepInEx/interop/UnityEngine.CoreModule.dll"))=="fd65570bc81061e45fd9ce3bd162cd5839e0efb8f4d76f6902ead8232cd69e69","CoreModule input differs"));
        var parameters=new ReaderParameters{ReadingMode=ReadingMode.Deferred,ReadSymbols=false};
        using var csharp=AssemblyDefinition.ReadAssembly(Path.Combine(install,"BepInEx/interop/Assembly-CSharp.dll"),parameters);
        using var engine=AssemblyDefinition.ReadAssembly(Path.Combine(install,"BepInEx/interop/UnityEngine.CoreModule.dll"),parameters);
        using var host=AssemblyDefinition.ReadAssembly(hostPath,parameters);
        using var core=AssemblyDefinition.ReadAssembly(typeof(AdapterCoordinator).Assembly.Location,parameters);
        TypeDefinition Type(string name)=> (name.StartsWith("UnityEngine.")?engine:csharp).MainModule.GetType(name.Replace('+','/'))??throw new Exception("Missing type "+name);
        foreach(var spec in BindingCatalog.Methods)
        {
            test("metadata method "+spec.Id,()=>{
                var type=Type(spec.Type);var field=type.Fields.Single(f=>f.Name==spec.PointerField);check(field.IsStatic && field.IsInitOnly && field.FieldType.FullName=="System.IntPtr","pointer field shape differs");
                var method=type.Methods.Single(m=>m.Name==spec.Method && !m.IsStatic && m.ReturnType.FullName==spec.ReturnType && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(spec.Parameters));
                check(method.HasBody && method.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldsfld && i.Operand is FieldReference f && f.Name==spec.PointerField),"selected overload does not use the expected field");
                var il=type.Methods.Single(m=>m.IsConstructor && m.IsStatic).Body.Instructions;
                var at=Enumerable.Range(0,il.Count).Single(i=>il[i].OpCode.Code==Code.Stsfld && il[i].Operand is FieldReference f && f.Name==spec.PointerField);
                check(at>=2 && il[at-1].Operand is MethodReference call && call.Name=="GetIl2CppMethodByToken" && il[at-2].OpCode.Code==Code.Ldc_I4 && (int)il[at-2].Operand==spec.Token,"native metadata token initializer differs");
            });
        }
        foreach(var spec in BindingCatalog.Fields)
        {
            test("metadata field "+spec.Type+"."+spec.NativeName,()=>{
                var type=Type(spec.Type);var pointer=type.Fields.Single(f=>f.Name==spec.PointerField);check(pointer.IsStatic && pointer.IsInitOnly && pointer.FieldType.FullName=="System.IntPtr","field pointer shape differs");
                var property=type.Properties.Single(p=>p.Name==spec.Property);check(property.PropertyType.FullName==spec.PropertyType && property.GetMethod.IsStatic==spec.Static,"field accessor type/staticness differs");
                check(property.GetMethod.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldsfld && i.Operand is FieldReference f && f.Name==spec.PointerField),"accessor lacks its expected native field");
                var il=type.Methods.Single(m=>m.IsConstructor && m.IsStatic).Body.Instructions;
                var at=Enumerable.Range(0,il.Count).Single(i=>il[i].OpCode.Code==Code.Stsfld && il[i].Operand is FieldReference f && f.Name==spec.PointerField);
                check(at>=2 && il[at-1].Operand is MethodReference call && call.Name=="GetIl2CppField" && il[at-2].OpCode.Code==Code.Ldstr && (string)il[at-2].Operand==spec.NativeName,"native field lookup name differs");
            });
        }
        IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
        var hostTypes=host.MainModule.Types.SelectMany(All).ToArray();var hostMethods=hostTypes.SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
        test("host has no plugin or module initializer automatic entry",()=>{
            check(!hostTypes.SelectMany(t=>t.CustomAttributes).Any(a=>a.AttributeType.FullName.Contains("BepInPlugin")),"automatic plugin attribute present");
            check(!hostMethods.SelectMany(m=>m.CustomAttributes).Any(a=>a.AttributeType.FullName.Contains("ModuleInitializer")),"module initializer present");
            check(!host.MainModule.Types.Single(t=>t.Name=="<Module>").Methods.Any(m=>m.Name==".cctor"),"module startup body present");
        });
        test("host callback posts carry actual run-original and bool results",()=>{
            var callbacks=hostTypes.Single(t=>t.Name=="NativeAdapterHost");
            foreach(var name in new[]{"AwakePostfix","CoroutinePostfix","InitPostfix","ReadPostfix","SavePostfix","WritePostfix","ResourcePostfix"})
            {var m=callbacks.Methods.Single(m=>m.Name==name);check(m.Parameters.Any(p=>p.Name=="__runOriginal" && p.ParameterType.FullName=="System.Boolean"),"missing original-run signal");check(m.Parameters.Any(p=>p.Name=="__state" && p.ParameterType.FullName==typeof(AdapterFrame).FullName),"missing immutable frame");if(name is "InitPostfix" or "ReadPostfix" or "SavePostfix")check(m.Parameters.Any(p=>p.Name=="__result" && p.ParameterType.FullName=="System.Boolean"),"missing actual iterator return");}
        });
        test("host has no native constructor factory or manual MoveNext calls",()=>{
            var calls=hostMethods.SelectMany(m=>m.Body.Instructions).Where(i=>i.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj).Select(i=>i.Operand).OfType<MethodReference>().ToArray();
            check(!calls.Any(m=>m.Name is "il2cpp_object_new" or "il2cpp_runtime_invoke" || (m.DeclaringType.FullName.StartsWith("UserSettings") && (m.Name is ".ctor" or "MoveNext" or "SaveTask" or "ReadUserSettings" or "InitTask"))),"manual native execution/allocation helper present");
            check(calls.Count(m=>m.Name=="il2cpp_gc_wbarrier_set_field")==1,"unexpected native write helper");
        });
        test("host common finalizer supplies actual method receiver and state to tested dispatch",()=>{
            var method=hostTypes.Single(t=>t.Name=="NativeAdapterHost").Methods.Single(m=>m.Name=="OriginalFinalizer");
            check(method.Parameters.Any(p=>p.Name=="__originalMethod" && p.ParameterType.FullName=="System.Reflection.MethodBase"),"actual method identity missing");
            check(method.Parameters.Any(p=>p.Name=="__instance" && p.ParameterType.FullName=="Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase"),"actual receiver identity missing");
            check(method.Parameters.Any(p=>p.Name=="__state" && p.ParameterType.FullName==typeof(AdapterFrame).FullName),"callback state missing");
            check(method.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.FullName==typeof(FinalizerDispatch).FullName && m.Name==nameof(FinalizerDispatch.Complete)),"host bypasses the tested common dispatch");
        });
        test("callback finalizer identity and disposition are immutable and internally issued",()=>{
            var type=core.MainModule.GetType(typeof(AdapterFrame).FullName);
            check(!type.Methods.Any(m=>m.IsConstructor && !m.IsStatic && m.IsPublic),"callback identity can be publicly constructed");
            foreach(var name in new[]{nameof(AdapterFrame.Kind),nameof(AdapterFrame.Instance),nameof(AdapterFrame.Disposition),nameof(AdapterFrame.ManagedThreadId)})
            { var property=type.Properties.Single(p=>p.Name==name);check(property.GetMethod is not null && property.SetMethod is null,"mutable callback identity/disposition"); }
        });
        test("preparation libraries supply no permitting admission implementation",()=>{
            foreach(var assembly in new[]{core,host})check(!assembly.MainModule.Types.SelectMany(All).Where(t=>!t.IsInterface).SelectMany(t=>t.Interfaces).Any(i=>i.InterfaceType.FullName is "Azurite.NormalEditor.Adapter.IAdapterAdmission" or "Azurite.NormalEditor.INormalAdmissionLease"),"production permitting lease present");
        });
        test("shared aliases keep exact separately pinned token identities",()=>{var a=BindingCatalog.Methods.Single(x=>x.Id=="coroutine");var b=BindingCatalog.Methods.Single(x=>x.Id=="coroutineAlias");check(a.Token==100667066 && b.Token==100667067 && a.Rva==0x3408550 && b.Rva==a.Rva,"shared binding differs from captured native evidence");});
    }
}

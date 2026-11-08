using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Azurite.EditorIsolation;
using Azurite.NormalEditor;
using Il2CppInterop.Runtime;

namespace Azurite.NormalEditor.Adapter.Host;

internal sealed record BoundMethod(MethodBindingSpec Spec,MethodInfo Managed,IntPtr MethodInfo,IntPtr Entry);
internal sealed class NativeBindings
{
    internal readonly Dictionary<string,BoundMethod> Methods=new();
    internal readonly Dictionary<string,IntPtr> Classes=new();
    internal readonly Dictionary<(string,string),IntPtr> Fields=new();
    internal static void Hash(string path,string? expected)
    {
        if(expected is null || expected.Length!=64)throw new InvalidOperationException("Missing explicit binary pin.");
        RunPaths.RequireSafeExisting(path);
        if(!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Binary pin drift: "+Path.GetFileName(path));
    }
    internal static NativeBindings Bind(AdapterBinding binding,IAdapterAdmission admission,string evidenceHash)
    {
        void Admitted(){InstallationAuthority.RequireEvidence(evidenceHash,admission.EvidenceSha256);admission.RequireInstallation(binding);InstallationAuthority.RequireEvidence(evidenceHash,admission.EvidenceSha256);}
        Admitted(); // Before any target class initialization or field lookup.
        using var process=Process.GetCurrentProcess();var install=binding.Policy.Paths.Install;
        InstallationAuthority.Require(binding,process.MainModule?.FileName??throw new InvalidOperationException("Missing current executable identity."),typeof(NativeAdapterHost).Assembly.Location);
        var used=new[]{"GameAssembly.dll","UnityPlayer.dll","AzureArchive_Data/il2cpp_data/Metadata/global-metadata.dat","BepInEx/interop/Assembly-CSharp.dll","BepInEx/interop/UnityEngine.CoreModule.dll","BepInEx/core/0Harmony.dll","BepInEx/core/Il2CppInterop.Runtime.dll","BepInEx/core/Il2CppInterop.HarmonySupport.dll"};
        foreach(var path in used)Hash(Path.Combine(install,path.Replace('/',Path.DirectorySeparatorChar)),HostContract.Files[path]);
        Hash(Path.Combine(install,"AzureArchive.exe"),"260fc3ecc5f38dab5230ba624a52e495708d861196dda72486c1250760fe6eff");
        Hash(Path.Combine(install,"BepInEx/interop/Il2Cppmscorlib.dll"),"07ac56bc73cafaaf853010a1a2418beb47ceaa792c2cec0fc8692db65f90491a");
        Hash(Path.Combine(install,"BepInEx/core/Il2CppInterop.Common.dll"),"070e04f2276a8963a6d2a7eb52d43b644f3ae9aa3816c34541124d3862f1b37f");
        var deployment=Path.Combine(install,InstallationAuthority.AdapterSubdirectory);
        foreach(var (assembly,expected,hash) in new[]{
            (typeof(NativeAdapterHost).Assembly,Path.Combine(deployment,"Azurite.NormalEditor.Adapter.Host.PreparationOnly.dll"),binding.HostAssemblySha256),
            (typeof(AdapterCoordinator).Assembly,Path.Combine(deployment,"Azurite.NormalEditor.Adapter.Core.PreparationOnly.dll"),binding.CoreAssemblySha256),
            (typeof(NormalSettingsLifecycle).Assembly,Path.Combine(deployment,"Azurite.NormalEditor.Policy.PreparationOnly.dll"),(string?)BindingCatalog.NormalPolicyHash),
            (typeof(RunPaths).Assembly,Path.Combine(deployment,"Azurite.EditorIsolation.Policy.LabOnly.dll"),(string?)BindingCatalog.LegacyPolicyHash)})
        { if(!RunPaths.Same(assembly.Location,expected))throw new InvalidOperationException("Unpinned adapter/policy load path.");Hash(expected,hash); }
        foreach(var assembly in new[]{typeof(UserSettings).Assembly,typeof(UnityEngine.MonoBehaviour).Assembly,typeof(Il2CppSystem.Object).Assembly})
            if(!RunPaths.Same(assembly.Location,Path.Combine(install,"BepInEx/interop",assembly.GetName().Name+".dll")))throw new InvalidOperationException("Unpinned interop load location.");
        foreach(var name in new[]{"0Harmony","Il2CppInterop.Runtime","Il2CppInterop.Common","Il2CppInterop.HarmonySupport"})
        {
            var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name==name);
            if(!RunPaths.Same(assembly.Location,Path.Combine(install,"BepInEx/core",name+".dll")))throw new InvalidOperationException("Unpinned native patch backend location.");
        }
        var module=process.Modules.Cast<ProcessModule>().Single(m=>string.Equals(m.ModuleName,"GameAssembly.dll",StringComparison.OrdinalIgnoreCase));
        if(!RunPaths.Same(module.FileName,Path.Combine(install,"GameAssembly.dll")))throw new InvalidOperationException("Unpinned loaded native module.");
        var result=new NativeBindings();
        var assemblies=new Dictionary<string,Assembly>{{"Assembly-CSharp",typeof(UserSettings).Assembly},{"UnityEngine.CoreModule",typeof(UnityEngine.MonoBehaviour).Assembly}};
        var names=BindingCatalog.Methods.Select(x=>x.Type).Concat(BindingCatalog.Fields.Select(x=>x.Type)).Distinct();
        var types=names.ToDictionary(name=>name,name=>(name.StartsWith("UnityEngine.")?assemblies["UnityEngine.CoreModule"]:assemblies["Assembly-CSharp"]).GetType(name,true)!);
        foreach(var pair in types)
        {
            Admitted();
            RuntimeHelpers.RunClassConstructor(pair.Value.TypeHandle);
            var pointer=Il2CppClassPointerStore.GetNativeClassPointer(pair.Value);
            if(pointer==IntPtr.Zero)throw new InvalidOperationException("Missing native class.");result.Classes.Add(pair.Key,pointer);
        }
        foreach(var spec in BindingCatalog.Methods)
        {
            var type=types[spec.Type];var method=type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
                .Single(m=>m.Name==spec.Method && m.ReturnType.FullName==spec.ReturnType && m.GetParameters().Select(p=>p.ParameterType.FullName).SequenceEqual(spec.Parameters));
            var field=type.GetField(spec.PointerField,BindingFlags.Static|BindingFlags.NonPublic)??throw new MissingFieldException(spec.PointerField);
            var info=(IntPtr)field.GetValue(null)!;
            if(info==IntPtr.Zero || info!=IL2CPP.GetIl2CppMethodByToken(result.Classes[spec.Type],spec.Token))throw new InvalidOperationException("Native MethodInfo/token mismatch: "+spec.Id);
            var entry=Marshal.ReadIntPtr(info);if(entry.ToInt64()-module.BaseAddress.ToInt64()!=spec.Rva)throw new InvalidOperationException("Native entry RVA mismatch: "+spec.Id);
            result.Methods.Add(spec.Id,new(spec,method,info,entry));
        }
        foreach(var spec in BindingCatalog.Fields)
        {
            var field=types[spec.Type].GetField(spec.PointerField,BindingFlags.Static|BindingFlags.NonPublic)??throw new MissingFieldException(spec.PointerField);
            var info=(IntPtr)field.GetValue(null)!;
            if(info==IntPtr.Zero || info!=IL2CPP.GetIl2CppField(result.Classes[spec.Type],spec.NativeName))throw new InvalidOperationException("Native field identity mismatch.");
            if(spec.Offset is int offset && IL2CPP.il2cpp_field_get_offset(info)!=(uint)offset)throw new InvalidOperationException("Native field offset mismatch: "+spec.PointerField);
            if(((IL2CPP.il2cpp_field_get_flags(info)&0x10)!=0)!=spec.Static)throw new InvalidOperationException("Native field staticness mismatch.");
            result.Fields.Add((spec.Type,spec.NativeName),info);
        }
        if(result.Methods["coroutine"].Entry!=result.Methods["coroutineAlias"].Entry)throw new InvalidOperationException("Coroutine alias no longer shares its native entry.");
        return result;
    }
}

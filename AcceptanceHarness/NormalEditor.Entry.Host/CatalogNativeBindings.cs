using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AzureArchive.Automation;
using Azurite.EditorIsolation;
using Azurite.NormalEditor.Adapter;
using Azurite.NormalEditor.Adapter.Host;
using Il2CppInterop.Runtime;

namespace Azurite.NormalEditor.Entry.Host;

internal sealed class CatalogNativeBindings
{
    internal sealed record Method(Type Type,string Name,string PointerField,int Token,long Rva,bool Static);
    internal static readonly Method[] Methods={
        new(typeof(global::Catalog),"Start","NativeMethodInfoPtr_Start_Private_Void_0",100665977,0x9b5200,false),
        new(typeof(AuthoringWorkbench),"Update","NativeMethodInfoPtr_Update_Private_Void_0",100695327,0x952bd0,false),
        new(typeof(AuthoringWorkbench),"RequireCatalogIdleForControl","NativeMethodInfoPtr_RequireCatalogIdleForControl_Private_Static_Void_0",100695332,0x952240,true)};
    internal readonly Dictionary<string,MethodInfo> Bound=new();
    internal readonly Dictionary<Type,IntPtr> Classes=new();
    internal static void Pin(string path,string? digest)
    {
        if(digest is null||digest.Length!=64||!digest.All(Uri.IsHexDigit))throw new InvalidOperationException("Explicit compiled assembly digest required.");
        RunPaths.RequireSafeExisting(path);
        if(!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),digest,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Catalog observer binary pin drift: "+Path.GetFileName(path));
    }
    internal static CatalogNativeBindings Bind(AdapterBinding binding,IAdapterAdmission admission,string evidence,string hostHash,string coreHash)
    {
        void Admitted(){InstallationAuthority.RequireEvidence(evidence,admission.EvidenceSha256);admission.RequireInstallation(binding);InstallationAuthority.RequireEvidence(evidence,admission.EvidenceSha256);}
        Admitted();using var process=Process.GetCurrentProcess();var install=binding.Policy.Paths.Install;
        InstallationAuthority.Require(binding,process.MainModule?.FileName??throw new InvalidOperationException("No executable identity."),typeof(NativeAdapterHost).Assembly.Location);
        var deploy=Path.Combine(install,InstallationAuthority.AdapterSubdirectory);
        foreach(var item in new[]{
            (typeof(CatalogEntryHost).Assembly,hostHash),(typeof(CatalogReadiness).Assembly,coreHash),
            (typeof(AdapterCoordinator).Assembly,"2606a2c3ddbb9087e70c82bb5a16c776ae9c0c590e9b156fa1506e76abdb8264"),
            (typeof(NativeAdapterHost).Assembly,"9031e8ccc5945635dab48a2d8bcbc8a140304b32a9caf01608d7cd05e716eea7"),
            (typeof(NormalSettingsLifecycle).Assembly,BindingCatalog.NormalPolicyHash),(typeof(RunPaths).Assembly,BindingCatalog.LegacyPolicyHash)})
        {var path=Path.Combine(deploy,item.Item1.GetName().Name+".dll");if(!RunPaths.Same(path,item.Item1.Location))throw new InvalidOperationException("Catalog observer dependency load location differs.");Pin(path,item.Item2);}
        foreach(var relative in new[]{"GameAssembly.dll","UnityPlayer.dll","AzureArchive_Data/il2cpp_data/Metadata/global-metadata.dat","BepInEx/interop/Assembly-CSharp.dll","BepInEx/interop/UnityEngine.CoreModule.dll","BepInEx/core/0Harmony.dll","BepInEx/core/Il2CppInterop.Runtime.dll","BepInEx/core/Il2CppInterop.HarmonySupport.dll"})Pin(Path.Combine(install,relative.Replace('/',Path.DirectorySeparatorChar)),HostContract.Files[relative]);
        Pin(Path.Combine(install,"AzureArchive.exe"),"260fc3ecc5f38dab5230ba624a52e495708d861196dda72486c1250760fe6eff");
        Pin(Path.Combine(install,"BepInEx/interop/Il2Cppmscorlib.dll"),"07ac56bc73cafaaf853010a1a2418beb47ceaa792c2cec0fc8692db65f90491a");
        Pin(Path.Combine(install,"BepInEx/core/Il2CppInterop.Common.dll"),"070e04f2276a8963a6d2a7eb52d43b644f3ae9aa3816c34541124d3862f1b37f");
        foreach(var assembly in new[]{typeof(global::Catalog).Assembly,typeof(UnityEngine.MonoBehaviour).Assembly,typeof(Il2CppSystem.Object).Assembly})if(!RunPaths.Same(assembly.Location,Path.Combine(install,"BepInEx/interop",assembly.GetName().Name+".dll")))throw new InvalidOperationException("Catalog interop load location differs.");
        foreach(var name in new[]{"0Harmony","Il2CppInterop.Runtime","Il2CppInterop.Common","Il2CppInterop.HarmonySupport"}){var a=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name==name);if(!RunPaths.Same(a.Location,Path.Combine(install,"BepInEx/core",name+".dll")))throw new InvalidOperationException("Catalog patch backend load location differs.");}
        var module=process.Modules.Cast<ProcessModule>().Single(m=>string.Equals(m.ModuleName,"GameAssembly.dll",StringComparison.OrdinalIgnoreCase));if(!RunPaths.Same(module.FileName,Path.Combine(install,"GameAssembly.dll")))throw new InvalidOperationException("Loaded GameAssembly differs.");
        var result=new CatalogNativeBindings();
        foreach(var spec in Methods){Admitted();RuntimeHelpers.RunClassConstructor(spec.Type.TypeHandle);var cls=Il2CppClassPointerStore.GetNativeClassPointer(spec.Type);if(cls==IntPtr.Zero)throw new InvalidOperationException("Native class unavailable.");result.Classes[spec.Type]=cls;
            var method=spec.Type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Single(m=>m.Name==spec.Name&&m.IsStatic==spec.Static&&m.ReturnType==typeof(void)&&m.GetParameters().Length==0);
            var pointer=spec.Type.GetField(spec.PointerField,BindingFlags.Static|BindingFlags.NonPublic)??throw new MissingFieldException(spec.PointerField);var info=(IntPtr)pointer.GetValue(null)!;
            if(info==IntPtr.Zero||info!=IL2CPP.GetIl2CppMethodByToken(cls,spec.Token)||Marshal.ReadIntPtr(info).ToInt64()-module.BaseAddress.ToInt64()!=spec.Rva)throw new InvalidOperationException("Catalog method token/native RVA differs: "+spec.Name);result.Bound.Add(spec.Name,method);
        }
        return result;
    }
    internal void Instance(Type type,IntPtr pointer){if(pointer==IntPtr.Zero||IL2CPP.il2cpp_object_get_class(pointer)!=Classes[type])throw new InvalidOperationException("Foreign native Catalog/workbench receiver.");}
}

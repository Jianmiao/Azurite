namespace Azurite.EditorIsolation;

public sealed record NativeMethodContract(string Id, string Assembly, string Type, string Method, string PointerField, int Token, long Rva);

public static class HostContract
{
    public const string ApprovedLabRoot = @"E:\aamod\labs\Azurite-20261108-editor";
    public static void RequireApprovedRun(RunManifest manifest)
    {
        if (!RunPaths.Same(manifest.LabRoot, ApprovedLabRoot) ||
            !RunPaths.Same(manifest.InstallRoot, Path.Combine(ApprovedLabRoot, "install")) ||
            !RunPaths.Same(manifest.RunRoot, Path.Combine(ApprovedLabRoot, "runs", manifest.RunId)))
            throw new InvalidOperationException("Only the reviewed copied lab and GUID run are admitted; the manifest cannot authorize another installation.");
    }
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>
    {
        ["GameAssembly.dll"] = "0776516c59811c724c450e205930546c5d102975f279e33c46c96b878fa06c8b",
        ["UnityPlayer.dll"] = "df965f4ba87ce9976d55b0699671779e0ef921f75d5407689ac0ddd514fef1bc",
        ["AzureArchive_Data/il2cpp_data/Metadata/global-metadata.dat"] = "b4aeee165e7e10e0f46f85e095fd94487fdb041215f6dcb7ae9de5d28a364f43",
        ["BepInEx/interop/Assembly-CSharp.dll"] = "d95062f3a49fba645986dc7fe0cee749197c9d0c84ff381fc3ca2d9d2823c013",
        ["BepInEx/interop/UnityEngine.CoreModule.dll"] = "fd65570bc81061e45fd9ce3bd162cd5839e0efb8f4d76f6902ead8232cd69e69",
        ["BepInEx/interop/Unity.Addressables.dll"] = "06c6cd741d28c5b19c8d22620d16b10d30122abf4f7fff7ec5a21eda92102b5e",
        ["BepInEx/core/0Harmony.dll"] = "6c898933b52149e8bcf6722305c7e4d94add47c9e53b364096f91721624c0877",
        ["BepInEx/core/Il2CppInterop.Runtime.dll"] = "65ab051a681c2c1e52df7596ff738be401aae72cff882ff18241cf54a3ea38a4",
        ["BepInEx/core/Il2CppInterop.HarmonySupport.dll"] = "f7612ea2e5a4d473b771f34ee3b20cf32dc0c4ecea0185416474999a17fbcc07",
        ["BepInEx/core/BepInEx.Unity.IL2CPP.dll"] = "62c8246a3076bfe459ec80c482381228d0eb1dde3434fe97ae5255fef406fe72",
        ["BepInEx/core/BepInEx.Core.dll"] = "352aacc2d5356489a00891f76db28b6463c81d25bcb02f90035171a669b7e35b",
        ["BepInEx/core/BepInEx.Preloader.Core.dll"] = "ec168752c9a885d9fe035370ac2dbd8b52b91b24cb60fbc425cfcbee5436dae9"
    };
    private static NativeMethodContract C(string id, string type, string method, string suffix, int token, long rva, string assembly = "Assembly-CSharp") => new(id, assembly, type, method, "NativeMethodInfoPtr_" + suffix, token, rva);
    public static readonly NativeMethodContract[] Methods =
    {
        C("awake", "UserSettings", "Awake", "Awake_Protected_Virtual_Void_0", 100689258, 0x7f5710),
        C("read", "UserSettings+_ReadUserSettings_d__43", "MoveNext", "MoveNext_Private_Virtual_Final_New_Boolean_0", 100689386, 0x7f24e0),
        C("save", "UserSettings+_SaveTask_d__41", "MoveNext", "MoveNext_Private_Virtual_Final_New_Boolean_0", 100689392, 0x7f2610),
        C("write", "UserSettings", "WriteUserSettings", "WriteUserSettings_Public_Void_0", 100689268, 0x7f6640),
        C("fallback", "UserSettings", "InitializeSettings", "InitializeSettings_Private_IEnumerator_0", 100689266, 0x7f5dd0),
        C("cache", "UserSettings", "get_CachePath", "get_CachePath_Public_get_String_0", 100689255, 0x7f6a20),
        C("workspace", "UserSettings", "get_WorkspacePath", "get_WorkspacePath_Public_get_String_0", 100689256, 0x7f6c70),
        C("resources", "ScenarioResourceManager", "Initialize", "Initialize_Public_Void_0", 100688737, 0x7bffd0),
        C("discovery", "AzureArchive.Automation.AuthoringControlProtocol", "get_DiscoveryDirectory", "get_DiscoveryDirectory_Internal_Static_get_String_0", 100694828, 0x90a330),
        C("offline", "AzureArchive.Automation.AuthoringOfflineResources", "ValidateDirectory", "ValidateDirectory_Public_Static_Void_String_String_String_String_0", 100695181, 0x93c5b0),
        C("addressSet", "UnityEngine.AddressableAssets.Initialization.AddressablesRuntimeProperties", "SetPropertyValue", "SetPropertyValue_Public_Static_Void_String_String_0", 100663883, 0x2f6f900, "Unity.Addressables"),
        C("addressClear", "UnityEngine.AddressableAssets.Initialization.AddressablesRuntimeProperties", "ClearCachedPropertyValues", "ClearCachedPropertyValues_Public_Static_Void_0", 100663884, 0x2f6edb0, "Unity.Addressables"),
        C("addressEvaluate", "UnityEngine.AddressableAssets.Initialization.AddressablesRuntimeProperties", "EvaluateProperty", "EvaluateProperty_Public_Static_String_String_0", 100663885, 0x2f6ee30, "Unity.Addressables")
    };
    public static void VerifyFiles(string install)
    {
        foreach (var pin in Files)
        {
            var path = Path.Combine(install, pin.Key.Replace('/', Path.DirectorySeparatorChar));
            RunPaths.RequireSafeExisting(path);
            if (!string.Equals(RunManifest.Sha256(path), pin.Value, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Pinned host mismatch: " + pin.Key);
        }
    }
}

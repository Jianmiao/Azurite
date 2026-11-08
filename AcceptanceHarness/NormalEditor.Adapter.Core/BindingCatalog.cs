using Azurite.EditorIsolation;

namespace Azurite.NormalEditor.Adapter;

public sealed record MethodBindingSpec
{
    public MethodBindingSpec(string id,string assembly,string type,string method,string pointerField,int token,long rva,string returnType,params string[] parameters)
    { Id=id;Assembly=assembly;Type=type;Method=method;PointerField=pointerField;Token=token;Rva=rva;ReturnType=returnType;Parameters=Array.AsReadOnly(parameters.ToArray()); }
    public string Id {get;}
    public string Assembly {get;}
    public string Type {get;}
    public string Method {get;}
    public string PointerField {get;}
    public int Token {get;}
    public long Rva {get;}
    public string ReturnType {get;}
    public IReadOnlyList<string> Parameters {get;}
}
public sealed record FieldBindingSpec(string Type,string PointerField,string NativeName,string Property,string PropertyType,int? Offset,bool Static=false);
public static class BindingCatalog
{
    private static MethodBindingSpec M(string id,string type,string method,string suffix,int token,long rva,string returns="System.Void",string assembly="Assembly-CSharp",params string[] parameters)=>new(id,assembly,type,method,"NativeMethodInfoPtr_"+suffix,token,rva,returns,parameters);
    public static readonly IReadOnlyList<MethodBindingSpec> Methods=Array.AsReadOnly(new[]{
        M("awake","UserSettings","Awake","Awake_Protected_Virtual_Void_0",100689258,0x7f5710),
        M("init","UserSettings+_InitTask_d__36","MoveNext","MoveNext_Private_Virtual_Final_New_Boolean_0",100689374,0x7f1f60,"System.Boolean"),
        M("read","UserSettings+_ReadUserSettings_d__43","MoveNext","MoveNext_Private_Virtual_Final_New_Boolean_0",100689386,0x7f24e0,"System.Boolean"),
        M("save","UserSettings+_SaveTask_d__41","MoveNext","MoveNext_Private_Virtual_Final_New_Boolean_0",100689392,0x7f2610,"System.Boolean"),
        M("write","UserSettings","WriteUserSettings","WriteUserSettings_Public_Void_0",100689268,0x7f6640),
        M("fallback","UserSettings","InitializeSettings","InitializeSettings_Private_IEnumerator_0",100689266,0x7f5dd0,"Il2CppSystem.Collections.IEnumerator"),
        M("resource","ScenarioResourceManager","Initialize","Initialize_Public_Void_0",100688737,0x7bffd0),
        M("coroutine","UnityEngine.MonoBehaviour","StartCoroutine","StartCoroutine_Public_Coroutine_IEnumerator_0",100667066,0x3408550,"UnityEngine.Coroutine","UnityEngine.CoreModule","Il2CppSystem.Collections.IEnumerator"),
        M("coroutineAlias","UnityEngine.MonoBehaviour","StartCoroutine_Auto","StartCoroutine_Auto_Public_Coroutine_IEnumerator_0",100667067,0x3408550,"UnityEngine.Coroutine","UnityEngine.CoreModule","Il2CppSystem.Collections.IEnumerator")});
    private static FieldBindingSpec F(string type,string field,string native,string property,string propertyType,int? offset,bool isStatic=false)=>new(type,"NativeFieldInfoPtr_"+field,native,property,propertyType,offset,isStatic);
    public static readonly IReadOnlyList<FieldBindingSpec> Fields=Array.AsReadOnly(new[]{
        F("UserSettings","data","data","data","UserSettings/SettingData",0x28),
        F("UserSettings","settingFilePath","settingFilePath","settingFilePath","System.String",0x38),
        F("UserSettings","isDataDirty","isDataDirty","isDataDirty","System.Boolean",0x40),
        F("UserSettings","_isReady_k__BackingField","<isReady>k__BackingField","_isReady_k__BackingField","System.Boolean",0x60),
        F("UserSettings","_cachedWorkspacePath","_cachedWorkspacePath","_cachedWorkspacePath","System.String",0x68),
        F("UserSettings+SettingData","_username_k__BackingField","<username>k__BackingField","_username_k__BackingField","System.String",0x38),
        F("UserSettings+SettingData","_cachePath_k__BackingField","<cachePath>k__BackingField","_cachePath_k__BackingField","System.String",0x98),
        F("UserSettings+SettingData","_workspacePath_k__BackingField","<workspacePath>k__BackingField","_workspacePath_k__BackingField","System.String",0xa0),
        F("AzureArchive.Automation.AuthoringCliHost","_IsActive_k__BackingField","<IsActive>k__BackingField","_IsActive_k__BackingField","System.Boolean",null,true)
    }.Concat(new[]{"UserSettings+_InitTask_d__36","UserSettings+_ReadUserSettings_d__43","UserSettings+_SaveTask_d__41"}.SelectMany(t=>new[]{
        F(t,"__1__state","<>1__state","__1__state","System.Int32",0x10),
        F(t,"__2__current","<>2__current","__2__current","Il2CppSystem.Object",0x18),
        F(t,"__4__this","<>4__this","__4__this","UserSettings",0x20)})).ToArray());
    public const string NormalPolicyHash="9030d49768ad4a8d773eaf2751a2293241f6f2ee65f0695ca742a8cf2bb0f3e6";
    public const string LegacyPolicyHash="2f6297caec213d87464efad1a387230e40943b89eb71333fb65c8e03dc0d7653";
}

public static class InstallationAuthority
{
    public const string ApprovedLabRoot=@"F:\AzuriteLabs\Editor-20261108-normal";
    public const string AdapterSubdirectory=@"BepInEx\plugins\AzuriteNormalEditorPreparation";
    public static void Require(AdapterBinding binding,string executable,string hostAssembly)
    {
        var p=binding.Policy;
        if(!RunPaths.Same(p.Paths.Lab,ApprovedLabRoot) || !RunPaths.Same(p.Paths.Install,Path.Combine(ApprovedLabRoot,"install")) ||
           !RunPaths.Same(p.Paths.Run,Path.Combine(ApprovedLabRoot,"runs",p.RunId)) || p.ProcessId!=Environment.ProcessId ||
           !RunPaths.Same(executable,Path.Combine(p.Paths.Install,"AzureArchive.exe")) ||
           !RunPaths.Same(hostAssembly,Path.Combine(p.Paths.Install,AdapterSubdirectory,"Azurite.NormalEditor.Adapter.Host.PreparationOnly.dll")))
            throw new InvalidOperationException("Only the exact admitted normal lab child and adapter location are eligible.");
    }
    public static void RequireUnownedAliases(IEnumerable<string> primaryOwners,IEnumerable<string> aliasOwners)
    { if(primaryOwners.Any() || aliasOwners.Any())throw new InvalidOperationException("Either shared coroutine alias is already patched."); }
    public static void RequireEvidence(string? expected,string? current)
    { if(expected is null || expected.Length!=64 || !expected.All(Uri.IsHexDigit) || current!=expected)throw new InvalidOperationException("External admission evidence is absent or changed."); }
}

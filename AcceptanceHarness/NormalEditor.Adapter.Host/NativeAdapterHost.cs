using System.Diagnostics;
using System.Reflection;
using Azurite.EditorIsolation;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace Azurite.NormalEditor.Adapter.Host;

// Explicit future entry point only: no BepInPlugin, module initializer, loader or automatic registration.
public static class NativeAdapterHost
{
    private sealed record PatchSpec(string Id,string Prefix,string? Postfix);
    private sealed class State
    {
        internal readonly NativeBindings Bindings;
        internal readonly AdapterCoordinator Coordinator;
        internal readonly Harmony Harmony;
        internal bool Ready;
        internal State(NativeBindings b,AdapterCoordinator c,Harmony h){Bindings=b;Coordinator=c;Harmony=h;}
    }
    private static State? state;
    private static readonly PatchSpec[] patches={
        new("fallback",nameof(FallbackPrefix),null),new("read",nameof(ReadPrefix),nameof(ReadPostfix)),
        new("write",nameof(WritePrefix),nameof(WritePostfix)),new("save",nameof(SavePrefix),nameof(SavePostfix)),
        new("init",nameof(InitPrefix),nameof(InitPostfix)),new("resource",nameof(ResourcePrefix),nameof(ResourcePostfix)),
        new("coroutine",nameof(CoroutinePrefix),nameof(CoroutinePostfix)),new("awake",nameof(AwakePrefix),nameof(AwakePostfix))};
    public static void Register(AdapterBinding binding,IAdapterAdmission? admission=null)
    {
        if(admission is null)throw new InvalidOperationException("External normal adapter admission is required and is not provided.");
        var evidenceHash=admission.EvidenceSha256;
        void Admitted(){InstallationAuthority.RequireEvidence(evidenceHash,admission.EvidenceSha256);admission.RequireInstallation(binding);InstallationAuthority.RequireEvidence(evidenceHash,admission.EvidenceSha256);}
        Admitted();
        if(state is not null)throw new InvalidOperationException("Adapter registration is single-use.");
        var bindings=NativeBindings.Bind(binding,admission,evidenceHash);
        InstallationAuthority.RequireUnownedAliases(Owners(bindings,"coroutine"),Owners(bindings,"coroutineAlias"));
        foreach(var method in bindings.Methods.Values)if(Harmony.GetPatchInfo(method.Managed)?.Owners.Any()==true)throw new InvalidOperationException("Another owner patches a normal adapter seam.");
        var fatal=new OwnLabFatal(binding);
        state=new State(bindings,new AdapterCoordinator(binding,new NativeAccess(bindings),fatal,admission),new Harmony("lab.azurite.normal-settings."+binding.Policy.RunId));
        try
        {
            foreach(var patch in patches)
            {
                Admitted();
                state.Harmony.Patch(bindings.Methods[patch.Id].Managed,new HarmonyMethod(typeof(NativeAdapterHost),patch.Prefix),
                    patch.Postfix is null?null:new HarmonyMethod(typeof(NativeAdapterHost),patch.Postfix),
                    finalizer:new HarmonyMethod(typeof(NativeAdapterHost),nameof(OriginalFinalizer)));
            }
            VerifyOwnership();state.Ready=true; // Registration evidence only; no native hit or startup claim.
        }
        catch(Exception error){state.Coordinator.Reject("Registration failed: "+error.Message);throw;}
    }
    private static IEnumerable<string> Owners(NativeBindings bindings,string id)=>Harmony.GetPatchInfo(bindings.Methods[id].Managed)?.Owners??Enumerable.Empty<string>();
    private static void VerifyOwnership()
    {
        var s=state??throw new InvalidOperationException("Adapter not registered.");
        if(Owners(s.Bindings,"coroutineAlias").Any())throw new InvalidOperationException("The shared coroutine alias gained patch ownership.");
        foreach(var spec in patches)
        {
            var info=Harmony.GetPatchInfo(s.Bindings.Methods[spec.Id].Managed)??throw new InvalidOperationException("Missing adapter patch registration.");
            bool Matches(IEnumerable<Patch> values,string? method)
            {
                var a=values.ToArray();return method is null?a.Length==0:a.Length==1 && a[0].owner==s.Harmony.Id && a[0].PatchMethod.DeclaringType==typeof(NativeAdapterHost) && a[0].PatchMethod.Name==method;
            }
            if(info.Owners.Any(owner=>owner!=s.Harmony.Id) || !Matches(info.Prefixes,spec.Prefix) || !Matches(info.Postfixes,spec.Postfix) || !Matches(info.Finalizers,nameof(OriginalFinalizer)) || info.Transpilers.Any())
                throw new InvalidOperationException("Patch ownership/content changed: "+spec.Id);
        }
    }
    private delegate bool PrefixAction(out AdapterFrame? frame);
    private static bool Begin(PrefixAction action,out AdapterFrame? frame)
    {
        frame=null;
        try
        {
            if(state is null || !state.Ready)throw new InvalidOperationException("Callback arrived before complete admitted registration.");
            VerifyOwnership();return action(out frame);
        }
        catch(Exception error){state?.Coordinator.Reject(error.Message);return false;}
    }
    private static void End(AdapterFrame? frame,CallbackKind kind,long pointer,bool original,bool? yielded=null)
    {
        try { VerifyOwnership();state!.Coordinator.Returned(frame,kind,pointer,original,yielded); }
        catch(Exception error){state?.Coordinator.Reject(error.Message);}
    }
    private static bool AwakePrefix(UserSettings __instance,out AdapterFrame? __state)=>Begin((out AdapterFrame? f)=>state!.Coordinator.AwakeEntered(__instance.Pointer.ToInt64(),out f),out __state);
    private static void AwakePostfix(UserSettings __instance,AdapterFrame? __state,bool __runOriginal)=>End(__state,CallbackKind.Awake,__instance.Pointer.ToInt64(),__runOriginal);
    private static bool CoroutinePrefix(UnityEngine.MonoBehaviour __instance,Il2CppSystem.Collections.IEnumerator? __0,out AdapterFrame? __state)=>Begin((out AdapterFrame? f)=>state!.Coordinator.CoroutineEntered(__instance.Pointer.ToInt64(),__0?.Pointer.ToInt64()??0,out f),out __state);
    private static void CoroutinePostfix(UnityEngine.MonoBehaviour __instance,AdapterFrame? __state,bool __runOriginal)=>End(__state,CallbackKind.Coroutine,__instance.Pointer.ToInt64(),__runOriginal);
    private static bool InitPrefix(Il2CppObjectBase __instance,out AdapterFrame? __state)=>Begin((out AdapterFrame? f)=>state!.Coordinator.IteratorEntered(__instance.Pointer.ToInt64(),NativeKind.Init,out f),out __state);
    private static void InitPostfix(Il2CppObjectBase __instance,AdapterFrame? __state,bool __runOriginal,bool __result)=>End(__state,CallbackKind.Init,__instance.Pointer.ToInt64(),__runOriginal,__result);
    private static bool ReadPrefix(Il2CppObjectBase __instance,out AdapterFrame? __state)=>Begin((out AdapterFrame? f)=>state!.Coordinator.IteratorEntered(__instance.Pointer.ToInt64(),NativeKind.Read,out f),out __state);
    private static void ReadPostfix(Il2CppObjectBase __instance,AdapterFrame? __state,bool __runOriginal,bool __result)=>End(__state,CallbackKind.Read,__instance.Pointer.ToInt64(),__runOriginal,__result);
    private static bool SavePrefix(Il2CppObjectBase __instance,out AdapterFrame? __state)=>Begin((out AdapterFrame? f)=>state!.Coordinator.IteratorEntered(__instance.Pointer.ToInt64(),NativeKind.Save,out f),out __state);
    private static void SavePostfix(Il2CppObjectBase __instance,AdapterFrame? __state,bool __runOriginal,bool __result)=>End(__state,CallbackKind.Save,__instance.Pointer.ToInt64(),__runOriginal,__result);
    private static bool WritePrefix(UserSettings __instance,out AdapterFrame? __state)=>Begin((out AdapterFrame? f)=>state!.Coordinator.WriteEntered(__instance.Pointer.ToInt64(),out f),out __state);
    private static void WritePostfix(UserSettings __instance,AdapterFrame? __state,bool __runOriginal)=>End(__state,CallbackKind.Write,__instance.Pointer.ToInt64(),__runOriginal);
    private static bool ResourcePrefix(ScenarioResourceManager __instance,out AdapterFrame? __state)=>Begin((out AdapterFrame? f)=>state!.Coordinator.ResourceEntered(__instance.Pointer.ToInt64(),out f),out __state);
    private static void ResourcePostfix(ScenarioResourceManager __instance,AdapterFrame? __state,bool __runOriginal)=>End(__state,CallbackKind.Resource,__instance.Pointer.ToInt64(),__runOriginal);
    private static bool FallbackPrefix(out AdapterFrame? __state)=>Begin((out AdapterFrame? f)=>{f=null;return state!.Coordinator.RejectFallback();},out __state);
    private static Exception? OriginalFinalizer(Exception? __exception,AdapterFrame? __state,Il2CppObjectBase __instance,MethodBase __originalMethod)
    {
        try
        {
            VerifyOwnership();
            var id=state!.Bindings.Methods.Values.Single(b=>b.Managed.Equals(__originalMethod)).Spec.Id;
            var kind=id switch
            {
                "awake"=>CallbackKind.Awake,"coroutine" or "coroutineAlias"=>CallbackKind.Coroutine,
                "init"=>CallbackKind.Init,"read"=>CallbackKind.Read,"save"=>CallbackKind.Save,
                "write"=>CallbackKind.Write,"resource"=>CallbackKind.Resource,
                _=>throw new InvalidOperationException("A refused fallback has no normal completion token.")
            };
            return FinalizerDispatch.Complete(state.Coordinator,kind,__instance.Pointer.ToInt64(),__state,__exception);
        }
        catch(Exception error){state?.Coordinator.Reject("Finalizer dispatch failed: "+error.Message);return __exception;}
    }
    private sealed class OwnLabFatal : IFatalSink
    {
        private readonly AdapterBinding binding;
        internal OwnLabFatal(AdapterBinding binding)=>this.binding=binding;
        public void Terminate(string reason)
        {
            using var process=Process.GetCurrentProcess();
            InstallationAuthority.Require(binding,process.MainModule?.FileName??throw new InvalidOperationException("Missing current executable identity."),typeof(NativeAdapterHost).Assembly.Location);
            OwnProcessAbort.Terminate(87); // Existing pseudo-handle helper cannot target another PID.
        }
    }
}

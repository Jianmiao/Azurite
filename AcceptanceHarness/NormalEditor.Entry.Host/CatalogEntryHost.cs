using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using AzureArchive.Automation;
using Azurite.NormalEditor.Adapter;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Azurite.NormalEditor.Entry.Host;

// No plugin/automatic registration or permitting lease. A separately admitted
// integration may register this observer; it never drives Start/Begin/Update.
public static class CatalogEntryHost
{
    private sealed class State
    {
        internal State(AdapterBinding binding,IAdapterAdmission admission,string evidence,CatalogNativeBindings native,CatalogReadiness gate,Harmony harmony,CatalogClockAnchor clock,long milliseconds){Binding=binding;Admission=admission;Evidence=evidence;Native=native;Gate=gate;Harmony=harmony;Clock=clock;LastMilliseconds=milliseconds;Ticks=new(gate);}
        internal readonly AdapterBinding Binding;internal readonly IAdapterAdmission Admission;internal readonly string Evidence;internal readonly CatalogNativeBindings Native;internal readonly CatalogReadiness Gate;internal readonly Harmony Harmony;internal readonly CatalogClockAnchor Clock;
        internal readonly CatalogTickCallbacks Ticks;internal bool Registered;internal long LastMilliseconds;
    }
    private static State? state;
    [DllImport("kernel32.dll",ExactSpelling=true)]private static extern uint GetCurrentThreadId();
    public static void Register(AdapterBinding binding,IAdapterAdmission admission,string entryHostSha256,string entryCoreSha256,Action<CatalogEvidence> record,CatalogClockAnchor clock,long timeoutMilliseconds=120000)
    {
        ArgumentNullException.ThrowIfNull(admission);ArgumentNullException.ThrowIfNull(record);ArgumentNullException.ThrowIfNull(clock);
        if(state is not null)throw new InvalidOperationException("Catalog observer registration is single-use.");
        var milliseconds=clock.ElapsedMillisecondsAt(Stopwatch.GetTimestamp(),Stopwatch.Frequency);
        var proof=admission.EvidenceSha256;InstallationAuthority.RequireEvidence(proof,admission.EvidenceSha256);admission.RequireInstallation(binding);
        if(binding.Policy.ProcessId!=Environment.ProcessId||binding.Policy.NativeThreadId!=GetCurrentThreadId())throw new InvalidOperationException("Catalog observer must register on admitted native main thread.");
        var native=CatalogNativeBindings.Bind(binding,admission,proof,entryHostSha256,entryCoreSha256);
        foreach(var method in native.Bound.Values)if(Harmony.GetPatchInfo(method)?.Owners.Any()==true)throw new InvalidOperationException("A Catalog observer/idle seam is already patched.");
        var gate=new CatalogReadiness(new(binding.Policy.RunId,Environment.ProcessId,Environment.CurrentManagedThreadId,GetCurrentThreadId(),timeoutMilliseconds),record);
        state=new(binding,admission,proof,native,gate,new Harmony("lab.azurite.catalog-observer."+binding.Policy.RunId),clock,milliseconds);
        try{
            state.Harmony.Patch(native.Bound["Start"],new HarmonyMethod(typeof(CatalogEntryHost),nameof(StartPrefix)),new HarmonyMethod(typeof(CatalogEntryHost),nameof(StartPostfix)),finalizer:new HarmonyMethod(typeof(CatalogEntryHost),nameof(StartFinalizer)));
            state.Harmony.Patch(native.Bound["Update"],new HarmonyMethod(typeof(CatalogEntryHost),nameof(UpdatePrefix)),new HarmonyMethod(typeof(CatalogEntryHost),nameof(UpdatePostfix)),finalizer:new HarmonyMethod(typeof(CatalogEntryHost),nameof(UpdateFinalizer)));
            Verify();state.Registered=true;
        }catch(Exception e){gate.Reject("Catalog registration failed: "+e.Message,LastStamp(0));throw;}
    }
    private static void Verify()
    {
        var s=state??throw new InvalidOperationException("Catalog observer not registered.");InstallationAuthority.RequireEvidence(s.Evidence,s.Admission.EvidenceSha256);s.Admission.RequireInstallation(s.Binding);InstallationAuthority.RequireEvidence(s.Evidence,s.Admission.EvidenceSha256);
        foreach(var tuple in new[]{("Start",nameof(StartPrefix),nameof(StartPostfix),nameof(StartFinalizer)),("Update",nameof(UpdatePrefix),nameof(UpdatePostfix),nameof(UpdateFinalizer))}){
            var info=Harmony.GetPatchInfo(s.Native.Bound[tuple.Item1])??throw new InvalidOperationException("Missing Catalog observer patch.");
            bool Matches(IEnumerable<Patch> patches,string name){var a=patches.ToArray();return a.Length==1&&a[0].owner==s.Harmony.Id&&a[0].PatchMethod.DeclaringType==typeof(CatalogEntryHost)&&a[0].PatchMethod.Name==name;}
            if(info.Owners.Any(x=>x!=s.Harmony.Id)||!Matches(info.Prefixes,tuple.Item2)||!Matches(info.Postfixes,tuple.Item3)||!Matches(info.Finalizers,tuple.Item4)||info.Transpilers.Any())throw new InvalidOperationException("Catalog observer patch ownership/content changed.");
        }
        if(Harmony.GetPatchInfo(s.Native.Bound["RequireCatalogIdleForControl"])?.Owners.Any()==true)throw new InvalidOperationException("Native catalog idle guard was patched.");
    }
    private static long Pointer(Il2CppObjectBase? value)=>value?.Pointer.ToInt64()??0;
    private static CatalogStamp LastStamp(long catalog)=>new(state!.Binding.Policy.RunId,Environment.ProcessId,Environment.CurrentManagedThreadId,GetCurrentThreadId(),0,state.LastMilliseconds,catalog);
    private static CatalogStamp FallbackStamp(long catalog)
    {
        var s=state!;var milliseconds=s.Clock.ElapsedMillisecondsAt(Stopwatch.GetTimestamp(),Stopwatch.Frequency);
        if(milliseconds<s.LastMilliseconds)throw new InvalidOperationException("Observer monotonic clock reversed.");
        s.LastMilliseconds=milliseconds;return LastStamp(catalog);
    }
    private static CatalogStamp Stamp(long catalog)=>FallbackStamp(catalog) with{Frame=Time.frameCount};
    private static void Fail(Exception e,long catalog){state?.Gate.Reject(e.Message,LastStamp(catalog));}
    private static void Callback(Type type,IntPtr pointer,MethodBase method,string name)
    {
        var s=state??throw new InvalidOperationException("Catalog observer is absent.");if(!s.Registered)throw new InvalidOperationException("Catalog callback before full registration.");Verify();if(method!=s.Native.Bound[name])throw new InvalidOperationException("Foreign Catalog callback method.");s.Native.Instance(type,pointer);
        if(Environment.ProcessId!=s.Binding.Policy.ProcessId||GetCurrentThreadId()!=s.Binding.Policy.NativeThreadId)throw new InvalidOperationException("Foreign Catalog callback process/native thread.");
    }
    private static void StartPrefix(global::Catalog __instance,MethodBase __originalMethod,out CatalogStartFrame? __state)
    {__state=null;try{Callback(typeof(global::Catalog),__instance.Pointer,__originalMethod,"Start");__state=state!.Gate.StartEntered(Stamp(__instance.Pointer.ToInt64()));}catch(Exception e){Fail(e,Pointer(__instance));}}
    private static void StartPostfix(global::Catalog __instance,MethodBase __originalMethod,CatalogStartFrame? __state,bool __runOriginal)
    {try{Callback(typeof(global::Catalog),__instance.Pointer,__originalMethod,"Start");state!.Gate.StartReturned(__state,Stamp(__instance.Pointer.ToInt64()),__runOriginal);}catch(Exception e){Fail(e,Pointer(__instance));}}
    private static Exception? StartFinalizer(global::Catalog __instance,MethodBase __originalMethod,CatalogStartFrame? __state,Exception? __exception)
    {try{Callback(typeof(global::Catalog),__instance.Pointer,__originalMethod,"Start");return state!.Gate.StartFinalized(__state,Stamp(__instance.Pointer.ToInt64()),__exception);}catch(Exception e){Fail(e,Pointer(__instance));return __exception;}}
    private static void UpdatePrefix(AuthoringWorkbench __instance,MethodBase __originalMethod,out CatalogTickFrame? __state)
    {__state=null;try{Callback(typeof(AuthoringWorkbench),__instance.Pointer,__originalMethod,"Update");__state=state!.Ticks.Enter(Stamp(0),__instance.Pointer.ToInt64());}catch(Exception e){Fail(e,0);}}
    private static void UpdatePostfix(AuthoringWorkbench __instance,MethodBase __originalMethod,CatalogTickFrame? __state,bool __runOriginal)
    {try{Callback(typeof(AuthoringWorkbench),__instance.Pointer,__originalMethod,"Update");state!.Ticks.Returned(__state,Stamp(0),__instance.Pointer.ToInt64(),__runOriginal);}catch(Exception e){Fail(e,0);}}
    private static Exception? UpdateFinalizer(AuthoringWorkbench __instance,MethodBase __originalMethod,CatalogTickFrame? __state,Exception? __exception)
    {
        try{Callback(typeof(AuthoringWorkbench),__instance.Pointer,__originalMethod,"Update");if(!state!.Ticks.TryComplete(__state,Stamp(0),__instance.Pointer.ToInt64(),__exception))return __exception;
            state.Gate.ObserveTick(Stamp(0),()=>Observe(__instance),()=>{Verify();AuthoringWorkbench.RequireCatalogIdleForControl();return Observe(__instance);});
        }catch(Exception e){Fail(e,0);}return __exception;
    }
    private static CatalogObservation Observe(AuthoringWorkbench callbackOwner)
    {
        // Only called through the deferred, admitted observation bridge. These
        // are field reads, but first use may run generated native class init.
        // Natural class initialization still needs separate native evidence.
        var catalog=Singleton<global::Catalog>._Instance_k__BackingField;var workbench=AuthoringWorkbench._Instance_k__BackingField;
        var settings=Singleton<UserSettings>._Instance_k__BackingField;var resources=Singleton<ScenarioResourceManager>._Instance_k__BackingField;var loading=Singleton<Loading>._Instance_k__BackingField;
        if(Pointer(workbench)!=Pointer(callbackOwner))throw new InvalidOperationException("Workbench callback no longer matches native singleton.");
        return new(Stamp(Pointer(catalog)),Pointer(workbench),Pointer(settings),Pointer(resources),SceneManager.GetActiveScene().name,
            AuthoringCliHost._IsActive_k__BackingField,!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZUREARCHIVE_CLI_DIRECTORY")),
            settings?._isReady_k__BackingField??false,resources?._AreDbsLoaded_k__BackingField??false,resources?._Preloading_k__BackingField??true,
            loading?.loadingCount??-1,Pointer(AuthoringEditorSession._Current_k__BackingField),workbench?.controlOpening??true);
    }
}

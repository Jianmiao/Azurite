using System.Reflection;
using Azurite;
using BepInEx.Configuration;
using Background = Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer;
using Popup = Studio.Scripts.Window.PopupImageExplorer.PopupImageExplorer;
using Character = Studio.Scripts.Window.CharacterExplorer.CharacterExplorer;

internal static class SelectorPolicyChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var type = typeof(DeferredEditorSelectors).Assembly.GetType("Azurite.DeferredSelectorPolicy");
        if (type == null) { check(false,"actual production config/tick policy is linked"); return; }
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var bind = (Func<ConfigFile,ConfigEntry<bool>>)type.GetMethod("Bind",flags)!.CreateDelegate(typeof(Func<ConfigFile,ConfigEntry<bool>>));
        var installController = (Func<DeferredEditorSelectors,bool,HostProfile,bool,bool,bool,HostCompatibilitySnapshot,bool,bool>)type.GetMethod("Install",flags)!.CreateDelegate(typeof(Func<DeferredEditorSelectors,bool,HostProfile,bool,bool,bool,HostCompatibilitySnapshot,bool,bool>));
        var apply = (Action<DeferredEditorSelectors?,bool,bool,bool,HostProfile,HostCompatibilitySnapshot,bool>)type.GetMethod("Apply",flags)!.CreateDelegate(typeof(Action<DeferredEditorSelectors?,bool,bool,bool,HostProfile,HostCompatibilitySnapshot,bool>));
        var pause = (Action<DeferredEditorSelectors?>)type.GetMethod("Pause",flags)!.CreateDelegate(typeof(Action<DeferredEditorSelectors?>));
        DeferredEditorSelectors? Install(Action<string> log,bool initialized,HostProfile profile,bool configured,bool enabled,bool disabled,HostCompatibilitySnapshot snapshot,bool hold)
        {
            var value=new DeferredEditorSelectors(log);
            if(installController(value,initialized,profile,configured,enabled,disabled,snapshot,hold))return value;
            value.Dispose();return null;
        }
        var fix = HostProfile.ResolvePortable("2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528","107C1E0F80C1C6A87CA7239C803DF711ED1DD1F9D2BBAAE0EBF5C9EA9F6078A9",true);
        var release101 = HostProfile.ResolvePortable("0776516C59811C724C450E205930546C5D102975F279E33C46C96B878FA06C8B","B4AEEE165E7E10E0F46F85E095FD94487FDB041215F6DCB7AE9DE5D28A364F43",true);
        var legacy = HostProfile.Resolve("BD45C2DFBA4EE59A3A3007E34B53B401985B838D66FFDEBAC863C8527948A80F","AFA74D22354E75803E63E8D39F48C4FDE4F13379BA4400BC12C0E880EAEBB11C","86FE750B9E5F0B5E5A9FB8E699671036E8EFB8BB05B613AFB4B9BA2DB197B927");
        var healthy = HostCompatibilitySnapshot.NoExport("fixture");
        var config = new ConfigFile(); var entry = bind(config);
        check(!entry.Value && config.Entries.ContainsKey("Large Projects/DeferEditorSelectors"),"production binder creates independent default-off setting");
        entry.Value = true; check(ReferenceEquals(entry,bind(config)) && entry.Value,"production binder preserves existing opt-in value"); entry.Value=false;
        Background? first = null;
        HarmonyLib.Harmony.AfterPatch = method => { if (first == null && method.DeclaringType == typeof(Background) && method.Name == "Start") { first = new(); first.Start(); } };
        DeferredEditorSelectors? controller;
        try { controller = Install(_=>{},true,fix,entry.Value,true,false,healthy,false); }
        finally { HarmonyLib.Harmony.AfterPatch = null; }
        using (controller)
        {
            check(controller != null && !controller.Enabled && first?.StartCalls == 1,"default off is applied before the very first hook callback");
            if (controller == null) return;
            entry.Value=true;apply(controller,entry.Value,true,false,fix,healthy,false);
            var bg=new Background();var popup=new Popup();var character=new Character();bg.Start();popup.Start();character.Awake();
            check(bg.StartCalls==0 && popup.StartCalls==0 && character.characterList.LoadCalls==0 && controller.PendingCount==3,"live opt-in defers all three fresh selector families");
            entry.Value=false;apply(controller,entry.Value,true,false,fix,healthy,false);
            check(!controller.Enabled && controller.PendingCount==3 && bg.StartCalls==0 && popup.StartCalls==0 && character.characterList.LoadCalls==0,"live opt-out neither flushes nor discards pending demand");
            var nativeBg=new Background();var nativePopup=new Popup();var nativeCharacter=new Character();nativeBg.Start();nativePopup.Start();nativeCharacter.Awake();
            check(nativeBg.StartCalls==1 && nativePopup.StartCalls==1 && nativeCharacter.characterList.LoadCalls==1,"live opt-out keeps new character/background/popup startup native");
            bg.Init(new(),new());popup.Init(new(),new());character.Init(new(),0,false);
            check(bg.StartCalls==1 && popup.StartCalls==1 && character.characterList.LoadCalls==1 && bg.InitCalls==1 && popup.InitCalls==1 && character.InitCalls==1 && controller.PendingCount==0,"all pending families finish exactly once on native demand after opt-out");
            apply(controller,true,true,false,fix,healthy,false);var pending=new Background();var pendingPopup=new Popup();var pendingCharacter=new Character();pending.Start();pendingPopup.Start();pendingCharacter.Awake();pause(controller);
            check(!controller.Enabled && controller.PendingCount==3 && pending.StartCalls==0 && pendingPopup.StartCalls==0 && pendingCharacter.characterList.LoadCalls==0,"pause retains all three pending families without eagerly building them");
            pending.Show();pendingPopup.Init(new(),new());pendingCharacter.Init(new(),0,false);
            check(pending.StartCalls==1 && pending.ShowCalls==1 && pendingPopup.StartCalls==1 && pendingPopup.InitCalls==1 && pendingCharacter.characterList.LoadCalls==1 && pendingCharacter.InitCalls==1 && controller.PendingCount==0,"character/background/popup demand still completes exactly once while paused");
            foreach(var gate in new[]{"global","session","unsupported","legacy","101","probe","export","hold"})
            {
                apply(controller,true,gate!="global",gate=="session",gate=="unsupported"?HostProfile.Unsupported:gate=="legacy"?legacy:gate=="101"?release101:fix,gate=="probe"?HostCompatibilitySnapshot.Pending("pending"):gate=="export"?new(true,true,true,true,"export"):healthy,gate=="hold");
                var view=new Background();view.Start();check(!controller.Enabled && view.StartCalls==1,"production policy blocks new deferral for "+gate);
            }
            apply(controller,true,true,false,fix,healthy,false);var resumed=new Popup();resumed.Start();check(controller.Enabled && resumed.StartCalls==0,"healthy next tick can resume explicit opt-in after a hold");resumed.Init(new(),new());
        }
        foreach(var host in new[]{HostProfile.Unsupported,legacy,release101})
        {
            using var denied=Install(_=>{},true,host,true,true,false,healthy,false);
            check(denied==null && HarmonyLib.Harmony.Patches.Count==0,"production install keeps existing host capability rejection: "+host.Name);
        }
        using(var denied=Install(_=>{},false,fix,true,true,false,healthy,false))check(denied==null,"unverified host initialization cannot install selector hooks");
        using(var globallyOff=Install(_=>{},true,fix,true,false,false,healthy,false))check(globallyOff!=null && !globallyOff.Enabled,"configured-on startup respects global disable before interception");
        foreach(bool destroy in new[]{false,true})
        {
            var c=Install(_=>{},true,fix,true,true,false,healthy,false)!;var bg=new Background();var popup=new Popup();var ch=new Character();bg.Start();popup.Start();ch.Awake();pause(c);
            if(destroy)new Studio.Scripts.StudioCommon().OnDestroy();c.Dispose();
            int expected=destroy?0:1;check(bg.StartCalls==expected && popup.StartCalls==expected && ch.characterList.LoadCalls==expected && HarmonyLib.Harmony.Patches.Count==0,destroy?"scene destruction preserves native teardown for all paused families":"live unload completes all pending families before unpatching");
        }
    }
}

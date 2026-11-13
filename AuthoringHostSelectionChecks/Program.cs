using System.Reflection;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using Studio.Scripts.Window;
using UnityEngine;

static class Program
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Type Selection => typeof(Program).Assembly.GetType("Azurite.AuthoringCardPackHostSelection") ?? typeof(Program).Assembly.GetType("Azurite.DialogueBoxSelection")!;
    static bool qualified;
    static object New()
    {
        qualified=true; Application.isFocused=true; UICamera.inputHasFocus=UICamera.isDragging=false;
        UICamera.hoveredObject=null; UIInput.current=UIInput.selection=null; UIInput.mLastIME="";
        Input.Down=Input.Held=Input.Up=false; Input.Keys.Clear(); Time.frameCount++;
        Singleton<WindowManager>.Instance=new();
        var ctor=Selection.GetConstructor(Flags,null,new[]{typeof(int),typeof(Func<bool>),typeof(Action<string>)},null);
        var value=ctor!=null ? ctor.Invoke(new object?[]{Environment.CurrentManagedThreadId,(Func<bool>)(()=>qualified),null}) : Activator.CreateInstance(Selection,Flags,null,new object?[]{null},null)!;
        Check((bool)Call(value,"Install")!,"fixture hook registration rejected"); return value;
    }
    static object? Call(object value,string name,params object?[] args)
    {
        var method=Selection.GetMethod(name,Flags) ?? throw new InvalidOperationException("Missing production behavior: "+name);
        try { return method.Invoke(value,args); } catch(TargetInvocationException e) { throw e.InnerException!; }
    }
    static object? Hook(string name,params object?[] args)
    {
        try { return Selection.GetMethod(name,Flags)!.Invoke(null,args); } catch(TargetInvocationException e) { throw e.InnerException!; }
    }
    static long Revision(object s)=>(long)Selection.GetProperty("SelectionRevision",Flags)!.GetValue(s)!;
    static void Check(bool yes,string message) { if(!yes) throw new InvalidOperationException(message); }
    static ScriptNodeInspector Setup(int count=3)
    {
        var inspector=new ScriptNodeInspector(); ScriptNodeInspector.instance=inspector;
        for(int i=0;i<count;i++)
        {
            inspector.scriptNode.scripts.Add(new Script { LineId="native-line-"+i });
            float top=1000-i*20;
            var row=new ScriptListItem { inspector=inspector,scriptNode=inspector.scriptNode,index=i,child=new UIWidget { worldCorners=new[]{new Vector3(0,top,0),new Vector3(100,top,0),new Vector3(100,top-18,0),new Vector3(0,top-18,0)} } };
            inspector.scriptNodeListItemsCache.Add(row);
        }
        return inspector;
    }
    static void Frame(object s,float x,float y,bool down=false,bool held=false,bool up=false)
    {
        Time.frameCount++; Input.mousePosition=new Vector3(x,Screen.height-y,0); Input.Down=down;Input.Held=held;Input.Up=up;Call(s,"Update",true);
    }
    static void Click(object s,int index,bool ctrl=false)
    {
        Input.Keys.Clear(); if(ctrl) Input.Keys.Add(KeyCode.LeftControl);
        Frame(s,10,index*20+5,true,true); Frame(s,10,index*20+5,up:true); Input.Keys.Clear();
    }
    static ScriptListItem[] Selected(object s)
    {
        object?[] args={null,null}; Check((bool)Call(s,"TryGetSelectedRows",args)!,"selected rows unavailable");
        return ((IReadOnlyList<ScriptListItem>)args[1]!).ToArray();
    }
    static void With(Action<object,ScriptNodeInspector> test,int count=3)
    {
        var inspector=Setup(count); var s=New(); try { Call(s,"Update",true);test(s,inspector); } finally { Call(s,"Dispose"); }
    }
    static void Throws(Action action,string message) { try {action();} catch(InvalidOperationException){return;} throw new Exception(message); }
    static readonly (string Name,Action Test)[] Cases=
    {
        ("real pointer Ctrl preserves ordered selected native rows",()=>With((s,i)=>{Click(s,2);Click(s,0,true);Check(Selected(s).Select(r=>r.index).SequenceEqual(new[]{0,2}),"native order or Ctrl toggling lost");})),
        ("5000-row list remains selectable",()=>With((s,i)=>{Click(s,0);Check(Selected(s).Length==1,"5000 rows were incorrectly rejected");},5000)),
        ("empty list rebuild invalidates preview revision",()=>With((s,i)=>{long before=Revision(s);Hook("AfterSyncScriptList",i);Check(Revision(s)>before,"empty selection rebuild retained stale revision");})),
        ("same-identity inspector reload invalidates preview revision",()=>With((s,i)=>{long before=Revision(s);Hook("AfterInspectorReset",i);Check(Revision(s)>before,"reload retained stale revision");})),
        ("same-count native node switch clears old selection",()=>With((s,i)=>{Click(s,0);long before=Revision(s);i.scriptNode=new ScriptNode {scripts=i.scriptNode.scripts};foreach(var row in i.scriptNodeListItemsCache)row.scriptNode=i.scriptNode;Frame(s,10,5);Check(Revision(s)>before && Selected(s).Length==0,"node identity switch was missed");})),
        ("row refresh invalidates empty preview",()=>With((s,i)=>{long before=Revision(s);Hook("AfterRowRefresh",i.scriptNodeListItemsCache[0]);Check(Revision(s)>before,"row refresh retained stale empty revision");})),
        ("unrelated inspector refresh does not clear selection",()=>With((s,i)=>{Click(s,0);var other=new ScriptNodeInspector();Hook("AfterRowRefresh",new ScriptListItem{inspector=other,scriptNode=other.scriptNode});Check(Selected(s).Length==1,"unrelated row refresh cleared selection");})),
        ("mapping reconciliation cancels adapter gesture",()=>With((s,i)=>{Frame(s,10,5,true,true);Frame(s,80,35,held:true);i.scriptNode.scripts[1]=new Script{LineId="replacement-line"};Frame(s,90,40,held:true);Check(!(bool)Selection.GetField("_gestureStarted",Flags)!.GetValue(s)!,"model canceled but adapter retained gesture ownership");})),
        ("qualified guard revocation blocks same-frame native interception",()=>With((s,i)=>{Click(s,0);qualified=false;Input.Keys.Add(KeyCode.LeftControl);Check((bool)Hook("BeforeRowClick",i.scriptNodeListItemsCache[1])!,"revoked qualification still consumed native click");})),
        ("modal window opened between frames keeps native click",()=>With((s,i)=>{Click(s,0);Singleton<WindowManager>.Instance.activeWindow=new object();Input.Keys.Add(KeyCode.LeftControl);Check((bool)Hook("BeforeRowClick",i.scriptNodeListItemsCache[1])!,"modal click was intercepted");})),
        ("retired row instance cannot become native anchor",()=>With((s,i)=>{Click(s,0);var stale=new ScriptListItem {inspector=i,scriptNode=i.scriptNode,index=0};Input.Keys.Add(KeyCode.LeftControl);Check((bool)Hook("BeforeRowClick",stale)!,"retired row click was consumed");Check(i.selectedScriptItem!=stale,"retired row was activated");})),
        ("post-result rejects reordered line IDs",()=>With((s,i)=>{Throws(()=>Call(s,"InvalidateNativeResult",i.scriptNode.guid.ToString(),(IReadOnlyList<string>)new[]{"native-line-1","native-line-0","native-line-2"}),"wrong line order accepted");})),
        ("unchanged identity capture keeps revision stable",()=>With((s,i)=>{Click(s,0);long a=Revision(s),b=Revision(s);Check(a==b,"read-only capture changed a stable revision");})),
        ("IME composition preserves native pointer ownership",()=>With((s,i)=>{UIInput.mLastIME="正在输入";Click(s,0);Check(Selected(s).Length==0,"IME input reached selection");})),
        ("post-result refresh validates real node and ordered line IDs",()=>With((s,i)=>{Click(s,0);long before=Revision(s);Call(s,"InvalidateNativeResult",i.scriptNode.guid.ToString(),(IReadOnlyList<string>)i.scriptNode.scripts.Select(x=>x.LineId).ToArray());Check(Revision(s)>before&&Selected(s).Length==0,"post-result did not invalidate");})),
        ("post-result rejects wrong identity and clears old selection",()=>With((s,i)=>{Click(s,0);Throws(()=>Call(s,"InvalidateNativeResult","wrong-node",(IReadOnlyList<string>)new[]{"native-line-0"}),"wrong native node accepted");Check(Selected(s).Length==0,"invalid post-result left selection live");})),
        ("post-result remains read-only when authoring guard closes",()=>With((s,i)=>{qualified=false;Call(s,"InvalidateNativeResult",i.scriptNode.guid.ToString(),(IReadOnlyList<string>)i.scriptNode.scripts.Select(x=>x.LineId).ToArray());})),
        ("cache handoff rejects duplicate native line IDs",()=>With((s,i)=>{Click(s,0);i.scriptNode.scripts[1].LineId="native-line-0";object?[] args={null,null};Check(!(bool)Call(s,"TryGetSelectedRows",args)!,"ambiguous native identity accepted");})),
        ("wrong-thread snapshot rejects before native cache access",()=>With((s,i)=>{bool accepted=Task.Run(()=>{object?[] args={null,null};return (bool)Call(s,"TryGetSelectedRows",args)!;}).GetAwaiter().GetResult();Check(!accepted,"wrong-thread native row read accepted");})),
        ("native input scrollbar and drag handle keep pointer",()=>With((s,i)=>{foreach(Type type in new[]{typeof(UIInput),typeof(UIScrollBar),typeof(Dragger)}){var hovered=new GameObject();hovered.Components[type]=Activator.CreateInstance(type)!;UICamera.hoveredObject=hovered;Click(s,0);Check(Selected(s).Length==0,"native control lost pointer ownership");}}))
    };
    static int Main()
    {
        int failed=0;foreach(var c in Cases)try{c.Test();Console.WriteLine("PASS "+c.Name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+c.Name+": "+e.Message);}
        Console.WriteLine($"RESULT {Cases.Length-failed} passed / {failed} failed");return failed==0?0:1;
    }
}

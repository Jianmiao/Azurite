// Only the external NGUI/IL2CPP callback boundary is substituted. The linked
// production Panel, Workflow and native Host orchestration all run unchanged.
public sealed class UILabel { public string text="";public bool supportEncoding=true; }
public sealed class UIButton : NativeObject
{
    public bool isEnabled=true;
    public List<EventDelegate> onClick=new();
    public void Click(){if(isEnabled)foreach(var callback in onClick.ToArray())callback.CallbackValue();}
}
public sealed class EventDelegate
{
    public delegate void Callback();public Callback CallbackValue=null!;
    public static void Add(List<EventDelegate> list,Callback callback)=>list.Add(new(){CallbackValue=callback});
    public static void Remove(List<EventDelegate> list,Callback callback)=>list.RemoveAll(item=>item.CallbackValue==callback);
}
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T? ConvertDelegate<T>(Action action) where T:Delegate=>(T)(Delegate)new EventDelegate.Callback(action);
    }
}

using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace Azurite.NormalEditor.Adapter.Host;

internal sealed class NativeAccess : INativeAccess
{
    private readonly NativeBindings bindings;
    internal NativeAccess(NativeBindings bindings)=>this.bindings=bindings;
    [DllImport("kernel32.dll",ExactSpelling=true)]private static extern uint GetCurrentThreadId();
    public uint NativeThreadId=>GetCurrentThreadId();
    private static IntPtr P(long pointer)=>(IntPtr)pointer;
    private static long Ref(long pointer,int offset)=>Marshal.ReadIntPtr(P(pointer),offset).ToInt64();
    private static string? Text(long pointer,int offset)=>IL2CPP.Il2CppStringToManaged((IntPtr)Ref(pointer,offset));
    public NativeKind KindOf(long pointer)
    {
        if(pointer==0)return NativeKind.Other;
        var type=IL2CPP.il2cpp_object_get_class(P(pointer));
        foreach(var (name,kind) in new[]{("UserSettings",NativeKind.Settings),("UserSettings+_InitTask_d__36",NativeKind.Init),("UserSettings+_ReadUserSettings_d__43",NativeKind.Read),("UserSettings+_SaveTask_d__41",NativeKind.Save),("ScenarioResourceManager",NativeKind.Resource)})
            if(type==bindings.Classes[name])return kind;
        return NativeKind.Other;
    }
    public unsafe NativeSettings ReadSettings(long owner)
    {
        if(KindOf(owner)!=NativeKind.Settings)throw new InvalidOperationException("Not the exact settings class.");
        var data=Ref(owner,0x28);
        if(data!=0 && IL2CPP.il2cpp_object_get_class(P(data))!=bindings.Classes["UserSettings+SettingData"])throw new InvalidOperationException("Unexpected settings data class.");
        bool cli=false;IL2CPP.il2cpp_field_static_get_value(bindings.Fields[("AzureArchive.Automation.AuthoringCliHost","<IsActive>k__BackingField")],&cli);
        return new(owner,data,Marshal.ReadByte(P(owner),0x60)!=0,Marshal.ReadByte(P(owner),0x40)!=0,Text(owner,0x38),Text(owner,0x68),
            data==0?null:Text(data,0xa0),data==0?null:Text(data,0x98),data==0?null:Text(data,0x38),cli,!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZUREARCHIVE_CLI_DIRECTORY")));
    }
    public NativeIterator ReadIterator(long pointer)
    {
        var kind=KindOf(pointer);
        if(kind is not (NativeKind.Init or NativeKind.Read or NativeKind.Save))return new(pointer,0,kind,0,0);
        return new(pointer,Ref(pointer,0x20),kind,Marshal.ReadInt32(P(pointer),0x10),Ref(pointer,0x18));
    }
    public void SetSettingsPath(long owner,string path)=>Write(owner,0x38,path);
    public void SetCachedWorkspace(long owner,string path)=>Write(owner,0x68,path);
    private void Write(long owner,int offset,string value)
    {
        if(KindOf(owner)!=NativeKind.Settings)throw new InvalidOperationException("Route owner class changed.");
        IL2CPP.il2cpp_gc_wbarrier_set_field(P(owner),P(owner+offset),IL2CPP.ManagedStringToIl2Cpp(value));
    }
}

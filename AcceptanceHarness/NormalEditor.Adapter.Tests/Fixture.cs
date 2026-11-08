using System.Security.Cryptography;
using System.Text.Json;
using Azurite.EditorIsolation;
using Azurite.NormalEditor;
using Azurite.NormalEditor.Adapter;

sealed class FakeAdmission : IAdapterAdmission
{
    public string EvidenceHash = new('a',64);
    public string EvidenceSha256 => EvidenceHash;
    public bool Revoked, ResourceAllowed=true, RouteAllowed=true;
    public readonly List<RouteRecord> Routes=new();
    public Action? BeforeRoute;
    void Check() { if(Revoked) throw new InvalidOperationException("fixture revoked"); }
    public void AssertPermitted(NormalBinding b,NormalStep s,SettingsObservation o)=>Check();
    public void RequireInstallation(AdapterBinding b)=>Check();
    public void RequireCallback(AdapterBinding b,CallbackKind k,long p)=>Check();
    public void RequireRouting(AdapterBinding b,RouteRecord r) { Check();if(!RouteAllowed)throw new InvalidOperationException("fixture route denied");BeforeRoute?.Invoke(); }
    public void RecordRouting(RouteRecord r) { Check();Routes.Add(r); }
    public void RequireResources(AdapterBinding b,long r,NativeSettings s) { Check();if(!ResourceAllowed)throw new InvalidOperationException("fixture resource denied"); }
}
sealed class FakeNative : INativeAccess
{
    public uint NativeThreadId {get;set;}=7;
    public NativeSettings Settings = new(100,0,false,false,"original-before-awake","original-workspace",null,null,null,false,false);
    public readonly Dictionary<long,NativeIterator> Iterators=new();
    public readonly List<string> Writes=new();
    public Action? AfterWrite;
    public NativeSettings ReadSettings(long owner) { if(owner!=Settings.Owner)throw new InvalidOperationException("wrong fixture owner");return Settings; }
    public NativeIterator ReadIterator(long p)=>Iterators[p];
    public NativeKind KindOf(long p)=>p==Settings.Owner?NativeKind.Settings:p==500?NativeKind.Resource:Iterators.TryGetValue(p,out var i)?i.Kind:NativeKind.Other;
    public void SetSettingsPath(long o,string p) { ReadSettings(o);Writes.Add("settingFilePath");Settings=Settings with {SettingsPath=p};AfterWrite?.Invoke(); }
    public void SetCachedWorkspace(long o,string p) { ReadSettings(o);Writes.Add("_cachedWorkspacePath");Settings=Settings with {CachedWorkspace=p};AfterWrite?.Invoke(); }
}
sealed class FakeFatal : IFatalSink { public int Calls;public void Terminate(string reason)=>Calls++; }
sealed class Fixture
{
    public readonly RunPaths Paths;
    public readonly AdapterBinding Binding;
    public readonly FakeNative Native=new();
    public readonly FakeAdmission Admission=new();
    public readonly FakeFatal Fatal=new();
    public readonly AdapterCoordinator Adapter;
    public AdapterFrame? AwakeFrame,CoroutineFrame,InitFrame;
    public Fixture(string parent,bool admitted=true)
    {
        var root=Path.Combine(parent,Guid.NewGuid().ToString("N"));var id=Guid.NewGuid().ToString("D");var lab=Path.Combine(root,"lab");
        Paths=new RunPaths(lab,Path.Combine(lab,"runs",id),Path.Combine(lab,"install"));
        foreach(var p in new[]{Paths.Install,Path.GetDirectoryName(Paths.Settings)!,Paths.Workspace,Paths.Cache,Paths.Persistent,Paths.Discovery,Paths.Output})Directory.CreateDirectory(p);
        File.WriteAllText(Paths.Settings,Text("lab-read-"+id));File.WriteAllText(Paths.Control,"control");
        Binding=new AdapterBinding(new NormalBinding(id,1,Environment.ProcessId,7,Paths,Hash(Paths.Settings),Hash(Paths.Control),"lab-read-"+id),"controller-bound-native-default-string");
        Native.Iterators[250]=new(250,100,NativeKind.Init,0,0);Native.Iterators[300]=new(300,100,NativeKind.Read,0,0);Native.Iterators[400]=new(400,100,NativeKind.Save,0,0);
        Adapter=new AdapterCoordinator(Binding,Native,Fatal,admitted?Admission:null);
    }
    public string Text(string marker)=>JsonSerializer.Serialize(new{username=marker,workspacePath=Paths.Workspace,cachePath=Paths.Cache});
    public static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
    public static void Must(bool value){if(!value)throw new Exception("expected callback admission");}
    public void Return(AdapterFrame? frame,CallbackKind kind,long pointer,bool original,bool? yielded=null)
    {
        Must(Adapter.Returned(frame,kind,pointer,original,yielded));
        var exception=FinalizerDispatch.Complete(Adapter,kind,pointer,frame,null);
        if(exception is not null || Adapter.Failure is not null)throw new Exception("valid finalizer refused");
    }
    public void BeforeYield()
    {
        Must(Adapter.AwakeEntered(100,out AwakeFrame));Must(Adapter.CoroutineEntered(100,250,out CoroutineFrame));
        Must(Adapter.IteratorEntered(250,NativeKind.Init,out InitFrame));
    }
    public void NativeYield()
    { Native.Settings=Native.Settings with{SettingsPath=Binding.ExpectedNativeSettingsPath};Native.Iterators[250]=Native.Iterators[250] with{State=1,Current=300}; }
    public void ReadChildReady()
    {
        BeforeYield();NativeYield();Return(InitFrame,CallbackKind.Init,250,true,true);
        Return(CoroutineFrame,CallbackKind.Coroutine,100,true);Return(AwakeFrame,CallbackKind.Awake,100,true);
    }
    public void ReadOriginal()
    {
        Must(Adapter.IteratorEntered(300,NativeKind.Read,out var frame));
        Native.Settings=Native.Settings with{Data=200,Marker=Binding.Policy.SeedMarker,DataWorkspace=Paths.Workspace,DataCache=Paths.Cache};Native.Iterators[300]=Native.Iterators[300] with{State=-1};
        Return(frame,CallbackKind.Read,300,true,false);
    }
    public void Continue(bool resource=true)
    {
        ReadChildReady();ReadOriginal();Must(Adapter.IteratorEntered(250,NativeKind.Init,out InitFrame));Native.Iterators[250]=Native.Iterators[250] with{State=-1};
        if(resource){Must(Adapter.ResourceEntered(500,out var frame));Return(frame,CallbackKind.Resource,500,true);}
    }
    public void CaptureSaver()
    { Native.Settings=Native.Settings with{Ready=true};Must(Adapter.CoroutineEntered(100,400,out CoroutineFrame)); }
    public void InitialWait()
    { Must(Adapter.IteratorEntered(400,NativeKind.Save,out var frame));Native.Iterators[400]=Native.Iterators[400] with{State=1};Return(frame,CallbackKind.Save,400,true,true); }
    public void Complete()
    { Return(CoroutineFrame,CallbackKind.Coroutine,100,true);Return(InitFrame,CallbackKind.Init,250,true,false); }
    public void Start(bool resource=true){Continue(resource);CaptureSaver();InitialWait();Complete();}
    public void Save(string marker,bool dirty=true)
    {
        Native.Settings=Native.Settings with{Dirty=dirty};Must(Adapter.IteratorEntered(400,NativeKind.Save,out var frame));
        if(dirty){Must(Adapter.WriteEntered(100,out var write));SaveFiles(marker);Native.Settings=Native.Settings with{Dirty=false};Return(write,CallbackKind.Write,100,true);}
        Return(frame,CallbackKind.Save,400,true,true);
    }
    public void SaveFiles(string marker)
    {
        if(File.Exists(Paths.Settings+".bk.3"))File.Delete(Paths.Settings+".bk.3");
        for(int n=2;n>=1;n--)if(File.Exists(Paths.Settings+".bk."+n))File.Move(Paths.Settings+".bk."+n,Paths.Settings+".bk."+(n+1));
        File.Move(Paths.Settings,Paths.Settings+".bk.1");File.WriteAllText(Paths.Settings,Text(marker));
    }
}

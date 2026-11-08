using Azurite.NormalEditor.Adapter;
using Azurite.NormalEditor;
using Azurite.EditorIsolation;
if(!OperatingSystem.IsWindows())throw new InvalidOperationException("Windows fixtures required");
if(args.Length!=3)throw new ArgumentException("Pass a fresh fixture root, explicit pinned AA install for metadata only, and compiled adapter host DLL path.");
var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
int pass=0,fail=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);}
void Test(string name,Action body){try{body();Console.WriteLine("PASS "+name);pass++;}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e.Message);fail++;}}
Test("genuine Awake is admitted before only two authorized field routes",()=>{var f=new Fixture(output);Check(f.Adapter.AwakeEntered(100,out var frame),"Awake rejected");Check(frame is not null && f.Native.Settings.SettingsPath==f.Paths.Settings && f.Native.Settings.CachedWorkspace==f.Paths.Workspace,"routes absent");Check(f.Native.Writes.SequenceEqual(new[]{"settingFilePath","_cachedWorkspacePath"}),"wrong fields mutated");});
Test("reviewed normal policy binary is used unchanged",()=>Check(Fixture.Hash(typeof(NormalSettingsLifecycle).Assembly.Location)==BindingCatalog.NormalPolicyHash,"normal policy pin differs"));
Test("legacy policy binary is used unchanged",()=>Check(Fixture.Hash(typeof(RunPaths).Assembly.Location)==BindingCatalog.LegacyPolicyHash,"legacy policy pin differs"));
Test("old one-shot runtime still refuses normal mode",()=>{bool denied=false;try{new ProbeLedger(ProbeMode.NormalObserverCatalogOnly).Arm(false,true);}catch(InvalidOperationException){denied=true;}Check(denied,"old normal refusal changed");});
CoordinatorCases.Run(output,Test,Check);
FinalizerCases.Run(output,Test,Check);
BindingMetadataTests.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Test,Check);
Console.WriteLine($"RESULT passed={pass} failed={fail}; hostExecuted=false; productionAdmissionProvided=false");
return fail==0?0:1;

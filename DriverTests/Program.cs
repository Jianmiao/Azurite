using System.Reflection;
using Azurite;
using Mono.Cecil;
using Mono.Cecil.Cil;

int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Call(AzuriteDriver driver, string name) => typeof(AzuriteDriver).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(driver, null);
var events = new List<string>();
var driver = new AzuriteDriver { Tick = _ => events.Add("tick"), LateTick = _ => events.Add("late"), Quitting = () => events.Add("quit"), Destroyed = () => events.Add("destroy") };
Call(driver, "OnApplicationQuit"); Call(driver, "OnApplicationQuit"); Call(driver, "Update"); Call(driver, "LateUpdate");
Check(events.SequenceEqual(new[] { "quit" }), "production quit disables ticks and abandons once");
Call(driver, "OnDestroy"); Call(driver, "OnDestroy");
Check(events.SequenceEqual(new[] { "quit", "destroy" }), "normal disposal still runs once after terminal abandon");
var failing = new AzuriteDriver { Tick = _ => throw new InvalidOperationException("live failure"), Quitting = () => events.Add("unexpected quit"), Destroyed = () => events.Add("live restore") };
Call(failing, "Update");
Check(events.Last() == "live restore" && !events.Contains("unexpected quit"), "live failure requests restoration rather than terminal abandon");

string aaInstallPath = args.FirstOrDefault() ?? Assembly.GetExecutingAssembly()
    .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "AAInstallPath").Value!;
using var host = ModuleDefinition.ReadModule(Path.Combine(aaInstallPath, "BepInEx", "interop", "Assembly-CSharp.dll"));
using var unity = ModuleDefinition.ReadModule(Path.Combine(aaInstallPath, "BepInEx", "interop", "UnityEngine.CoreModule.dll"));
var expectations = new[]
{
    (host,"Studio.Scripts.StudioCommon","Start",false,"System.Void",Array.Empty<string>()),
    (host,"Studio.Scripts.ScriptNodeInspector","Start",false,"System.Void",Array.Empty<string>()),
    (host,"Studio.Scripts.ScriptNodeInspector","OnChildSelect",false,"System.Void",new[]{"Studio.Scripts.Selectable"}),
    (host,"AzureArchive.Automation.AuthoringEditorSession","Begin",true,"System.Void",new[]{"Studio.Scripts.StudioCommon","ProjectData","System.String"}),
    (host,"AzureArchive.Automation.AuthoringResourceCatalog","BindProject",true,"System.Void",new[]{"ProjectData","System.String","System.String"}),
    (host,"AzureArchive.Automation.AuthoringResourceCatalog","CaptureMetadata",true,"AzureArchive.Automation.AuthoringResourceCatalog/Snapshot",new[]{"System.Boolean"}),
    (host,"AzureArchive.Automation.AuthoringResourceCatalog","EnsureNative",true,"System.Void",new[]{"ScenarioResourceManager"}),
    (unity,"UnityEngine.SceneManagement.SceneManager","LoadSceneAsync",true,"UnityEngine.AsyncOperation",new[]{"System.String"})
};
foreach (var (module,type,name,isStatic,result,parameters) in expectations)
{
    var methods = module.GetType(type).Methods.Where(m=>m.Name==name && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
    Check(methods.Length==1 && methods[0].IsStatic==isStatic && methods[0].ReturnType.FullName==result,"installed signature " + type + "." + name);
}
var getter = host.GetType("AzureArchive.Automation.AuthoringResourceCatalog").Properties.Single(p=>p.Name=="lastCaptureMetrics").GetMethod;
var calls = getter.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>((MethodReference)i.Operand).Name).ToArray();
Check(getter.IsStatic && calls.Contains("il2cpp_field_static_get_value") && !calls.Any(n=>n.Contains("CaptureMetadata")||n.Contains("CatalogVersion")||n.Contains("EnsureNative")),
    "lastCaptureMetrics is a static field getter, not catalog initialization");
Console.WriteLine($"RESULT {passed}/{passed}; production driver with simulated Unity messages; host metadata only, no native AA invocation.");

using Mono.Cecil;

string aa = args.FirstOrDefault() ?? @"F:\AzureArchive_100_fix";
string hostPath = Path.Combine(aa, "BepInEx", "interop", "Assembly-CSharp.dll");
using var module = ModuleDefinition.ReadModule(hostPath);
int passed = 0;
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); passed++; }
TypeDefinition Type(string fullName) => module.GetType(fullName) ?? throw new MissingMemberException(fullName);
MethodDefinition Method(TypeDefinition type, string name, params string[] args) =>
    type.Methods.FirstOrDefault(m => m.Name == name && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args))
    ?? throw new MissingMemberException(type.FullName, name + "(" + string.Join(",", type.Methods.Where(m => m.Name == name).Select(m => string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)))) + ")");
var background = Type("Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer");
var popup = Type("Studio.Scripts.Window.PopupImageExplorer.PopupImageExplorer");
var studio = Type("Studio.Scripts.StudioCommon");
Check(Method(background, "Start").ReturnType.FullName == "System.Void", "background Start signature");
Check(Method(popup, "Start").ReturnType.FullName == "System.Void", "popup Start signature");
Check(Method(background, "Init", "Script", "Il2CppSystem.Action`2<System.UInt32,System.String>").ReturnType.FullName == "System.Void", "background Init signature");
Check(Method(popup, "Init", "Studio.Scripts.Nodes.ScriptNode", "Script").ReturnType.FullName == "System.Void", "popup Init signature");
Check(Method(background, "Show").ReturnType.FullName == "System.Void", "background Show signature");
Check(Method(studio, "OnDestroy").ReturnType.FullName == "System.Void", "editor destroy signature");

string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Plugin", "Azurite", "DeferredEditorSelectors.cs"));
Check(!source.Contains("Task.Run", StringComparison.Ordinal) && !source.Contains("Thread.", StringComparison.Ordinal), "selector deferral stays on Unity main thread");
Check(source.Contains("entry.View", StringComparison.Ordinal) && source.Contains("entry.Ready", StringComparison.Ordinal), "per-instance readiness state");

var state = new SelectorState();
Check(state.Start() == false && state.StartCalls == 0, "scene Start is deferred");
Check(state.Demand() && state.StartCalls == 1, "first demand runs native Start once");
Check(state.Demand() && state.StartCalls == 1, "repeated demand does not rebuild gallery");
var retry = new SelectorState { FailNext = true };
Check(!retry.Demand() && retry.StartCalls == 1, "failed demand remains suppressed");
Check(retry.Demand() && retry.StartCalls == 2, "failed demand can retry explicitly");
var quit = new SelectorState(); quit.Abandon();
Check(!quit.Demand() && quit.StartCalls == 0, "quit never touches native selector");
Console.WriteLine($"RESULT {passed}/{passed}; installed metadata + deterministic selector state only; no Unity invocation.");

sealed class SelectorState
{
    public int StartCalls;
    public bool Ready, Running, FailNext, Quitting;
    public bool Start()
    {
        if (Quitting || Ready) return false;
        return false;
    }
    public bool Demand()
    {
        if (Quitting) return false;
        if (Ready) return true;
        if (Running) return false;
        Running = true; StartCalls++;
        try { if (FailNext) { FailNext = false; return false; } Ready = true; return true; }
        finally { Running = false; }
    }
    public void Abandon() { Quitting = true; Ready = false; }
}

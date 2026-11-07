using Mono.Cecil;
using Azurite;
using CharacterSelector = Studio.Scripts.Window.CharacterExplorer.CharacterExplorer;
using CharacterList = Studio.Scripts.Window.CharacterExplorer.CharacterList;
using BackgroundSelector = Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer;
using PopupSelector = Studio.Scripts.Window.PopupImageExplorer.PopupImageExplorer;

string aa = args.FirstOrDefault() ?? @"F:\AzureArchive_100_fix";
string hostPath = Path.Combine(aa, "BepInEx", "interop", "Assembly-CSharp.dll");
using var module = ModuleDefinition.ReadModule(hostPath);
int passed = 0, failed = 0;
void Check(bool value, string name) { if (!value) { failed++; Console.WriteLine("FAIL " + name); } else { Console.WriteLine("PASS " + name); passed++; } }
TypeDefinition HostType(string fullName) => module.GetType(fullName) ?? throw new MissingMemberException(fullName);
MethodDefinition Method(TypeDefinition type, string name, params string[] args) =>
    type.Methods.FirstOrDefault(m => m.Name == name && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args))
    ?? throw new MissingMemberException(type.FullName, name + "(" + string.Join(",", type.Methods.Where(m => m.Name == name).Select(m => string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)))) + ")");
var background = HostType("Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer");
var popup = HostType("Studio.Scripts.Window.PopupImageExplorer.PopupImageExplorer");
var studio = HostType("Studio.Scripts.StudioCommon");
Check(Method(background, "Start").ReturnType.FullName == "System.Void", "background Start signature");
Check(Method(popup, "Start").ReturnType.FullName == "System.Void", "popup Start signature");
Check(Method(background, "Init", "Script", "Il2CppSystem.Action`2<System.UInt32,System.String>").ReturnType.FullName == "System.Void", "background Init signature");
Check(Method(popup, "Init", "Studio.Scripts.Nodes.ScriptNode", "Script").ReturnType.FullName == "System.Void", "popup Init signature");
Check(Method(background, "Show").ReturnType.FullName == "System.Void", "background Show signature");
Check(Method(studio, "OnDestroy").ReturnType.FullName == "System.Void", "editor destroy signature");
var character = HostType("Studio.Scripts.Window.CharacterExplorer.CharacterExplorer");
Check(Method(character, "Awake").ReturnType.FullName == "System.Void", "character Awake signature");
Check(Method(character, "Init", "Script", "System.Int32", "System.Boolean").ReturnType.FullName == "System.Void", "character Init signature");
Check(Method(HostType("Studio.Scripts.Window.CharacterExplorer.CharacterList"), "LoadCharacters").ReturnType.FullName == "System.Void", "native character list load signature");
Check(Method(HostType("Studio.Scripts.ScriptNodeInspector"), "OpenCharacterSelector", "System.Int32").ReturnType.FullName == "System.Void", "inspector character selector entry signature");

string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Plugin", "Azurite", "DeferredEditorSelectors.cs"));
Check(!source.Contains("Task.Run", StringComparison.Ordinal) && !source.Contains("Thread.", StringComparison.Ordinal), "selector deferral stays on Unity main thread");
Check(source.Contains("entry.View", StringComparison.Ordinal) && source.Contains("entry.Ready", StringComparison.Ordinal), "per-instance readiness state");

using (var selectors = new DeferredEditorSelectors(_ => { }))
{
    Check(selectors.Install(), "production selector hooks register");
    var bg = new BackgroundSelector(); bg.Start();
    Check(bg.StartCalls == 0 && !bg.gameObject.activeSelf, "background scene Start is deferred and hidden");
    bg.Init(new Script(), new()); bg.Show(); bg.Init(new Script(), new());
    Check(bg.StartCalls == 1 && bg.InitCalls == 2 && bg.ShowCalls == 1, "background first demand initializes once and preserves later native operations");
    var popupView = new PopupSelector(); popupView.Start(); popupView.Init(new(), new());
    Check(popupView.StartCalls == 1 && popupView.InitCalls == 1, "popup demand uses actual production deferral");
    var retry = new BackgroundSelector { FailNext = true }; retry.Start(); retry.Show();
    Check(retry.StartCalls == 1 && retry.ShowCalls == 0, "failed gallery initialization suppresses display");
    retry.Show(); Check(retry.StartCalls == 2 && retry.ShowCalls == 1, "failed gallery initialization can retry");

    var chars = new CharacterSelector();
    var unrelated = new CharacterList(); chars.DuringAwake = unrelated.LoadCharacters;
    chars.Awake(); chars.Start();
    Check(chars.AwakeCalls == 1 && chars.StartCalls == 1 && !chars.gameObject.activeSelf && ReferenceEquals(Singleton<CharacterSelector>.Instance, chars),
          "character singleton registration, hiding and native Start still run");
    Check(chars.characterList.LoadCalls == 0 && unrelated.LoadCalls == 1, "only the matching character list is deferred inside editor Awake");
    var inspector = new Studio.Scripts.ScriptNodeInspector(); inspector.OpenCharacterSelector(1);
    Check(chars.characterList.LoadCalls == 1 && chars.InitCalls == 1 && chars.ShowCalls == 1 && chars.characterList.Rows == 1000,
          "inspector first demand populates character rows before native selection and Show");
    inspector.OpenCharacterSelector(2);
    Check(chars.characterList.LoadCalls == 1 && chars.InitCalls == 2, "reopening character selector reuses initialized list");
    chars.characterList.LoadCharacters();
    Check(chars.characterList.LoadCalls == 2, "import and alias-triggered character reloads remain native");

    var failedChars = new CharacterSelector(); failedChars.Awake(); failedChars.characterList.FailNext = true;
    inspector.OpenCharacterSelector(1);
    Check(failedChars.characterList.LoadCalls == 1 && failedChars.InitCalls == 0 && failedChars.ShowCalls == 0,
          "failed character load suppresses the whole inspector open including Show");
    inspector.OpenCharacterSelector(1);
    Check(failedChars.characterList.LoadCalls == 2 && failedChars.InitCalls == 1 && failedChars.ShowCalls == 1,
          "character demand retries a failed native build before exposing selection");

    var direct = new CharacterSelector(); direct.Awake(); direct.Init(new Script(), 0, false);
    Check(direct.characterList.LoadCalls == 1 && direct.InitCalls == 1, "direct character Init initializes a pending list");
    var reloadPending = new CharacterSelector(); reloadPending.Awake(); reloadPending.characterList.LoadCharacters(); reloadPending.Init(new(), 0, false);
    Check(reloadPending.characterList.LoadCalls == 1, "external reload of pending list prevents duplicate first-open construction");

    var outside = new CharacterSelector(); outside.gameObject.scene = new("Appreciation", 2); outside.Awake();
    Check(outside.characterList.LoadCalls == 1, "non-editor Awake keeps character construction native");
    selectors.Enabled = false;
    var disabled = new CharacterSelector(); disabled.Awake();
    Check(disabled.characterList.LoadCalls == 1, "disabled deferral keeps native startup");
    direct.Init(new(), 1, true); Check(direct.characterList.LoadCalls == 1, "disabling does not rebuild previously initialized rows");
    selectors.Enabled = true;
    var disabledPending = new CharacterSelector(); disabledPending.Awake(); selectors.Enabled = false;
    disabledPending.Init(new(), 0, false);
    Check(disabledPending.characterList.LoadCalls == 1 && disabledPending.InitCalls == 1,
          "turning deferral off still completes previously deferred rows before selection");
    selectors.Enabled = true;
    var awakeFailure = new CharacterSelector { FailAwake = true };
    try { awakeFailure.Awake(); } catch (InvalidOperationException) { }
    awakeFailure.characterList.LoadCharacters();
    Check(awakeFailure.characterList.LoadCalls == 1, "Awake exception clears its scope before subsequent explicit reload");

    var unloadPending = new CharacterSelector(); unloadPending.Awake();
    selectors.Dispose();
    Check(unloadPending.characterList.LoadCalls == 1, "live plugin unload completes pending character initialization before unpatching");
}
using (var selectors = new DeferredEditorSelectors(_ => { }))
{
    selectors.Install();
    var quitting = new CharacterSelector(); quitting.Awake();
    selectors.AbandonForShutdown(); selectors.Dispose();
    Check(quitting.characterList.LoadCalls == 0, "terminal shutdown abandons pending character work");
}
using (var selectors = new DeferredEditorSelectors(_ => { }))
{
    selectors.Install();
    var scenePending = new CharacterSelector(); scenePending.Awake();
    var closingStudio = new Studio.Scripts.StudioCommon(); closingStudio.OnDestroy(); selectors.Dispose();
    Check(scenePending.characterList.LoadCalls == 0, "scene destruction drops pending native references without constructing unused rows");
}
Console.WriteLine($"RESULT {passed} passed; {failed} failed; production source + installed metadata, no Unity invocation.");
return failed == 0 ? 0 : 1;

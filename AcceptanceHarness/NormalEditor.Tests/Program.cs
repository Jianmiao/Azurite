using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using Azurite.EditorIsolation;
using Azurite.NormalEditor;

if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows filesystem fixtures required.");
var legacyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(RunPaths).Assembly.Location))).ToLowerInvariant();
if (legacyHash != "2f6297caec213d87464efad1a387230e40943b89eb71333fb65c8e03dc0d7653") throw new InvalidOperationException("Frozen legacy policy DLL differs.");
var output = Path.GetFullPath(args.Length == 1 ? args[0] : Path.Combine(AppContext.BaseDirectory, "fixtures"));
Directory.CreateDirectory(output);
int passed = 0, failed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Deny(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("invalid transition was admitted"); }
void Test(string name, Action action) { try { action(); Console.WriteLine("PASS " + name); passed++; } catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error.Message); failed++; } }
Fixture New(bool admitted = true) => new(output, admitted);

Test("external normal admission is absent by default", () => { var f = New(false); Deny(() => f.Ledger.BindAwake(f.Observe(), false, false)); Check(f.Ledger.Failure is not null && f.Ledger.CompletedWrites == 0, "default admission produced progress"); });
Test("old one-shot normal mode remains closed", () => Deny(() => new ProbeLedger(ProbeMode.NormalObserverCatalogOnly).Arm(false, true)));
Test("CLI-active owner is not normal startup", () => { var f = New(); Deny(() => f.Ledger.BindAwake(f.Observe(), true, false)); });
Test("CLI transport presence is rejected", () => { var f = New(); Deny(() => f.Ledger.BindAwake(f.Observe(), false, true)); });
Test("already ready first owner is rejected", () => { var f = New(); f.Ready = true; Deny(() => f.Ledger.BindAwake(f.Observe(), false, false)); });
Test("already populated first owner is rejected", () => { var f = New(); f.Data = 200; Deny(() => f.Ledger.BindAwake(f.Observe(), false, false)); });
Test("native OnReady resource start before ready is valid", () => { var f = New(); f.ReadToOnReady(); f.Ledger.ResourceInitializationObserved(f.Observe()); Check(!f.Ledger.ReadyObserved && f.Ledger.ResourceStartObservations == 1, "early callback was relabelled ready"); f.FinishStartup(); });
Test("SaveTask callback may be first later true-ready observation", () => { var f = New(); f.ReadToOnReady(); f.Ledger.OnReadyReturned(f.Observe()); f.Ready = true; f.BindSaver(); Check(f.Ledger.ReadyObserved, "genuine later flag was not observed"); });
Test("resource start before OnReady is rejected", () => { var f = New(); f.ReadOnly(); Deny(() => f.Ledger.ResourceInitializationObserved(f.Observe())); });
Test("ready flag inside OnReady is rejected", () => { var f = New(); f.ReadToOnReady(); f.Ready = true; Deny(() => f.Ledger.ResourceInitializationObserved(f.Observe())); });
Test("OnReady needs successful original read", () => { var f = New(); f.Ledger.BindAwake(f.Observe(), false, false); Deny(() => f.Ledger.OnReadyEntered(f.Observe())); });
Test("read fallback yield is rejected", () => { var f = New(); f.BeginRead(); f.Data = 200; Deny(() => f.Ledger.OriginalReadReturned(f.Observe(), 300, true, f.Binding.SeedMarker)); });
Test("read result seed marker mismatch is rejected", () => { var f = New(); f.BeginRead(); f.Data = 200; Deny(() => f.Ledger.OriginalReadReturned(f.Observe(), 300, false, "other")); });
Test("read iterator mismatch is rejected", () => { var f = New(); f.BeginRead(); f.Data = 200; Deny(() => f.Ledger.OriginalReadReturned(f.Observe(), 301, false, f.Binding.SeedMarker)); });
Test("two dirty natural cycles complete observed policy lineage", () => { var f = New(); f.Start(); f.Save("1"); f.Save("2"); Check(f.Ledger.CompletedWrites == 2 && f.Ledger.CompletedSaveResumes == 2 && f.Ledger.RepeatedSaveLineageObserved, "valid repeated writes did not complete"); });
Test("four saves preserve three-backup rotation", () => { var f = New(); f.Start(); var hashes = new List<string> { Fixture.Hash(f.Paths.Settings) }; for (int i = 1; i <= 4; i++) { f.Save(i.ToString()); hashes.Add(Fixture.Hash(f.Paths.Settings)); } Check(Fixture.Hash(f.Paths.Settings + ".bk.1") == hashes[3] && Fixture.Hash(f.Paths.Settings + ".bk.3") == hashes[1], "backup rotation differs"); Check(f.Ledger.CompletedWrites == 4, "later write refused"); });
Test("identical serialized content can still be a genuine save", () => { var f = New(); f.Start(); f.Save("same"); f.Save("same"); Check(f.Ledger.RepeatedSaveLineageObserved, "same-content original returns were rejected"); });
Test("clean state-one resume counts no write", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, false); f.Ledger.SaveMoveNextReturned(f.Observe(), t, 400, true, 1); Check(f.Ledger.CompletedSaveResumes == 1 && f.Ledger.CompletedWrites == 0 && !f.Ledger.RepeatedSaveLineageObserved, "clean cycle counted as a save"); });
Test("initial state-zero delay cannot write", () => { var f = New(); f.ReadToOnReady(); f.Ledger.OnReadyReturned(f.Observe()); f.Ready = true; f.Ledger.SaveTaskCreated(f.Observe(), 400); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 0, true); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); });
Test("state-one before initial delay is rejected", () => { var f = New(); f.ReadToOnReady(); f.Ledger.OnReadyReturned(f.Observe()); f.Ready = true; f.Ledger.SaveTaskCreated(f.Observe(), 400); Deny(() => f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true)); });
Test("dirty resume without write cannot commit", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); Deny(() => f.Ledger.SaveMoveNextReturned(f.Observe(), t, 400, true, 1)); Check(f.Ledger.CompletedSaveResumes == 0 && f.Ledger.CompletedWrites == 0, "refused cycle counted"); });
Test("write on clean resume is rejected", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, false); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); });
Test("write count waits for enclosing original SaveTask return", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); var w = f.Ledger.AdmitWrite(f.Observe(), t); f.SimulateSaveResult("1"); f.Ledger.OriginalWriteReturned(f.Observe(), w); Check(f.Ledger.CompletedWrites == 0 && f.Ledger.WriteGeneration == 0, "premature write commit"); f.Ledger.SaveMoveNextReturned(f.Observe(), t, 400, true, 1); Check(f.Ledger.CompletedWrites == 1, "original return not committed"); });
Test("invalid original SaveTask return never commits pending write", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); var w = f.Ledger.AdmitWrite(f.Observe(), t); f.SimulateSaveResult("1"); f.Ledger.OriginalWriteReturned(f.Observe(), w); Deny(() => f.Ledger.SaveMoveNextReturned(f.Observe(), t, 400, false, -1)); Check(f.Ledger.CompletedWrites == 0 && !f.Ledger.RepeatedSaveLineageObserved, "invalid return counted as feature"); });
Test("reentrant SaveTask resume is rejected", () => { var f = New(); f.Start(); f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); Deny(() => f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true)); });
Test("reentrant native write is rejected", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); f.Ledger.AdmitWrite(f.Observe(), t); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); });
Test("stale completed invocation cannot write again", () => { var f = New(); f.Start(); var t = f.Save("1"); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); Check(f.Ledger.CompletedWrites == 1, "stale write counted"); });
Test("foreign invocation token is rejected", () => { var a = New(); var b = New(); a.Start(); b.Start(); var t = a.Ledger.SaveMoveNextEntered(a.Observe(), 400, 1, true); b.Ledger.SaveMoveNextEntered(b.Observe(), 400, 1, true); Deny(() => b.Ledger.AdmitWrite(b.Observe(), t)); });
Test("stale write permit is rejected", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); var w = f.Ledger.AdmitWrite(f.Observe(), t); f.SimulateSaveResult("1"); f.Ledger.OriginalWriteReturned(f.Observe(), w); Deny(() => f.Ledger.OriginalWriteReturned(f.Observe(), w)); Check(f.Ledger.CompletedWrites == 0, "duplicate return counted"); });
Test("wrong owner is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.ReadinessObserved(f.Observe() with { Owner = 101 })); });
Test("data identity drift is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.ReadinessObserved(f.Observe() with { Data = 201 })); });
Test("run generation drift is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.ReadinessObserved(f.Observe() with { Generation = 2 })); });
Test("different run is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.ReadinessObserved(f.Observe() with { RunId = Guid.NewGuid().ToString("D") })); });
Test("stale observation sequence is rejected", () => { var f = New(); f.Start(); var o = f.Observe(); f.Ledger.ReadinessObserved(o); Deny(() => f.Ledger.ReadinessObserved(o)); });
Test("different observed native thread is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.ReadinessObserved(f.Observe() with { NativeThreadId = 99 })); });
Test("actual managed callback thread drift is rejected", () => { var f = New(); f.Start(); var o = f.Observe(); Task.Run(() => Deny(() => f.Ledger.ReadinessObserved(o))).GetAwaiter().GetResult(); });
Test("settings path drift is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.ReadinessObserved(f.Observe() with { SettingsPath = f.Paths.Control })); });
Test("cached workspace drift is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.ReadinessObserved(f.Observe() with { CachedWorkspacePath = f.Paths.Cache })); });
Test("data cache drift is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.ReadinessObserved(f.Observe() with { DataCachePath = f.Paths.Workspace })); });
Test("readiness regression is rejected", () => { var f = New(); f.Start(); f.Ready = false; Deny(() => f.Ledger.ReadinessObserved(f.Observe())); });
Test("fresh validation detects sidecar before each write", () => { var f = New(); f.Start(); f.Save("1"); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); File.WriteAllText(f.Paths.Settings + ".tmp", "foreign"); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); Check(f.Ledger.CompletedWrites == 1, "stale validation was reused"); });
Test("backup hash drift is rejected before later write", () => { var f = New(); f.Start(); f.Save("1"); File.WriteAllText(f.Paths.Settings + ".bk.1", "foreign"); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); });
Test("wrong backup rotation is rejected after original return", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); var w = f.Ledger.AdmitWrite(f.Observe(), t); f.SimulateSaveResult("1"); File.WriteAllText(f.Paths.Settings + ".bk.1", "foreign"); Deny(() => f.Ledger.OriginalWriteReturned(f.Observe(), w)); Check(f.Ledger.CompletedWrites == 0, "bad lineage counted"); });
Test("control seed mutation is rejected", () => { var f = New(); f.Start(); File.WriteAllText(f.Paths.Control, "changed"); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); });
Test("persisted path mismatch is rejected after write", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); var w = f.Ledger.AdmitWrite(f.Observe(), t); f.SimulateSaveResult("1"); File.WriteAllText(f.Paths.Settings, JsonSerializer.Serialize(new { workspacePath = f.Paths.Cache, cachePath = f.Paths.Cache })); Deny(() => f.Ledger.OriginalWriteReturned(f.Observe(), w)); });
Test("hardlinked accepted backup is rejected on fresh validation", () => { var f = New(); f.Start(); f.Save("1"); var alias = Path.Combine(f.Root, "hardlink-evidence.txt"); Check(Native.CreateHardLink(@"\\?\"+alias, @"\\?\"+f.Paths.Settings+".bk.1", IntPtr.Zero), "fixture hardlink failed"); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); });
Test("failure latch cannot become repeated-save success", () => { var f = New(); f.Start(); f.Save("1"); f.Ledger.Fail("fixture refusal"); Deny(() => f.Save("2")); Check(f.Ledger.CompletedWrites == 1 && !f.Ledger.RepeatedSaveLineageObserved, "refusal counted as completion"); });
Test("external admission revocation is consulted on later write", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true); f.Lease.Revoked = true; Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); });
Test("closed owner cannot receive later callbacks", () => { var f = New(); f.Start(); f.Ledger.Close(f.Observe()); Deny(() => f.Ledger.ReadinessObserved(f.Observe())); });
Test("physical seed marker must match its bound marker", () => { var f = New(); var ledger = new NormalSettingsLifecycle(f.Binding with { SeedMarker = "wrong-marker" }, f.Lease); ledger.BindAwake(f.Observe(),false,false); Deny(() => ledger.ReadEntered(f.Observe(),300,0)); });
Test("original read return cannot skip its native prefix", () => { var f = New(); f.Ledger.BindAwake(f.Observe(),false,false); f.Data=200; Deny(() => f.Ledger.OriginalReadReturned(f.Observe(),300,false,f.Binding.SeedMarker)); });
Test("wrong SaveTask iterator is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.SaveMoveNextEntered(f.Observe(),401,1,true)); });
Test("nested admission callback refusal cannot be swallowed", () => { var f = New(); f.Start(); var t=f.Ledger.SaveMoveNextEntered(f.Observe(),400,1,true); f.Lease.OnAssert=step => { if(step==NormalStep.WriteEnter) { try { f.Ledger.ReadinessObserved(f.Observe()); } catch(InvalidOperationException) { } } }; Deny(() => f.Ledger.AdmitWrite(f.Observe(),t)); Check(f.Ledger.CompletedWrites==0 && f.Ledger.Failure is not null,"nested refusal returned a write permit"); });
Test("snapshot size bound rejects oversized settings before native read", () => { var f=New(); File.WriteAllText(f.Paths.Settings,new string('x',(int)SettingsSnapshotReader.MaximumFileBytes+1)); f.Ledger.BindAwake(f.Observe(),false,false); Deny(() => f.Ledger.ReadEntered(f.Observe(),300,0)); });
Test("fresh write validation rejects reparse backup", () => { var f=New();f.Start();f.Save("1");var target=Path.Combine(f.Root,"junction-target");Directory.CreateDirectory(target);var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"/d","/c","mklink","/J",f.Paths.Settings+".bk.2",target})start.ArgumentList.Add(arg);using var child=System.Diagnostics.Process.Start(start)!;if(!child.WaitForExit(5000)){child.Kill(true);throw new Exception("fixture junction timed out");}Check(child.ExitCode==0,"fixture junction failed");var t=f.Ledger.SaveMoveNextEntered(f.Observe(),400,1,true);Deny(() => f.Ledger.AdmitWrite(f.Observe(),t)); });
Test("post-write drift cannot be committed at SaveTask return", () => { var f=New();f.Start();var t=f.Ledger.SaveMoveNextEntered(f.Observe(),400,1,true);var w=f.Ledger.AdmitWrite(f.Observe(),t);f.SimulateSaveResult("1");f.Ledger.OriginalWriteReturned(f.Observe(),w);File.WriteAllText(f.Paths.Settings+".bk.1","changed-after-write-return");Deny(() => f.Ledger.SaveMoveNextReturned(f.Observe(),t,400,true,1));Check(f.Ledger.CompletedWrites==0 && f.Ledger.CompletedSaveResumes==0,"stale post-write snapshot committed"); });

Console.WriteLine($"RESULT passed={passed} failed={failed}; fixtureRoot={output}; nativeAAExecuted=false; externalProductionAdmissionProvided=false");
return failed == 0 ? 0 : 1;

sealed class FixtureAdmission : INormalAdmissionLease
{
    public string EvidenceSha256 => new('a', 64);
    public bool Revoked;
    public Action<NormalStep>? OnAssert;
    public void AssertPermitted(NormalBinding binding, NormalStep step, SettingsObservation observation)
    { if (Revoked) throw new InvalidOperationException("fixture lease revoked"); OnAssert?.Invoke(step); }
}

sealed class Fixture
{
    public readonly string Root;
    public readonly RunPaths Paths;
    public readonly NormalBinding Binding;
    public readonly NormalSettingsLifecycle Ledger;
    public readonly FixtureAdmission Lease = new();
    public long Data;
    public bool Ready;
    private long sequence;
    public Fixture(string output, bool admitted)
    {
        Root = Path.Combine(output, Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid().ToString("D"); var lab = Path.Combine(Root,"lab");
        Paths = new RunPaths(lab, Path.Combine(lab,"runs",id), Path.Combine(lab,"install"));
        foreach (var path in new[] { Paths.Install, Path.GetDirectoryName(Paths.Settings)!, Paths.Workspace, Paths.Cache, Paths.Persistent, Paths.Discovery, Paths.Output }) Directory.CreateDirectory(path);
        File.WriteAllText(Paths.Settings, Text("lab-read-"+id)); File.WriteAllText(Paths.Control,"control-seed");
        Binding = new NormalBinding(id,1,Environment.ProcessId,7,Paths,Hash(Paths.Settings),Hash(Paths.Control),"lab-read-"+id);
        Ledger = new NormalSettingsLifecycle(Binding, admitted ? Lease : null);
    }
    public SettingsObservation Observe() => new(Binding.RunId,Binding.Generation,Binding.ProcessId,Binding.NativeThreadId,++sequence,100,Data,Ready,Paths.Settings,Paths.Workspace,Data==0?"":Paths.Workspace,Data==0?"":Paths.Cache);
    public void BeginRead() { Ledger.BindAwake(Observe(),false,false); Ledger.ReadEntered(Observe(),300,0); }
    public void ReadOnly() { BeginRead(); Data=200; Ledger.OriginalReadReturned(Observe(),300,false,Binding.SeedMarker); }
    public void ReadToOnReady() { ReadOnly(); Ledger.OnReadyEntered(Observe()); }
    public void FinishStartup() { Ledger.OnReadyReturned(Observe()); Ready=true; Ledger.ReadinessObserved(Observe()); BindSaver(); }
    public void Start() { ReadToOnReady(); Ledger.ResourceInitializationObserved(Observe()); FinishStartup(); }
    public void BindSaver() { Ledger.SaveTaskCreated(Observe(),400); var t=Ledger.SaveMoveNextEntered(Observe(),400,0,false); Ledger.SaveMoveNextReturned(Observe(),t,400,true,1); }
    public SaveInvocation Save(string marker) { var t=Ledger.SaveMoveNextEntered(Observe(),400,1,true); var w=Ledger.AdmitWrite(Observe(),t); SimulateSaveResult(marker); Ledger.OriginalWriteReturned(Observe(),w); Ledger.SaveMoveNextReturned(Observe(),t,400,true,1); return t; }
    public void SimulateSaveResult(string marker) { if(File.Exists(Paths.Settings+".bk.3"))File.Delete(Paths.Settings+".bk.3"); for(int n=2;n>=1;n--)if(File.Exists(Paths.Settings+".bk."+n))File.Move(Paths.Settings+".bk."+n,Paths.Settings+".bk."+(n+1)); File.Move(Paths.Settings,Paths.Settings+".bk.1");File.WriteAllText(Paths.Settings,Text(marker)); }
    private string Text(string marker) => JsonSerializer.Serialize(new { username=marker, workspacePath=Paths.Workspace, cachePath=Paths.Cache });
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

static class Native
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateHardLinkW")]
    [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateHardLink(string path,string existing,IntPtr security);
}

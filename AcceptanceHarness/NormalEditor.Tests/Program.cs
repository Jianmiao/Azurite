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
Test("original read cannot start before actual InitTask yield", () => { var f = New(); f.Ledger.BindAwake(f.Observe(), false, false); Deny(() => f.Ledger.ReadEntered(f.Observe(), 300, 0)); });
Test("resource entry inside bound continuation may precede readiness", () => { var f = New(); f.ReadToContinuation(); f.Ledger.ResourceInitializationObserved(f.Observe(), f.Continuation); Check(!f.Ledger.ReadyObserved && f.Ledger.ResourceStartObservations == 1, "early callback was relabelled ready"); f.FinishStartup(); });
Test("SaveTask capture may first observe readiness with its initial wait nested", () => { var f = New(); f.ReadToContinuation(); f.Ready = true; f.BindSaver(); Check(f.Ledger.ReadyObserved && !f.Ledger.InitializationCompleted && f.Ledger.CompletedSaveResumes == 0, "nested wait completed initialization or counted a resume"); f.CompleteInitialization(); Check(f.Ledger.InitializationCompleted, "original InitTask return was not observed"); });
Test("false-ready resource entry before continuation is rejected", () => { var f = New(); f.ReadOnly(); Deny(() => f.Ledger.ResourceInitializationObserved(f.Observe())); });
Test("later direct resource entry with true readiness is valid", () => { var f = New(); f.ReadToContinuation(); f.FinishStartup(); f.Ledger.ResourceInitializationObserved(f.Observe()); Check(f.Ledger.ResourceStartObservations == 1 && f.Ledger.InitializationCompleted, "later resource entry refused"); });
Test("continuation needs successful original read", () => { var f = New(); f.BindInit(); Deny(() => f.Ledger.InitMoveNextEntered(f.Observe(), 250, 1)); });
Test("read fallback yield is rejected", () => { var f = New(); f.BeginRead(); f.Data = 200; Deny(() => f.Ledger.OriginalReadReturned(f.Observe(), 300, true, f.Binding.SeedMarker)); });
Test("read result seed marker mismatch is rejected", () => { var f = New(); f.BeginRead(); f.Data = 200; Deny(() => f.Ledger.OriginalReadReturned(f.Observe(), 300, false, "other")); });
Test("read iterator mismatch is rejected", () => { var f = New(); f.BeginRead(); f.Data = 200; Deny(() => f.Ledger.OriginalReadReturned(f.Observe(), 301, false, f.Binding.SeedMarker)); });
Test("two dirty natural cycles complete observed policy lineage", () => { var f = New(); f.Start(); f.Save("1"); f.Save("2"); Check(f.Ledger.CompletedWrites == 2 && f.Ledger.CompletedSaveResumes == 2 && f.Ledger.RepeatedSaveLineageObserved, "valid repeated writes did not complete"); });
Test("four saves preserve three-backup rotation", () => { var f = New(); f.Start(); var hashes = new List<string> { Fixture.Hash(f.Paths.Settings) }; for (int i = 1; i <= 4; i++) { f.Save(i.ToString()); hashes.Add(Fixture.Hash(f.Paths.Settings)); } Check(Fixture.Hash(f.Paths.Settings + ".bk.1") == hashes[3] && Fixture.Hash(f.Paths.Settings + ".bk.3") == hashes[1], "backup rotation differs"); Check(f.Ledger.CompletedWrites == 4, "later write refused"); });
Test("identical serialized content can still be a genuine save", () => { var f = New(); f.Start(); f.Save("same"); f.Save("same"); Check(f.Ledger.RepeatedSaveLineageObserved, "same-content original returns were rejected"); });
Test("clean state-one resume counts no write", () => { var f = New(); f.Start(); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, false); f.Ledger.SaveMoveNextReturned(f.Observe(), t, 400, true, 1); Check(f.Ledger.CompletedSaveResumes == 1 && f.Ledger.CompletedWrites == 0 && !f.Ledger.RepeatedSaveLineageObserved, "clean cycle counted as a save"); });
Test("initial state-zero delay cannot write", () => { var f = New(); f.ReadToContinuation(); f.Ready = true; f.Ledger.SaveTaskCreated(f.Observe(), f.Continuation, 400, 0); var t = f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 0, true); Deny(() => f.Ledger.AdmitWrite(f.Observe(), t)); });
Test("state-one before initial delay is rejected", () => { var f = New(); f.ReadToContinuation(); f.Ready = true; f.Ledger.SaveTaskCreated(f.Observe(), f.Continuation, 400, 0); f.CompleteInitialization(); Deny(() => f.Ledger.SaveMoveNextEntered(f.Observe(), 400, 1, true)); });
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
Test("physical seed marker must match its bound marker", () => { var f = New(); var ledger = new NormalSettingsLifecycle(f.Binding with { SeedMarker = "wrong-marker" }, f.Lease); f.BindReadChild(ledger); Deny(() => ledger.ReadEntered(f.Observe(),300,0)); });
Test("original read return cannot skip its native prefix", () => { var f = New(); f.Ledger.BindAwake(f.Observe(),false,false); f.Data=200; Deny(() => f.Ledger.OriginalReadReturned(f.Observe(),300,false,f.Binding.SeedMarker)); });
Test("wrong SaveTask iterator is rejected", () => { var f = New(); f.Start(); Deny(() => f.Ledger.SaveMoveNextEntered(f.Observe(),401,1,true)); });
Test("nested admission callback refusal cannot be swallowed", () => { var f = New(); f.Start(); var t=f.Ledger.SaveMoveNextEntered(f.Observe(),400,1,true); f.Lease.OnAssert=step => { if(step==NormalStep.WriteEnter) { try { f.Ledger.ReadinessObserved(f.Observe()); } catch(InvalidOperationException) { } } }; Deny(() => f.Ledger.AdmitWrite(f.Observe(),t)); Check(f.Ledger.CompletedWrites==0 && f.Ledger.Failure is not null,"nested refusal returned a write permit"); });
Test("snapshot size bound rejects oversized settings before native read", () => { var f=New(); File.WriteAllText(f.Paths.Settings,new string('x',(int)SettingsSnapshotReader.MaximumFileBytes+1)); f.BindReadChild(); Deny(() => f.Ledger.ReadEntered(f.Observe(),300,0)); });
Test("fresh write validation rejects reparse backup", () => { var f=New();f.Start();f.Save("1");var target=Path.Combine(f.Root,"junction-target");Directory.CreateDirectory(target);var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"/d","/c","mklink","/J",f.Paths.Settings+".bk.2",target})start.ArgumentList.Add(arg);using var child=System.Diagnostics.Process.Start(start)!;if(!child.WaitForExit(5000)){child.Kill(true);throw new Exception("fixture junction timed out");}Check(child.ExitCode==0,"fixture junction failed");var t=f.Ledger.SaveMoveNextEntered(f.Observe(),400,1,true);Deny(() => f.Ledger.AdmitWrite(f.Observe(),t)); });
Test("post-write drift cannot be committed at SaveTask return", () => { var f=New();f.Start();var t=f.Ledger.SaveMoveNextEntered(f.Observe(),400,1,true);var w=f.Ledger.AdmitWrite(f.Observe(),t);f.SimulateSaveResult("1");f.Ledger.OriginalWriteReturned(f.Observe(),w);File.WriteAllText(f.Paths.Settings+".bk.1","changed-after-write-return");Deny(() => f.Ledger.SaveMoveNextReturned(f.Observe(),t,400,true,1));Check(f.Ledger.CompletedWrites==0 && f.Ledger.CompletedSaveResumes==0,"stale post-write snapshot committed"); });

Test("InitTask capture requires scoped bound Awake", () => { var f=New(); Deny(() => f.Ledger.InitTaskCreated(f.Observe(),250,0)); });
Test("InitTask must be captured once in native state zero", () => { var f=New(); f.Ledger.BindAwake(f.Observe(),false,false); Deny(() => f.Ledger.InitTaskCreated(f.Observe(),250,1)); });
Test("duplicate InitTask capture is rejected", () => { var f=New(); f.BindInit(); Deny(() => f.Ledger.InitTaskCreated(f.Observe(),250,0)); });
Test("uncaptured InitTask cannot enter", () => { var f=New(); f.Ledger.BindAwake(f.Observe(),false,false); Deny(() => f.Ledger.InitMoveNextEntered(f.Observe(),250,0)); });
Test("foreign InitTask pointer cannot enter", () => { var f=New(); f.BindInit(); Deny(() => f.Ledger.InitMoveNextEntered(f.Observe(),251,0)); });
Test("reentrant InitTask entry is rejected", () => { var f=New(); f.BindInit(); f.Ledger.InitMoveNextEntered(f.Observe(),250,0); Deny(() => f.Ledger.InitMoveNextEntered(f.Observe(),250,0)); });
Test("state-zero InitTask must yield", () => { var f=New(); f.BindInit(); var t=f.Ledger.InitMoveNextEntered(f.Observe(),250,0); Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),t,250,false,-1,300)); });
Test("state-zero InitTask must enter state one after yield", () => { var f=New(); f.BindInit(); var t=f.Ledger.InitMoveNextEntered(f.Observe(),250,0); Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),t,250,true,0,300)); });
Test("state-zero return must provide a real read child identity", () => { var f=New(); f.BindInit(); var t=f.Ledger.InitMoveNextEntered(f.Observe(),250,0); Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),t,250,true,1)); });
Test("InitTask cannot yield itself as its read child", () => { var f=New(); f.BindInit(); var t=f.Ledger.InitMoveNextEntered(f.Observe(),250,0); Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),t,250,true,1,250)); });
Test("original read must be the yielded child", () => { var f=New(); f.BindReadChild(); Deny(() => f.Ledger.ReadEntered(f.Observe(),301,0)); });
Test("read cannot run inside state-zero InitTask before its return", () => { var f=New(); f.BindInit(); f.Ledger.InitMoveNextEntered(f.Observe(),250,0); Deny(() => f.Ledger.ReadEntered(f.Observe(),300,0)); });
Test("duplicate state-zero InitTask return is rejected", () => { var f=New(); f.BindReadChild(); Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),f.ReadInvocation,250,true,1,300)); });
Test("state zero cannot repeat after the yielded child", () => { var f=New(); f.BindReadChild(); Deny(() => f.Ledger.InitMoveNextEntered(f.Observe(),250,0)); });
Test("state-one continuation cannot begin while already ready", () => { var f=New(); f.ReadOnly(); f.Ready=true; Deny(() => f.Ledger.InitMoveNextEntered(f.Observe(),250,1)); });
Test("false-ready resource entry needs its actual continuation token", () => { var f=New(); f.ReadToContinuation(); Deny(() => f.Ledger.ResourceInitializationObserved(f.Observe())); });
Test("foreign continuation token cannot authenticate resource entry", () => { var a=New(); var b=New(); a.ReadToContinuation(); b.ReadToContinuation(); Deny(() => b.Ledger.ResourceInitializationObserved(b.Observe(),a.Continuation)); });
Test("resource entry is single even across readiness", () => { var f=New(); f.Start(); Deny(() => f.Ledger.ResourceInitializationObserved(f.Observe())); });
Test("false readiness after a true observation is rejected during continuation", () => { var f=New(); f.ReadToContinuation(); f.Ready=true; f.Ledger.ReadinessObserved(f.Observe(),f.Continuation); f.Ready=false; Deny(() => f.Ledger.ResourceInitializationObserved(f.Observe(),f.Continuation)); });
Test("ready observation inside continuation requires its token", () => { var f=New(); f.ReadToContinuation(); f.Ready=true; Deny(() => f.Ledger.ReadinessObserved(f.Observe())); });
Test("ready resource entry within bound continuation is representable", () => { var f=New(); f.ReadToContinuation(); f.Ready=true; f.Ledger.ResourceInitializationObserved(f.Observe(),f.Continuation); f.BindSaver(); f.CompleteInitialization(); Check(f.Ledger.ResourceStartObservations==1,"actual resource entry was lost"); });
Test("startup with no resource callback invents no resource or event observation", () => { var f=New(); f.ReadToContinuation(); f.FinishStartup(); Check(f.Ledger.InitializationCompleted && f.Ledger.ResourceStartObservations==0,"unobserved resource entry was manufactured"); });
Test("saver capture requires active state-one continuation", () => { var f=New(); f.ReadOnly(); f.Ready=true; Deny(() => f.Ledger.SaveTaskCreated(f.Observe(),f.ReadInvocation,400,0)); });
Test("saver capture requires actual true readiness", () => { var f=New(); f.ReadToContinuation(); Deny(() => f.Ledger.SaveTaskCreated(f.Observe(),f.Continuation,400,0)); });
Test("saver capture requires initial state zero", () => { var f=New(); f.ReadToContinuation(); f.Ready=true; Deny(() => f.Ledger.SaveTaskCreated(f.Observe(),f.Continuation,400,1)); });
Test("saver cannot alias InitTask or the read child", () => { foreach(var pointer in new long[]{250,300}) { var f=New(); f.ReadToContinuation(); f.Ready=true; Deny(() => f.Ledger.SaveTaskCreated(f.Observe(),f.Continuation,pointer,0)); } });
Test("foreign continuation token cannot capture saver", () => { var a=New();var b=New();a.ReadToContinuation();b.ReadToContinuation();b.Ready=true;Deny(() => b.Ledger.SaveTaskCreated(b.Observe(),a.Continuation,400,0)); });
Test("saver is captured only once", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.SaveTaskCreated(f.Observe(),f.Continuation,400,0)); });
Test("initial saver wait may occur after InitTask original return", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.Ledger.SaveTaskCreated(f.Observe(),f.Continuation,400,0);f.CompleteInitialization();f.InitialWait();f.Save("1");Check(f.Ledger.CompletedWrites==1,"asynchronous initial wait was refused"); });
Test("later state-one saver resume requires completed InitTask", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.SaveMoveNextEntered(f.Observe(),400,1,true));Check(!f.Ledger.InitializationCompleted && !f.Ledger.RepeatedSaveLineageObserved && f.Ledger.CompletedWrites==0,"missing InitTask return produced progress"); });
Test("missing InitTask return cannot close lifecycle", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.Close(f.Observe()));Check(!f.Ledger.InitializationCompleted,"close invented completion"); });
Test("original InitTask exception cannot be turned into normal completion", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();f.Ledger.Fail("original InitTask exception observed");Deny(f.CompleteInitialization);Check(!f.Ledger.InitializationCompleted && f.Ledger.CompletedWrites==0,"exceptional continuation completed"); });
Test("InitTask completion requires naturally captured saver", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.Ledger.ReadinessObserved(f.Observe(),f.Continuation);Deny(f.CompleteInitialization);Check(!f.Ledger.InitializationCompleted,"missing saver accepted"); });
Test("continuation return must be false", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),f.Continuation,250,true,-1));Check(!f.Ledger.InitializationCompleted,"yielding continuation completed"); });
Test("continuation return must have terminal state minus one", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),f.Continuation,250,false,1));Check(!f.Ledger.InitializationCompleted,"nonterminal continuation completed"); });
Test("continuation completion cannot claim another yielded child", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),f.Continuation,250,false,-1,300)); });
Test("foreign iterator cannot return bound continuation", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),f.Continuation,251,false,-1)); });
Test("foreign continuation token cannot complete startup", () => { var a=New();var b=New();a.ReadToContinuation();b.ReadToContinuation();b.Ready=true;b.BindSaver();Deny(() => b.Ledger.InitMoveNextReturned(b.Observe(),a.Continuation,250,false,-1));Check(!b.Ledger.InitializationCompleted,"foreign invocation completed startup"); });
Test("stale initial invocation cannot return continuation", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.InitMoveNextReturned(f.Observe(),f.ReadInvocation,250,false,-1)); });
Test("duplicate continuation return latches failure", () => { var f=New();f.Start();Deny(f.CompleteInitialization);Check(f.Ledger.Failure is not null && !f.Ledger.RepeatedSaveLineageObserved,"duplicate completion did not latch refusal"); });
Test("readiness regression at continuation return cannot complete startup", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();f.Ready=false;Deny(f.CompleteInitialization);Check(!f.Ledger.InitializationCompleted,"regressed continuation completed"); });
Test("InitTask cannot return while nested saver frame remains open", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.Ledger.SaveTaskCreated(f.Observe(),f.Continuation,400,0);f.Ledger.SaveMoveNextEntered(f.Observe(),400,0,false);Deny(f.CompleteInitialization); });
Test("retired continuation token cannot authenticate later readiness", () => { var f=New();f.Start();Deny(() => f.Ledger.ReadinessObserved(f.Observe(),f.Continuation)); });
Test("admission revocation prevents InitTask completion", () => { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();f.Lease.Revoked=true;Deny(f.CompleteInitialization);Check(!f.Ledger.InitializationCompleted,"revoked admission completed startup"); });
Test("invalid identity at continuation return never completes", () =>
{
    foreach(var alter in new Func<SettingsObservation,SettingsObservation>[] {
        o=>o with {Owner=101}, o=>o with {Data=201}, o=>o with {RunId=Guid.NewGuid().ToString("D")},
        o=>o with {Generation=2}, o=>o with {ProcessId=Environment.ProcessId+1}, o=>o with {NativeThreadId=99}, o=>o with {Sequence=1}})
    { var f=New();f.ReadToContinuation();f.Ready=true;f.BindSaver();Deny(() => f.Ledger.InitMoveNextReturned(alter(f.Observe()),f.Continuation,250,false,-1));Check(!f.Ledger.InitializationCompleted,"invalid identity completed startup"); }
});

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
    public InitInvocation ReadInvocation = null!;
    public InitInvocation Continuation = null!;
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
    public void BindInit() { Ledger.BindAwake(Observe(),false,false); Ledger.InitTaskCreated(Observe(),250,0); }
    public void BindReadChild(NormalSettingsLifecycle? ledger = null)
    {
        ledger ??= Ledger;
        ledger.BindAwake(Observe(),false,false); ledger.InitTaskCreated(Observe(),250,0);
        ReadInvocation=ledger.InitMoveNextEntered(Observe(),250,0);
        ledger.InitMoveNextReturned(Observe(),ReadInvocation,250,true,1,300);
    }
    public void BeginRead() { BindReadChild(); Ledger.ReadEntered(Observe(),300,0); }
    public void ReadOnly() { BeginRead(); Data=200; Ledger.OriginalReadReturned(Observe(),300,false,Binding.SeedMarker); }
    public void ReadToContinuation() { ReadOnly(); Continuation=Ledger.InitMoveNextEntered(Observe(),250,1); }
    public void CompleteInitialization() => Ledger.InitMoveNextReturned(Observe(),Continuation,250,false,-1);
    public void FinishStartup() { Ready=true; BindSaver(); CompleteInitialization(); }
    public void Start() { ReadToContinuation(); Ledger.ResourceInitializationObserved(Observe(),Continuation); FinishStartup(); }
    public void BindSaver() { Ledger.SaveTaskCreated(Observe(),Continuation,400,0); InitialWait(); }
    public void InitialWait() { var t=Ledger.SaveMoveNextEntered(Observe(),400,0,false); Ledger.SaveMoveNextReturned(Observe(),t,400,true,1); }
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

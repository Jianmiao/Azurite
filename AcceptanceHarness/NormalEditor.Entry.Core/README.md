# Catalog readiness observer — preparation only

This component implements item 2 of the 2026-11-13 normal Editor checklist. It records an actual `Catalog.Start` completion followed by a later, naturally completed `AuthoringWorkbench.Update` and an unchanged native idle readback. It is uninstalled preparation. It does not grant startup permission or establish normal Editor acceptance.

## Components and API

- `NormalEditor.Entry.Core` contains `CatalogReadiness`, the immutable observation/evidence records, `CatalogTickCallbacks`, and `CatalogClockAnchor`. These are the same managed components used by the thin host and exercised by behavioral tests. Core has no Unity, native, filesystem or policy dependency.
- `NormalEditor.Entry.Host` binds the pinned IL2CPP/Harmony seams and forwards actual callback parameters. It references the existing frozen adapter and policy DLLs. It has no plugin attribute, automatic loader, permitting admission implementation, process launcher or project opener.
- `NormalEditor.Entry.Tests` runs the real coordinators with controlled observations, then reads the compiled Host and copied interop as PE/IL data through the pinned Cecil reader. It never CLR-loads the Host or target interop assemblies.

The future explicit integration point is:

```csharp
CatalogEntryHost.Register(
    AdapterBinding binding,
    IAdapterAdmission admission,
    string entryHostSha256,
    string entryCoreSha256,
    Action<CatalogEvidence> record,
    CatalogClockAnchor clock,
    long timeoutMilliseconds = 120000);
```

The caller captures one clock before constructing the journal identity and registering the observer:

```csharp
var clock = CatalogClockAnchor.Capture();
var identity = new CatalogRunIdentity(catalogBinding, actualProcessStartUtc,
    clock.ObserverStartedUtc);
// Create the existing evidence writer with this identity, then pass the same
// clock object and writer.Record to CatalogEntryHost.Register.
```

`Capture` samples `Stopwatch.GetTimestamp()` and UTC together in one call. The immutable anchor exposes that timestamp, the counter frequency, and the exact UTC value to use as `ObserverStartedUtc`; the host never creates another stopwatch or estimates the origin. The pair is sampled sequentially, not an atomic operating-system correlation, and does not prove the accuracy of the system wall clock. The explicit constructor also accepts an already collected UTC/timestamp/frequency pair. Such values are caller-supplied data, not physical provenance or admission evidence. Non-UTC/missing origins, negative counters, nonpositive or changed frequencies, samples before the origin and unrepresentable elapsed milliseconds are rejected. The host validates the source frequency and current counter before binding native classes. Its readiness deadline starts at the supplied anchor, so time spent constructing the writer and registering hooks remains included. A clock failure is recorded with the last valid elapsed value without sampling the failing clock again.

There were no C# callers of the previous preparation-only signature. The clock is required; no overload starts a private replacement clock. The existing admission object remains an external requirement. Its installation evidence identity must remain unchanged on registration and callbacks. The entry Host/Core hashes must be supplied externally. Registration is single-use and runs on the admitted native main thread; the allowed readiness deadline is 1–300000 milliseconds. This library neither implements resource/cache/discovery/NoSound admission nor replaces those gates with observed ready flags.

## Observed lifecycle

`Catalog.Start` prefix issues an immutable token. The postfix must supply that same token and `__runOriginal=true`. Only its matching finalizer without an original exception records `catalog-start-complete`. Duplicate, missing, foreign, late or out-of-order callbacks fail the run. Original exceptions are returned unchanged by the managed finalizers.

`AuthoringWorkbench.Update` has its own tested token tracker. Each prefix, original return and finalizer must agree on run, process, managed/native thread, owner and native frame. A following Update cannot bypass an outstanding finalizer; a duplicate postfix cannot silently count as a second return. Its successful finalizer permits an observation but cannot itself declare readiness.

The Host calls the tested deferred `ObserveTick` bridge only after the actual Update finalizer is consumed. Before invoking the first field-read delegate, the bridge requires the bound run/process/thread, an unexpired deadline, actual Start finalization, a strictly later native frame, and no prior ready/failure result. Early, same-frame and terminal ticks do not sample backing fields. The read must stay within that native frame. Only then does the readiness coordinator require the actual `CatalogScene`, nonzero stable Catalog/workbench/settings/resource owners, settings/resources ready, no preload or loading, CLI inactive, no CLI transport directory, no current session, and `controlOpening=false`. Once all predicates hold, it calls the supplied deferred readback. The Host implements that readback with exactly one call to the existing `RequireCatalogIdleForControl()` followed by a second observation. Both observations must agree in every identity/state field and frame; monotonic elapsed time may advance within the deadline.

Successful readiness emits exactly one `catalog-ready` record. Subsequent healthy Update callbacks do not restart the readiness deadline. A native error, identity/order mismatch, guard failure, evidence-writer failure or reentrant observation latches failure without retry. `Reject` keeps the first reason. Ready is set only after the writer returns successfully with no latched failure.

## Native bindings and reads

| Actual wrapper | Native token | Native entry RVA | Generated pointer field |
|---|---:|---:|---|
| `Catalog.Start()` instance void | 100665977 | `0x9b5200` | `NativeMethodInfoPtr_Start_Private_Void_0` |
| `AuthoringWorkbench.Update()` instance void | 100695327 | `0x952bd0` | `NativeMethodInfoPtr_Update_Private_Void_0` |
| `AuthoringWorkbench.RequireCatalogIdleForControl()` static void | 100695332 | `0x952240` | `NativeMethodInfoPtr_RequireCatalogIdleForControl_Private_Static_Void_0` |

Runtime binding checks the loaded process/module/assembly locations, pinned files, native class, token, MethodInfo pointer and native entry RVA. During registration, `CatalogNativeBindings.Bind` explicitly calls `RuntimeHelpers.RunClassConstructor` for `Catalog` and `AuthoringWorkbench` from its method table. Those generated wrapper initializers can call `il2cpp_runtime_class_init`; registration therefore has a native class-initialization boundary and is not a pure observation. Its actual native effects require later qualification under the existing admission gates. Only Start and Update receive observer patches. Their prefixes return void, never suppressing the original. The idle guard must remain unpatched. Callback checks revalidate admission evidence and exact patch ownership/content.

State observations use the generated backing-field accessors: singleton backing owners, settings `_isReady`, resource `_AreDbsLoaded`/`_Preloading`, CLI `_IsActive`, session `_Current`, workbench `controlOpening`, and `Loading.loadingCount`. Cecil confirms these are field accessors rather than native convenience-method calls. The Host also reads the real Unity frame and active-scene name. It does not call `Singleton.Instance`, session `Current`, readiness convenience getters, native factories, Start/Begin/coroutines, OpenProject/OpenControlled, or state setters. These reads are not claimed to be side-effect-free: first access can run generated type initializers, including `il2cpp_runtime_class_init`. Binding also explicitly initializes its wrapper types to resolve native metadata. Whether all observed types were naturally initialized, and whether first access has native effects, remains a separate runtime qualification gate. Deferring field sampling until after authentic Start completion prevents premature sampling; it does not establish natural initialization of every type.

The copied compile inputs are exactly the six files recorded in `artifacts/normal-editor-entry-20261113/compile-inputs.json`. Only those explicitly authorized F inputs were read for copying; before/after source and C destination hashes matched. Subsequent builds and metadata checks use only their C copies. The two frozen policy DLLs, existing adapter DLLs, isolation implementation, production plugin and RenderControlV1 were not edited or rebuilt by this task.

## Evidence contract and limits

`CatalogEvidence` records a monotonically increasing sequence, kind, bound run ID, actual callback/observation stamp, optional complete observation and optional failure. The stamp includes PID, managed/native thread, native frame, elapsed milliseconds and Catalog owner. Event kinds are `catalog-start-enter`, `catalog-start-return`, `catalog-start-complete`, `catalog-ready` and `catalog-failed`.

The record callback must persist evidence and return synchronously. It must not invoke lifecycle methods, drive the observer or use a ready event to open a project. Reentry is rejected. A writer may append a ready record and then throw or reject the run; a later `catalog-failed` invalidates that run. If the writer itself is unavailable, even the failure record can be absent. An isolated ready line is therefore not a durable terminal acceptance result. Independent writer/consumer/close evidence remains a later integration requirement.

Timeouts are checked on actual callbacks and ticks. If the main thread stops or no callback occurs, this component cannot wake itself or terminate the process. A separately admitted external watchdog and an actual normal-shutdown caller remain outstanding item-1 work. Partial registration failure latches failure and throws; it does not restore or retry partially installed observer patches. Such a process must not be reused as a qualified run. There is no automatic shutdown or cleanup.

## Explicit managed producer retirement

`CatalogEntryHost.Close()` is the normal-shutdown integration point. Registration must have used the **same binding object and admission object** as `NativeAdapterHost.Register`. Close runs on the registered managed/native owner thread. It checks complete registration, current admission/patch ownership, healthy Catalog-ready state, no active managed callback, no unfinished Start/Update return/finalizer, and no field-read, idle-guard or evidence callback. A foreign actual managed-thread request returns false before acquiring the evidence/close locks or invoking delegates. Same-thread reentry and malformed context latch failure. Readiness timeout does not become a shutdown deadline.

Foreign `TryEnter` and callback retirement do not call the evidence sink or take owner locks. They atomically retain the first refusal; the next owner registration/callback/retirement/Close boundary records it and refuses success. Closing and foreign refusal share a single compare/exchange decision: a refusal that wins before the successful close commit prevents true, including a refusal between the last health check and commit. A successful commit wins only from the no-refusal state; afterward both late entry and retirement hooks are inert. `ProducersStopped` during adapter Close is provisional and is never itself Seal permission. The previous synchronous foreign refusal implementation had an owner-evidence-lock wait defect; its original results and the peer counterexample are retained, not relabeled as passing.

The Host uses the tested `CatalogShutdown` around each complete managed hook invocation, beginning before external admission verification and ending after finalization/field observation/evidence callbacks. Ready alone never stops it. Explicit successful Close first makes these Catalog hook entry points inert, then calls `NativeAdapterHost.Close(binding, admission)`. That adapter API checks its exact registered objects, full registration, thread and patch ownership, and forwards to the existing `AdapterCoordinator.Close` with the original frozen settings policy. It does not copy or replace that lifecycle.

Only a true `CatalogEntryHost.Close()` result permits the external integration to call its writer's `Seal()` afterward, followed by Dispose. The API does not hold a writer or invoke Seal itself. Verification/adapter/evidence failures return false and do not advertise Closed; a failed adapter close leaves the Catalog producer stopped but failed, without retry or a valid terminal. Repeated Close returns false. Once stopped, later Catalog hooks perform no AA field observation or journal emission and preserve execution of the original methods and their original exceptions. Patches remain installed until process exit; no Unpatch or Quit is invoked.

This proves retirement of this managed evidence producer only. It is not a claim that native coroutines, pending tasks or trampolines are drained, that the application has exited, or that settings/resources/session work elsewhere has finished. An outer integration must choose the genuine final owner-thread shutdown step, preserve failed evidence, and supply the existing independent normal-close/exit proof. Neither the Close API nor a sealed file grants admission or replaces that external proof. No Ready callback may call Close/Seal or open a project.

The separate `NormalEditor.Entry.Shutdown.Tests` project exercises this production Core boundary with the actual `AdapterCoordinator.Close` and unchanged frozen policies behind explicitly labeled native/admission fixtures. Its metadata mode reads the compiled Host assemblies as PE/IL only; it does not load or execute Host/native code. Test inputs and outputs must be explicit C-only paths. Earlier compiler/setup failures and RED controls are retained in `artifacts/normal-editor-shutdown-20261114`.

The shared-clock revision has **125 passing checks: 49 readiness coordinator, 20 Update callback coordinator, 14 deferred observation bridge, 18 clock-anchor checks, and 24 metadata checks**. It retains the original 103 checks and adds UTC/monotonic origin sharing, elapsed conversion and overflow boundaries, registration delay, erroneous clocks, required host wiring, validation before native binding, and failure reporting without rereading the clock. The original suite covers real success/evidence, same/later frame, pre-Start updates, run/process/thread/owner identity, repetition/order/missing callbacks, original suppression/exceptions, all readiness predicates, guard readback drift/error/reentry, evidence failure/reentry, clock reversal and deadlines. The bridge tests run the same coordinator used by the Host and prove that early/terminal/foreign ticks do not invoke the field delegate. Metadata checks establish signatures, pointer/token initialization, backing accessor shape, callback forwarding and absence of prohibited entry/writes.

Those results do not establish native patch hits, runtime argument conversion, native offsets/MethodInfo values, actual resource readiness, catalog-idle guard effects, scene timing, native exception propagation, sound/cache/discovery boundaries, process exit, project entry, session identity/snapshot correctness, native edits or performance. No Host registration, AA launch, plugin installation, project open, registry read or global lease acquisition was performed.

## Reproduce the C-only verification

Run in the current C worktree with the already restored .NET 6 reference pack and frozen dependency DLLs. This verification used SDK `10.0.203`.

```powershell
$entryRoot = (Get-Location).Path
$entryInputs = Join-Path $entryRoot 'artifacts/normal-editor-entry-20261113/build-input'
$entryHost = Join-Path $entryRoot 'AcceptanceHarness/NormalEditor.Entry.Host/bin/Release/net6.0/Azurite.NormalEditor.Entry.Host.PreparationOnly.dll'
dotnet build AcceptanceHarness/NormalEditor.Entry.Host/NormalEditor.Entry.Host.csproj -c Release --no-restore "-p:AAInstallPath=$entryInputs"
dotnet run --project AcceptanceHarness/NormalEditor.Entry.Tests/NormalEditor.Entry.Tests.csproj -c Release --no-restore --no-launch-profile -- $entryInputs $entryHost
```

The bounded review package is `artifacts/normal-editor-entry-20261113/review-package.json`; it binds source/output snapshots, frozen inputs, compiler/test logs and parsed final results. Earlier red controls and the overbroad initial metadata test failure are preserved alongside final logs. The metadata correction permits ordinary managed `System.Collections` enumerator `MoveNext`; it continues to prohibit native coroutine/lifecycle driving.

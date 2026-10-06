# Production progressive-loading integration harness

Run:

```powershell
dotnet run --project .\LoadingIntegrationTests\LoadingIntegrationTests.csproj -c Release -- 'F:\AzureArchive_100_fix\BepInEx\interop\Assembly-CSharp.dll'
```

This executable compiles the production `ProgressiveEditorLoading` controller and immutable resource-request type. It exercises the controller's registered Harmony callbacks, not a second implementation of its state machine. Before accepting a hook, it compares its target name, exact parameter types, return type, and static/instance status against the supplied host interop assembly using read-only metadata inspection. Workbench/project properties and the native resource-factory signatures are checked against that same assembly.

The regression starts at `AuthoringWorkbench._Load_d__47.MoveNext`, the editor-open path established by the native disassembly in `diagnostics/Azurite-1.0.2/native-loading-evidence-20261006.md`. It asserts that the original resource enumerator is detached without being advanced, the editor's `Start` callback unlocks structure editing while resources remain pending, and preview requests wait until completion. It also exercises failure and cancellation cleanup. A separate assertion disallows accidentally intercepting `CoLoadSave`, which is a different native loading path.

The host objects, Harmony dispatch, and coroutine scheduler are simulated. No AA process is started, no native detour is installed, and no Unity resource is decoded. Passing these tests establishes controller behavior and host-signature compatibility; it does **not** measure user-visible load speed or prove the complete native editor lifecycle. Those claims require an AA runtime trace.

The red baseline against the preceding production implementation failed all ten cases during `Install` because it requested `StudioCommon.Load()`; the current host exposes `StudioCommon.Load(ProjectData)`. This is the exact installation failure in the user's latest log, which the older gate-only tests could not detect.

The native-resource-queue repair now has **24 passing cases**, with **393 exact method/property comparisons** against the installed host. A simulated native task engine advances unchanged resource enumerators once per engine frame. An engine-owned resource rejects `MoveNext` calls outside that scheduler, so the previous `PaceResource -> managed wrapper -> NativeAdapter.MoveNext` route fails explicitly. Tests cover one admission per frame, at most four slow native tasks, instant cached completions, all six resource kinds, resource-key deduplication, immutable metadata after live edits, exact-once native result consumption, failure, cancellation, export admission pause, and deferred preview recovery.

The exact same suite was run against the archived pre-fix production controller using the optional source override:

```powershell
dotnet run --project .\LoadingIntegrationTests\LoadingIntegrationTests.csproj -c Release -p:ProgressiveControllerSource=E:\aamod\diagnostics\Azurite-1.0.3-entryfix\ProgressiveEditorLoading-red-baseline.cs -- 'F:\AzureArchive_100_fix\BepInEx\interop\Assembly-CSharp.dll'
```

That baseline returns **10/24**, including failure of the native execution-ownership assertion. No native access violation is deliberately reproduced in the test process: the unsafe boundary is represented by a bounded exception. The actual user failure is documented in `diagnostics/Azurite-1.0.3-entryfix/hang-evidence.md`; a real AA replay reaching `resources-ready` without the fatal native trace is still required before claiming a verified end-user fix.

# Editor isolation acceptance harness — LAB ONLY

This is a source/build increment for a **bounded current-account route selftest**, not a completed normal Editor isolation contract. It is not referenced by the shipping Plugin/Core projects, existing CLI runner, packages or contracts. No AA/Unity launch or installation is performed by these projects or tests.

## Build and offline checks

From the repository root, using the existing installed metadata and dependencies:

```powershell
dotnet run --project AcceptanceHarness\EditorIsolation.Tests -- artifacts\editor-isolation-probe
dotnet run --project AcceptanceHarness\EditorIsolation.HostBindingTests -p:AAInstallPath=F:\AzureArchive_101 -- F:\AzureArchive_101
dotnet build AcceptanceHarness\EditorIsolation.Guard -c Release -p:AAInstallPath=F:\AzureArchive_101
dotnet build AcceptanceHarness\EditorIsolation.Probe -c Release -p:AAInstallPath=F:\AzureArchive_101
```

Policy tests operate only on fresh workspace-owned fixtures, including junction/hardlink rejection. One harmless `dotnet` child demonstrates that the actual own-process `TerminateProcess(GetCurrentProcess(), 86)` helper prevents a following write even when its caller catches exceptions. This is not an AA runtime test. The host-binding program reads PE metadata with the already-installed Mono.Cecil; it never executes native host code.

## Separate lab and deployment contract for the future controller

The runtime admission pins only `E:\aamod\labs\Azurite-20261108-editor`, with `install` below it and fresh `runs\<GUID-D>` directories. The stable CLI lab and daily installation are not accepted runtime targets. Changing this root requires source review/rebuild. Do not install or launch from this README without the parent's reviewed controller, copied-host pins, current-account snapshots, NoSound proof, process-job containment and window/output observations.

Future layout (one physical copy of the guardian and shared policy):

```text
install/BepInEx/patchers/AzuriteEditorIsolation/
  Azurite.EditorIsolation.Guard.LabOnly.dll
  Azurite.EditorIsolation.Policy.LabOnly.dll
install/BepInEx/plugins/AzuriteEditorIsolation/
  Azurite.EditorIsolation.Probe.LabOnly.dll
```

The probe locates the unique already-loaded guardian assembly at that exact path by reflection. Do not copy a second guardian or policy DLL into the plugins folder. The outer controller must pin all three DLLs, guard discovery, loader configuration and host dependencies; code that is never discovered cannot guard itself.

The child-only environment variable is `AZURITE_EDITOR_ISOLATION_MANIFEST=<run>\manifest.json`. No global environment setting is needed. A loaded guardian/probe inside the dedicated approved lab rejects an absent, empty or whitespace-only value by immediate own-process abort. The only inert exception is no requested manifest while both executable and assembly are outside the approved installation; this is explicitly unprotected and never counts as acceptance. For `route-selftest`, also use the real `AZUREARCHIVE_CLI_DIRECTORY=<run>\request` transport with an `arguments.json` whose `argv` is exactly `["capabilities"]`. The program requires `-batchmode`, `-nographics`, `-disableaudio` exactly once each. Flags alone do not prove NoSound; preserve the separately verified native NoSound configuration/contract. The existing restricted runner/contract is unchanged and does not automatically deploy or run this harness.

Manifest fields, with exact case:

```json
{
  "Version": 1,
  "RunId": "<fresh GUID in D format>",
  "Mode": "route-selftest",
  "LabRoot": "E:\\aamod\\labs\\Azurite-20261108-editor",
  "RunRoot": "E:\\aamod\\labs\\Azurite-20261108-editor\\runs\\<same GUID>",
  "InstallRoot": "E:\\aamod\\labs\\Azurite-20261108-editor\\install",
  "SeedSha256": "<SHA256 of settings/user_settings.json>",
  "ControlSha256": "<SHA256 of settings/miss_control.json>"
}
```

`ControlSha256` is the alternative native settings seed hash, not a controller-file hash. There is no manifest/control mutual hash. Guardian and probe independently hash the manifest file and bind the in-process handshake to it. The outer launch-control file can separately bind the manifest and artifacts after generation.

Pre-create `settings`, `workspace`, `cache`, `persistent`, `discovery`, `output`, `request` under the fresh run. Every admitted path must be canonical, existing, local-drive, beneath the run, without reparse ancestors. Settings/manifest files must have one hard link. The settings directory must initially contain only the two seeds. Each seed must have exactly all 26 current native properties; see the literal `Seed` fixture in `EditorIsolation.Tests/Program.cs`. Use marker `lab-read-<GUID>` in the main seed and `miss-control-<GUID>` in the alternative. Both use identical canonical lab `workspacePath`/`cachePath`, empty visited/favorite lists and custom/catalog source strings, all behavior booleans false, and **all four volumes = 1**. Internal PCM is not muted to obtain silence. Native settings save may create its existing same-directory temporary/backup files.

## Callable interception and evidence

The guardian's actual `BasePatcher.Finalizer` patches managed `BaseChainloader<BasePlugin>.Execute` completion before the native `Internal_ActiveSceneChanged` callback can resume. It requires the same manifest identity and all 12 route registrations. Its Harmony finalizer also aborts if `Execute` throws. Both guardian and normal plugin failure paths immediately terminate their own process; throwing is insufficient because the installed BepInEx loader catches and continues.

The probe installs exact native routes in `BasePlugin.Load`, verifies loaded module paths, twelve frozen host/core file hashes, generated fields and native entry RVAs, then signals registration to the guardian. `UserSettings.Awake` is the selftest trigger only after the guarded Execute completion has been observed. It requires actual CLI-active state and an untouched owner (null data, readiness false). It lets the native Awake run its existing CLI branch. No CLI/readiness flag is forced.

The actual native read test preloads only the owner's path with the complete alternative lab seed. It invokes the fixed **native entry pointer** for `ReadUserSettings.MoveNext`. A prefix hit must change the real native owner fields; the original method must deserialize the main seed marker. A native interception miss reads only the safe alternative seed and fails proof before saving. It then exercises **native `SaveTask.MoveNext -> native WriteUserSettings`**, requires the separate write guard hit, lets the original serialize/SafeSave, and verifies the saved marker and unchanged alternative seed. Fresh settings-directory admission runs at initial manifest validation, again **before seed-lock release and native SaveTask dispatch**, and again inside WritePrefix. Thus the write-miss control does not depend on its unproved hook to reject pre-existing ordinary, hardlinked or reparse backup entries. It never calls `Begin`, fabricates `Current`, assigns `PersistentData`, starts a resource coroutine, creates a listener or opens a project.

Additional resource-free direct native checks cover CachePath, WorkspacePath, DiscoveryDirectory and Addressables Set/Clear/EvaluateProperty. The pinned getter disassembly contains no application-level file write. **A CachePath interception miss can read daily settings**; its returned path is recorded and rejected before being passed to any file/cache/resource API. This is a no-daily-write boundary, not a no-daily-read claim. No Unity cache API is invoked. Addressables checks inspect the original native dictionary changes, original clear and restored property; they do not load a catalog. The native offline validator is not invoked in this first increment because its complete resource input is not yet established.

Evidence is append-only, per-run JSONL: `output/editor-isolation-guardian.jsonl` and `output/editor-isolation-evidence.jsonl`. Every event has exactly these top-level camelCase fields: `sequence` (per-file integer starting at 1), `utc` (UTC timestamp), `monotonicTicks` (Stopwatch timestamp), `runId` (GUID-D from the independently validated directory), `pid` (actual process ID), `manifestSha256` (SHA-256 of the exact raw manifest bytes), `kind`, and `data`. Values nested in `data` cannot override this binding. The consuming controller must reject top-level run/PID/hash disagreement.

Before parsing JSON, the guardian validates exactly `<approvedlab>/runs/<GUID-D>/manifest.json`, its existing non-reparse output directory and single-link manifest, reads/hashes bounded raw bytes, then opens a create-new correlated evidence stream. It never takes early output paths or identity from invalid JSON. A fixed malformed input such as raw UTF-8 `{` therefore produces `guardian-setup-failed` with `data.nativeStartupMayResume=false` and exits 86. A missing environment value has no trustworthy run identity, so it aborts without inventing a correlated guardian receipt; an exit code alone is not receipt proof.

Registration is explicitly not proof of interception. Completion requires native hit counts, owner/iterator identities, original result markers and saved-file evidence. Missing final completion is failure, even if capabilities itself succeeds. Reusing a mutated run is rejected by hashes, sidecars and create-new evidence files.

## Remaining gates — not completion claims

1. Independent source review and exact preloader/normal-plugin discovery/order proof; CLI-active negative runs with the probe missing, an invalid manifest and a missing route. Guardian absence itself is an outer-controller integrity/discovery gate.
2. Actual guarded route-selftest on the copied host, including proof of each native hit and original effects. No such run has been performed by this source task.
3. A complete matching offline resource set, catalog/settings pins, immutable resource evidence, and consistent offline-validator fourth argument/Addressables cache resolution before any resource work.
4. Extend the one-shot ledger into the reviewed normal repeated-save lifecycle; retain real OnReady-before-isReady order. Add resource/cache-write guards and a later stable native Catalog observer. `normalObserverCatalogOnly` is recognized but deliberately refused by this one-shot build; enabling normal mode is a subsequent implementation/review step, not a JSON approval bit.
5. Normal Catalog-only startup with current-account state snapshots, NoSound, no window/focus/input, bounded job cleanup and all path effects contained. Only after it passes: genuine `AuthoringWorkbench.OpenProject(copiedPath)` and the native scene/Studio lifecycle, followed by edits, Undo/Redo and save/reopen in separate gates.

These controls are not an OS filesystem sandbox against hostile same-account races. Keep the run private and unchanged, retain parent snapshots/restore/quarantine, and treat any extra side effect or missed native evidence as failure.

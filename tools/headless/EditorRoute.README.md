# Lab-only native route selftest controller

This controller is separate from the stable CLI runner. Its only runtime target is `E:\aamod\labs\Azurite-20261108-editor\install`. Its three scopes are `Positive`, `ProbeMissing`, and `InvalidManifest`. It does not implement a normal Editor or graphical acceptance run.

## Review and preparation

The root coordinator owns installation and admission. The controller never installs DLLs, changes the selected profile, rewrites loader configuration, removes a probe for a fault test, or adopts a current installation hash as an expected value.

1. Review the frozen controller sources and `EditorRouteReviewedInputs.candidate.json`. The candidate deliberately has pending admission and cannot authorize anything. After independent review, the coordinator writes the fixed `tools/headless/EditorRouteReviewedInputs.json`, retaining the reviewed hashes and setting its admission to `reviewed-cli-route-selftest-only`.
2. Install the reviewed fix1 guardian and policy in `BepInEx/patchers/AzuriteEditorIsolation/`, and the probe alone in `BepInEx/plugins/AzuriteEditorIsolation/`, according to `AcceptanceHarness/README.md`. Only the coordinator performs installation or deliberate missing-probe staging.
3. Explicitly prepare one scope's contract. Preparation compares the actual install with expected values from the frozen CLI startup contract, copied-host receipt, frozen harness receipt, native source/guardian review, ordering/audio report, and controller source pins. Existing contract files are never overwritten.
4. `PlanOnly` is read-only, including in-memory bounded shared-state examination. `PrepareOnly` creates a new private GUID run with seeds, manifest, exact capabilities sidecar, pinned tooling, compiled helper receipt and current-state snapshot, then stops. A prepared-only run cannot be resumed or reused by this controller.
5. Actual launch additionally requires the coordinator's explicit `lab.marker.json` Boolean `runtimeAllowed=true`. This Boolean alone cannot bypass any source, contract, artifact, identity, pin or quarantine check.

From the repository root:

```powershell
pwsh -NoProfile -File tools/headless/EditorRoutePrepareContract.ps1 -Scope Positive -PlanOnly
pwsh -NoProfile -File tools/headless/EditorRoutePrepareContract.ps1 -Scope Positive
pwsh -NoProfile -File tools/headless/Invoke-AzuriteEditorRouteSelfTest.ps1 -Scope Positive -PlanOnly
pwsh -NoProfile -File tools/headless/Invoke-AzuriteEditorRouteSelfTest.ps1 -Scope Positive -PrepareOnly
```

Only after separate runtime admission:

```powershell
pwsh -NoProfile -File tools/headless/Invoke-AzuriteEditorRouteSelfTest.ps1 -Scope Positive -TimeoutSeconds 120
```

For `ProbeMissing`, the coordinator first removes the probe from the copied installation through its explicit reviewed staging operation, then prepares that exact scope's contract. Its exact-directory contract refuses a remaining or duplicate probe. Both guardian and policy remain required. For `InvalidManifest`, install all three reviewed DLLs and explicitly stage the independently derived early-abort configuration described below before preparing that scope's contract; each new run receives the one fixed malformed UTF-8 byte `{`. No arbitrary manifest, path, executable, business command, environment key or skip-guard input is accepted by the public runner.

`BepInEx/config/BepInEx.cfg` has one exact pre-run and post-run digest per scope:

| Scope | Required digest |
|---|---|
| Positive / ProbeMissing | `F2E0F930EFDA6A7FA292E872760A27CDC9BB1D232CD82D719446622C4DC6EF06` |
| InvalidManifest only | `A0DA59BFA4590A1C3A2B75FF8FF8EE078EF192783606994FF2D1772C358CDBE6` |

The latter comes from `artifacts/early-abort-config/BepInEx.early-abort.expected.cfg`, independently generated from pristine input and the actual early binding declarations. InvalidManifest binds its frozen derivation receipt, audit report and derived file. All 27 ordered settings are unchanged; only the exact approved serialization is accepted. There is no two-hash allowance, semantic tolerance, live-hash adoption or automatic config write. The coordinator explicitly restores only the copied lab's pristine F2E bytes before Positive or ProbeMissing; daily state is never restored by this workflow. Of the 451 base file pins, only this one changes for InvalidManifest. Totals remain 454 pins with all three harness DLLs, or 453 for ProbeMissing.

```powershell
pwsh -NoProfile -File tools/headless/EditorRoutePrepareContract.ps1 -Scope ProbeMissing
pwsh -NoProfile -File tools/headless/Invoke-AzuriteEditorRouteSelfTest.ps1 -Scope ProbeMissing -TimeoutSeconds 120
pwsh -NoProfile -File tools/headless/EditorRoutePrepareContract.ps1 -Scope InvalidManifest
pwsh -NoProfile -File tools/headless/Invoke-AzuriteEditorRouteSelfTest.ps1 -Scope InvalidManifest -TimeoutSeconds 120
```

These commands are an execution reference, not evidence that they have run. Never substitute one scope's installation shape or contract for another.

## Runtime boundary and proof

Every run has a fresh GUID-D `RunId`; the unchanged process helper receives the same GUID in N format as its job handshake correlation. Both forms are checked. The native command is always `capabilities`, transported by `AZUREARCHIVE_CLI_DIRECTORY` and `arguments.json`. The child alone receives `AZURITE_EDITOR_ISOLATION_MANIFEST`. Native arguments are exactly `-batchmode -nographics -disableaudio -logFile <run>\output\unity.log`.

The main and miss-control seeds contain all 26 native settings. Their distinct markers are `lab-read-<RunId>` and `miss-control-<RunId>`. The four volumes are 1; lists are empty, behavior booleans are false, custom/catalog strings are empty, and workspace/cache are run-local. `SeedSha256` hashes the main seed, and `ControlSha256` hashes the miss-control seed. It is never the launch-control JSON hash. `manifest.json` uses the exact eight documented version-1 fields.

The bootstrap joins a fresh unnamed Job and cannot start identity/native children until the outer controller obtains its query/terminate observer and acknowledges it. It preserves the existing shared UTC deadline, below-normal priority, bounded output and window observation, a maximum five-second cleanup grace, and proof of full owned-tree drain. No input, focus, screen or global-volume operation is performed.

After the direct native process exits, the bootstrap allows an observation budget of at most 2,000 milliseconds, further capped by the remaining shared deadline, for the exact Job member list to contain only the bootstrap on two consecutive observations. It does not ignore unavailable/exited-looking members or permit named helpers. The first four and last four samples retain bounded member IDs and up to eight PID/creation-time/state details per sample. Query failure, persistent membership or budget exhaustion fails before reading native evidence. Synchronous diagnostic queries can finish just after the budget; such late observations cannot authorize success. The outer controller independently checks this record and still requires its unchanged final full-Job drain to zero before final mutable-evidence acceptance.

Shared profile/HKCU state is honestly shared. The runner requires the actual unelevated current SID and native KnownFolder proof. Fresh before/after snapshots cover the existing bounded actual AA settings, selected daily profile/config inputs and typed PlayerPrefs. Raw snapshots/logs remain under private directories. The existing two exact engine-session-metadata exceptions remain unchanged; all other additions/deletions/content/type changes fail. No restoration occurs.

Positive success requires both an actual shape-validated native capabilities response and complete guardian/probe JSONL. Every event has exactly `sequence`, `utc`, `monotonicTicks`, `runId`, `pid`, `manifestSha256`, `kind`, and `data`. Both consumers independently require the real native PID, run/manifest identity, fresh ordered timestamps, exact route set and frozen native bindings, guardian continuation before Awake, original main-seed read marker, correct native owner/iterator, getter hits, original Addressables effects, the native SaveTask-to-Write edge, saved-file hash/marker, and terminal completion. Registration, process exit 0 or capabilities alone cannot pass.

Negative success requires actual native exit 86 and the correlated guardian-own abort before native scene continuation; capabilities must not be produced. `ProbeMissing` requires armed then missing-probe failure. `InvalidManifest` requires the early `guardian-setup-failed` event bound to the malformed raw bytes. Neither may produce probe/read/write events, change either seed or create settings sidecars. These are real CLI-transport-configured invocations stopped before scene continuation; they do not fabricate an observed `AuthoringCliHost.IsActive` value at a later lifecycle point.

Loader evidence is captured only after full owned cleanup and evaluated after independent native/guardian validation. Positive and ProbeMissing require a nonempty fresh complete loader log. Only the exact proved InvalidManifest early-abort boundary may have no loader file or a fresh complete zero-byte file. Existing stale, oversized, truncated, changed or misattributed files are never waived. Private results record the loader capture and admission decision. Shared-state preservation and the exact scope-specific installation pins remain required for overall success.

The permitted CachePath interception miss remains a possible daily-settings **read**, as documented in the frozen native source review; the returned path cannot be consumed for downstream I/O. This is not a no-daily-read claim. Native source review and NoSound configuration proof do not establish physical output/PCM or normal Editor acceptance.

## Failure retention

The same global AA mutex is shared with the stable CLI runner, and any other AA process blocks admission. A pending private cleanup marker blocks subsequent preparation and launches. After any runtime failure, pin drift, unexpected state change, missing proof or uncertain drain, quarantine remains until explicit coordinator review. There is no automatic reset, restore, log trimming or cleanup on an uncertain drain. In that case, bounded controller diagnostics are saved separately under private `secrets`; native run evidence remains untouched. A proved successful scope may clear only its own pending marker after full drain and accepted shared-state comparison.

Private controller diagnostics retain `expectedControlSha256` for the outer `tooling/control.json`, together with validation stage and script stack. Prepared-file mismatches also retain the exact named check/path, expected digest and value type, observed digest or read error. The native manifest's `ControlSha256` remains the miss-control **seed** digest; these are separate fields in separate records. The stager returns its nested tool-pin mapping as a property object matching the JSON readback, so validators enumerate the thirteen file pins rather than dictionary metadata.

## Tests

```powershell
pwsh -NoProfile -File tools/headless/EditorRoute.Tests.ps1
pwsh -NoProfile -File tools/headless/EditorRoute.Tests.ps1 -ProcessFixtures
pwsh -NoProfile -File tools/headless/EditorRoute.Quiescence.Tests.ps1
pwsh -NoProfile -File tools/headless/EditorRoute.ControlHash.Tests.ps1
pwsh -NoProfile -File tools/headless/EditorRoute.LoaderEvidence.Tests.ps1
pwsh -NoProfile -File tools/headless/EditorRoute.ConfigPolicy.Tests.ps1
```

The optional process fixtures launch harmless system PowerShell children only. They exercise completion, owned-child timeout, absent handshake and output overflow, using the same public process helper and controller GUID/deadline adapter. Synthetic JSONL/settings/typed-registry fixtures test the parser and negative controls; they are explicitly not native AA acceptance evidence. No test installs a DLL, starts AA/Unity, opens a project or writes daily settings, account state, registry values or global environment.

The quiescence fixtures additionally compile a small harmless managed child in a fresh workspace artifact directory. They exercise the actual `Run()` return-to-`ProcessIds()` boundary, direct self-abort 86, a naturally exiting helper, a persistently live helper, exhausted deadline and injected query error. The original first real ProbeMissing failure is retained; its unrecorded extra member identities remain unknown. These fixtures do not convert that failed run into accepted runtime evidence.

The control-hash fixture runs the real production stager and effects validator in a private workspace-only lab. It replaces only the test module's lab location and shared-state readers; serialization, ACLs, tool copying, helper compilation and hash checks remain real. It checks all thirteen named pins against JSON readback, then independently mutates every staged tool and the launch-control file. All fourteen mutations must fail and retain precise private diagnostics. It neither invokes the production launcher nor grants runtime admission.

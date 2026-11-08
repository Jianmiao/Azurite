# Restricted native CLI query probe

This is authoring-branch infrastructure for the fixed independent lab at
`E:\aamod\labs\Azurite-20261108-stable`. The first native capabilities attempt
returned a fresh success envelope but the overall run remained failed; its
records are preserved unchanged. The user's
selected execution mode is explicitly **`-UseCurrentAccount`**: use the current
unelevated Windows identity without creating any account, supplying credentials,
loading another profile or requesting elevation. Independent review precedes
the first native query. There is no account creation, elevation, audio-device
change, input injection, visible fallback, or arbitrary-command option here.

The existing `Invoke-AzuriteHeadless.ps1` audio gate is unchanged. This separate
entrypoint permits only the resource-free native `capabilities` and `schema`
queries under the fixed static NoSound startup contract.

## Read-only plan and later invocation

Use PowerShell 7 for the controller. `-PlanOnly` performs validation and reports
missing prerequisites without creating directories, locking a mutex, compiling
the process helper, changing ACLs, or starting a child:

```powershell
pwsh -NoProfile -File .\tools\headless\Invoke-AzuriteRestrictedCli.ps1 `
  -LabRoot 'E:\aamod\labs\Azurite-20261108-stable' `
  -UseCurrentAccount -Command capabilities -PlanOnly
```

After independent review and explicit recovery of the existing quarantine, the
next bounded current-account capabilities query is:

```powershell
pwsh -NoProfile -File .\tools\headless\Invoke-AzuriteRestrictedCli.ps1 `
  -LabRoot 'E:\aamod\labs\Azurite-20261108-stable' `
  -UseCurrentAccount -Command capabilities -TimeoutSeconds 120
```

After reviewing a successful capabilities result, request schema separately by
changing only `-Command capabilities` to `-Command schema`. Do not remove a
failed startup flag or pass project, media, export, MCP, or other business args.

The owned AA process receives exactly:

```text
-batchmode -nographics -disableaudio -logFile <absolute-run-output>\unity.log
AZUREARCHIVE_CLI_DIRECTORY=<absolute-run-request-directory>
```

The protected `arguments.json` is exactly the following shape; `cwd` is an
existing, empty per-run workspace:

```json
{"argv":["capabilities"],"cwd":"<absolute-run-workspace>"}
```

There is no `--cli` token. `-disableaudio` is a standalone single-dash Unity
token, not a business argument or an environment substitute.
`--unity-user-data` is a UI style-property literal, not an established startup
flag, and is never passed.

## Fixed inputs and account boundary

`restricted-startup-contract.json` has a digest pinned in the shared helper. It
contains 451 expected file hashes, ten exact directory/file scopes, and sixteen
evidence hashes. Expected values come from the reviewed startup evidence and
the initial `runtime-copy-manifest.json`; the runner never approves current
installation bytes by recalculating its expected pins.

Contract revision `aa-2023.2.22f1-azurite-1.0.38-cli-nosound-v2` changes only the
selected `halocue.azurite.cfg` byte pin to the independently reconstructed
canonical BepInEx save (`0D3CA42AE3AF63C8E147FC2657747F8E6A166CC4F472DF797C0AD43C6249BF38`).
All 57 section/key/value triples are unchanged. The other 450 input entries,
ten scopes and thirteen earlier evidence pins remain identical; the audit,
reconstruction proof and reconstructed expected cfg are three additional evidence
pins. This is a specific reviewed correction, not automatic resealing, tolerance
for future variants, a config write or a quarantine reset. The LF contract digest
is `5CED2B8185DFE43E94217F3B5D61F7B76D8DC5D70C817CABB50A098D46DC8362`.

The semantic startup evidence fixes UnityPlayer 2023.2.22f1, the host executable,
GameAssembly, metadata, serialized startup assets, profile/config, Azurite
1.0.38, the sole patcher, and the three Unity native plugins. Ordinary BepInEx
plugins must remain empty. Initial-copy integrity pins additionally fix
BepInEx core/interop/unity-libs, dotnet, and root native binaries. Those extra
hashes establish dependency integrity, not a semantic audit of each library.
Doorstop's reviewed paths resolve under the isolated install:
`BepInEx\core\BepInEx.Unity.IL2CPP.dll`, `dotnet\coreclr.dll`, and `dotnet`;
boot-config and Mono-search overrides are empty. Reparse links and unexpected
startup/runtime environment overrides are refused.

Current mode validates the marked/pinned lab, current SID, unelevated token,
private lab preparation and absence of another AA process. It does not call
dedicated-account, SAM-membership or credential/DPAPI helpers. Being a member of
Administrators with an ordinary filtered token is not itself a refusal. PlanOnly
may report `Ready=true` while `runs`/`secrets` still need lab-local preparation;
it does not create them. The actual invocation creates only required lab-local
directories/ACLs and retains historical files.

The machine-wide PowerShell 5.1 bootstrap runs with no alternate credentials and
no LoadUserProfile request. Its own system Security/Management/Utility modules
are imported explicitly from PSHOME, avoiding PS7 module-path inheritance bugs
without rewriting PSModulePath or other environment values. It proves the same
actual SID/profile as the controller, a non-elevated token and loaded HKCU.
USERPROFILE, APPDATA, LOCALAPPDATA, TEMP/TMP and HKCU remain the actual current
user's values. Only the AA child's process environment gains the CLI directory.
No registry environment is modified. This is not account or HKCU isolation.

Paths must be canonical existing local directories without reparse ancestors.
Current mode checks inherited paths against the controller's actual baseline,
including legitimately configured temporary paths; it does not fabricate a new
Windows profile. LocalLow planning uses its registered definition/override without
compilation. Before the real backup and native start, SHGetKnownFolderPath from
the controller-built helper must agree with that prospective path.

The legacy mode without `-UseCurrentAccount` retains its dedicated-user manifest,
credential, Users-only membership and distinct-profile checks for compatibility.
It is not the execution choice for this user, and is never an automatic fallback.

## Guarded shared user state

The current Windows profile, `KnownFolder LocalLow\foxxlight\AzureArchive` and
`HKCU\Software\foxxlight\AzureArchive` are explicitly shared. The resource-free
native CLI path bypasses UserSettings initialization and workbench startup, but
the engine can still update its own session metadata. No normal Editor command
is covered by this mode.

Before launch, fresh private `shared-state/before.json` and `scope.json` preserve
bounded raw file backups and the full typed PlayerPrefs subtree. File roots are
the AA LocalLow root, daily ActiveProfile.txt, its actual selected profile tree,
daily Doorstop config and daily BepInEx config. Configured project/cache path
strings are backed up as data; the multi-gigabyte project/cache trees are not
followed or enumerated. The bound is 64 MiB total raw data, 32 MiB per file,
512 files/directories, 128 registry keys and 2,048 values (registry data at most
8 MiB). Missing required, inaccessible, changing, oversized or reparse inputs
refuse launch rather than being skipped. ExpandString is read with
DoNotExpandEnvironmentNames and binary bytes/value kinds are preserved.

Only after verified owned-job drain does the controller capture `after.json`
and compare file additions/deletions/content/attributes/timestamps, root
structure and typed registry changes. Raw evidence and deltas stay private;
public summaries contain classification/counts/hashes, not settings or visited
paths. Unexpected state changes or unverifiable capture keep the run failed and
quarantined. Same-user prepared-file hash changes are also refused.

Only these exact existing REG_BINARY values have a narrow metadata exception:

- `unity.player_session_count_h922449978`: both values must be 1–64 ASCII decimal
  digits followed by exactly one terminal NUL; the numeric value must increment
  by exactly one. It is not a little-endian integer and no exact observed byte
  length is hardcoded.
- `unity.player_sessionid_h1351336811`: both values must have the same bounded
  decimal-plus-terminal-NUL shape and unchanged Binary type, without deletion.

Every raw before/after delta remains in private evidence. There is no wildcard
`unity.*` allowance. Metadata-only changes can close the user-preference gate,
but summaries explicitly set `sharedRegistryChanged=true` and
`finalStateUnchanged=false`. **No shared-state restoration is performed**, even
for engine metadata. All actual user preferences and scoped daily files must
remain unchanged; any other delta requires review. Schema is a separate later
run with a fresh baseline after capabilities closes successfully.

## File ownership and owned cleanup

In current mode each run and its tooling/evidence have private controller-owned
ACLs; prepared files are marked read-only and hash-bound before/after execution.
The same SID cannot create an ACL security boundary between parent and child:
the child could remove read-only attributes. Hash/freshness checks are integrity
guards for this reviewed query, not a sandbox claim. Shared settings are never
given new ACLs or attributes. Output/request/workspace stay in the lab.

In legacy dedicated mode, each run has a protected root/tooling directory with only
read/execute access for the test account. Prepared bootstrap/module/native
helper/contract/control/request files are individually protected and hash-checked.
The request directory allows the test account to read and create files, but not
delete children. A file-only, inherit-only CREATOR OWNER Modify grant lets the
native host create, reopen, rename and delete its own response temporary files.
`arguments.json` does not inherit that grant. Output and workspace are separate
mutable directories. The lab/runs ancestor ACL is checked for exact Modify
rights, without DeleteChild, so it cannot override the protected run root.

The controller compiles the reviewed helper source with the system .NET Framework
compiler and stages/hash-protects the resulting DLL. The PowerShell 5.1 bootstrap
loads this DLL; it does not compile source or spawn a compiler before containment.
It creates an **unnamed, non-inheritable, kill-on-close Windows Job**, sets
BelowNormal priority, and joins itself. Before any identity-helper/native child,
it sends one bounded run-correlated handle handshake and waits for an ACK.

Using its already-owned bootstrap process handle, the controller duplicates only
query/terminate access to that exact Job. Before ACK it verifies that the owned
bootstrap belongs, the controller does not, and current membership is exactly
the bootstrap. Missing, malformed or uncertain proof receives no ACK. Child and
grandchild processes then inherit the job; breakaway is not enabled. The
controller is never assigned to any job.

Keeping the observer handle means bootstrap exit alone no longer closes the
last Job handle. On timeout or failure the controller terminates that exact
verified Job and, if necessary, its owned bootstrap handle. Normal bootstrap
completion still flushes the correlated result then terminates its own job.
Before final evidence is read, the controller requires both the direct process
handle to signal exit and the same kernel Job's active-process count to reach
zero. PID enumeration is used for owned-window monitoring, not as cleanup proof.

Process and both redirected pipes share one runtime deadline. A separately
disclosed cleanup grace is capped at **5,000 ms**, and the result records its
actual duration. This grace checks authoritative exit/drain state; there is no
post-return settling sleep. Reader retention is
capped; inherited pipe handles cannot cause an unbounded ReadToEnd/WaitForExit.
The monitor observes only owned-job window/foreground PIDs. An unrelated user's
window change is not a violation. Polling detects observed visible windows but
does not prove that no transient frame ever appeared between polls.

A private `secrets\restricted-cli.pending.json` is durably created under the
global lock before launch. It is removed only for that run after verified full
cleanup and retained controller results. If handoff/drain is unverified or the
controller is interrupted, this marker remains. Both PlanOnly and native starts
refuse it immediately. There is no automatic expiry, deletion or retry; clearing
an unverified run requires explicit reviewed recovery. Mutable child evidence
is neither copied, hashed nor trimmed while cleanup is unverified. Only bounded
in-memory diagnostics and the controller-owned failure result are retained.

## Evidence and failure rules

A zero process exit is insufficient. The controller requires the expected
bootstrap PID/SID/run ID/command/paths, job evidence, exact startup args and
NoSound proof hash, a fresh raw `response.json`, matching hashes, and a native
success envelope with string API version `1`, boolean `ok: true`, object `data`,
and `warnings` array. Capabilities must contain the pinned API/format/schema
identity, the native tool/resource/stable-ID declarations, typed limits,
persistence/project-file contracts and nested `cli_mcp_control` declarations.
There is no `data.commands`; those control commands live at
`data.cli_mcp_control.commands`. This corrects the earlier static-source reading
using the first exact native response. Its fixture validates response content
only: the original failed run remains failed and immutable, and advertised
tools do not establish Editor/card acceptance. Schema must contain its native `$id`, `$schema`,
nonempty tools and CLI commands, and meaningful `$defs.LineInput` and
`$defs.LinePatch` object properties. Generic synthetic `NativeResult` records,
empty/stale/malformed/oversized/failed responses cannot pass.

Captured stdout/stderr, Unity log, raw response and their hashes stay per run.
Without changing BepInEx config, any preexisting isolated
`install\BepInEx\LogOutput.log` is moved to private `previous-log` evidence before
launch. After owned exit, the newly created loader log is copied to the current
run with presence, freshness, size and hash. A missing/stale new log cannot be
replaced by an old log. Live size monitoring triggers owned cleanup on excess;
after verified drain, retained logs are capped and any truncation is explicitly
recorded and cannot yield acceptance. When cleanup cannot be verified, potentially
live files are left untouched under quarantine rather than claiming a retention
cap was enforced. BepInEx's incidental cache and source log writes remain
inside the isolated install. Post-run changes to any pinned dependency are
recorded and fail acceptance; subsequent probes refuse the drift. There is no
automatic resealing.

`controller-result.json` includes `bootstrapProcess.error/reason` with sanitized
outer startup/monitor error details, plus `cleanup.runId`, `verified`,
`jobObserved`, `directProcessExited`, `jobDrained`, `remainingOwnedProcesses`,
`noProcessCreated`, grace/elapsed milliseconds, error, quarantine path and
`requiresExplicitRecovery`. A known native process-creation refusal can prove
that no child was created; an uncertain start outcome remains quarantined.
`helperBuild` records the compiled source/assembly hashes and compiler identity.
Successful native results also carry the target `profilePathProof` and
`identitySha256`. None of these fields upgrades the native acceptance scope.

Execution success is separate from verified cleanup. A shared production
predicate rejects any nonempty startup/monitor error, timeout, window violation,
output-limit violation or nonzero exit. It is used for the native child, outer
bootstrap and helper compiler, and the controller independently applies it to
the serialized native result. An actual exit code of zero remains recorded as
zero even when a monitor error makes the execution fail; the actionable error
is retained in bounded, sanitized form. A passed child status cannot override it.

The exact acceptance label is:

> native CLI query only; audio/GPU/Editor unverified

Static evidence establishes a pre-init NoSound configuration for this fixed
AudioManager/FMOD path. A query does not prove actual backend readback, nonzero
or correct internal PCM, GPU/export behavior, real editing/Undo/Redo/save,
loaded project/media coverage, or GUI acceptance. Actual AA execution under the
selected current account remains a runtime check. Dedicated-user credentialed
logon is only a legacy-mode boundary and is not a prerequisite for current mode.
Offline fixtures are never native AA evidence.

## Offline fixtures

```powershell
pwsh -NoProfile -File .\Tests\headless\Test-RestrictedCli.ps1
pwsh -NoProfile -File .\Tests\headless\Test-RestrictedCliFix1.ps1
pwsh -NoProfile -File .\Tests\headless\Test-RestrictedCliFix2.ps1
pwsh -NoProfile -File .\Tests\headless\Test-CurrentAccountCli.ps1 -CheckProductionPlan
pwsh -NoProfile -File .\Tests\headless\Test-NativeCapabilitiesShape.ps1
pwsh -NoProfile -File .\Tests\headless\Test-CanonicalConfigPin.ps1
pwsh -NoProfile -File .\Tests\headless\Run-Tests.ps1
```

The new suite uses only a tiny harmless console helper and Windows PowerShell
5.1 fixture bootstrap under the test runner identity. It checks native argument
fidelity, child-only environment changes, response refusal cases, protected
sidecar ACLs, loader-log freshness, dependency drift, PlanOnly, nested job
cleanup and unrelated-process survival. Return-boundary checks have no settling
sleep. Missing/dirty pre-child handshakes, persistent quarantine, wrong-run gate
clear attempts and unchanged mutable evidence are covered. The focused review
suite additionally exercises canonical traversal/reparse refusal, target proof
validation and a harmless missing-executable error through controller reporting.
A restricted **same-logon fixture
token** attempts and is denied prepared-input truncate/delete/replace; this is
not a logon as `AzuriteTest`. The `.cs.txt` fixture source is copied and compiled
inside ignored per-run artifacts, so it cannot enter the repository's recursive
HostProfile test compilation.

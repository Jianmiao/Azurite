# Restricted native CLI query probe

This is authoring-branch infrastructure for the fixed independent lab at
`E:\aamod\labs\Azurite-20261108-stable`. It has **not launched AA**. The
dedicated `AzuriteTest` account must first be prepared manually using the already
reviewed account initializer. The controller must then review this implementation
before any native query. There is no account creation, elevation, audio-device
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
  -Command capabilities -PlanOnly
```

Only after independent review and manual account preparation, the exact first
query is:

```powershell
pwsh -NoProfile -File .\tools\headless\Invoke-AzuriteRestrictedCli.ps1 `
  -LabRoot 'E:\aamod\labs\Azurite-20261108-stable' `
  -Command capabilities -TimeoutSeconds 120
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

## Fixed inputs and account boundary

`restricted-startup-contract.json` has a digest pinned in the shared helper. It
contains 451 expected file hashes, ten exact directory/file scopes, and thirteen
evidence hashes. Expected values come from the reviewed startup evidence and
the initial `runtime-copy-manifest.json`; the runner never approves current
installation bytes by recalculating its expected pins.

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

The controller validates the lab marker, private ACL owners and grants,
manifest/DPAPI credential digest, account ownership description, enabled
`AzuriteTest` SID distinct from its own, and Users-only local membership. It
checks for any existing AzureArchive process, then acquires a global single-run
mutex. Failure has no fallback to the controller's account.

The credentialed launch is the machine-wide Windows PowerShell 5.1 executable
with `LoadUserProfile=true`. Its `ProcessStartInfo.Environment` and
`EnvironmentVariables` are not touched. The bootstrap proves its actual SID,
ordinary token privileges, loaded HKCU, known folders and corresponding target
environment paths. Only then does it add the CLI directory to **its AA child's**
environment. Neither user/machine registry environment nor controller environment
is changed.

All profile-related paths must be canonical absolute local directories. Traversal,
ambiguous components, controller-profile overlap, missing/inaccessible directories
and reparse points anywhere in their ancestor chains are refused. The filesystem
check runs under the target bootstrap identity and leaves inherited values
unchanged. The controller may not have access to that user's profile, so it
rechecks canonical containment and the correlated, timestamped bootstrap proof
and identity-file hash rather than probing those directories with its own token.

## File ownership and owned cleanup

Each run has a controller-owned protected root and tooling directory, with only
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
identity and nonempty commands. Schema must contain its native `$id`, `$schema`,
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
loaded project/media coverage, or GUI acceptance. The real dedicated-user
credentialed launch and profile behavior remain runtime checks after manual
setup. Offline fixtures are never native AA evidence.

## Offline fixtures

```powershell
pwsh -NoProfile -File .\Tests\headless\Test-RestrictedCli.ps1
pwsh -NoProfile -File .\Tests\headless\Test-RestrictedCliFix1.ps1
pwsh -NoProfile -File .\Tests\headless\Test-RestrictedCliFix2.ps1
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

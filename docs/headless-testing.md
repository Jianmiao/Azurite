# Azurite headless lab preparation

## Current account requirement

The user changed the account requirement on 2026-11-08: use the current unelevated Windows account and do not create a dedicated or one-time account. Do not run the account-creation or credentialed launch commands retained below for this implementation. The current authority is `plans/azurite-progress-20261108.md`; the initial current-account results are recorded separately in `current-account-cli-validation-20261108.md`.

The current ordinary-Editor lab is `F:\AzuriteLabs\Editor-20261108-normal`. Keep the daily installation and Profile unchanged. Current-account runs must verify shared user files and typed registry settings independently; environment-variable overrides are not an account boundary. Each AA test must remain headless, silent, low priority and uniquely owned, with no system input or foreground switching. Never run concurrently with another AA instance. Native CLI, ordinary Editor, offscreen graphics/audio and performance acceptance remain separate requirements.

The full-restricted launch path and its preparation records are described in `full-restricted-cli-native-preparation.md`, `full-restricted-cli-integration-preparation.md`, `full-restricted-stage-baseline.md` and `full-restricted-inputs-assembly.md`. These documents describe preparation, not proof that all Editor or application gates passed. Every execution must use current source/artifact identities and a fresh run record; never reuse an old runtime approval.

## Superseded dedicated-account design — historical reference only

The remaining material records the earlier account-based design. Its account setup, E-drive lab and status statements are not the current execution instructions.

Use the independent lab at `E:\aamod\labs\Azurite-20261108-stable`. Its `install` directory must be a full AA copy with `AzureArchive.exe`, `ActiveProfile.txt` containing `AzuriteAcceptance`, and `profiles\AzuriteAcceptance\modconfig.json` and `configs`. The lab must have no reparse links to daily writable state. Its existing `lab.marker.json` identifies the lab GUID, the controller SID, the daily install path and the creation time:

```json
{
  "schema": 1,
  "purpose": "AzuriteHeadlessLab",
  "labId": "a-new-guid-for-this-lab",
  "controllerSid": "the-controller-Windows-SID",
  "dailyInstallPath": "F:\\AzureArchive_101",
  "createdUtc": "2026-11-08T00:00:00Z"
}
```

The only account this setup can create or reuse is `AzuriteTest`. The current controller process is unelevated, but its owning user can manually open an elevated shell. There is one setup command, under that **same controller SID**. The script never requests elevation or starts UAC.

First inspect the read-only plan from the controller account in PowerShell 7:

```powershell
& .\tools\headless\Initialize-AzuriteTestAccount.ps1 -LabRoot 'E:\aamod\labs\Azurite-20261108-stable' -PlanOnly
```

After reviewing the plan, manually open an **already elevated PowerShell 7 shell as the controller account** and run:

```powershell
& .\tools\headless\Initialize-AzuriteTestAccount.ps1 -LabRoot 'E:\aamod\labs\Azurite-20261108-stable'
```

Before account or password changes, setup checks the install and native profile, path types, reparse points, local group drift, private ACL owner and entries, and existing mutable/tooling ACLs. Protected descendants, unexpected owners, explicit denies and broad write grants are refused with paths. It validates the generated 43-character ownership description against the real `New-LocalUser` parameter limit. An unexpected existing `AzuriteTest` account is refused. A new account is explicitly joined to built-in Users; a manifest-owned account left without that membership by an interruption can be joined on retry. Any other local group membership is refused before password reset.

Setup creates an intent manifest before the account, allowing recovery after an interrupted account/group step. It rebuilds each owned-lab DACL to exactly the controller, Administrators and SYSTEM, plus the intended test role where needed. The test account gets Modify only on the isolated `install`, `workspace`, `user-data` and `runs` directories, and read/execute on the lab root and hash-pinned `tooling\Identity-Preflight.ps1`. The controller's password for `AzuriteTest` is random and sealed only with **CurrentUser DPAPI**, in `secrets\controller.credential.json` under a protected, owner-checked ACL. The manifest commits the credential digest only after the OS password and credential file are written. A rerun with that valid committed pair reconciles lab permissions without resetting the password or overwriting the credential. An interrupted update leaves the digest uncommitted, so rerun refreshes the password and credential to recover. This offline check cannot detect an unrelated external password change; the later credentialed child must prove logon. No password is accepted or emitted in arguments, source, logs or output. A deleted account whose SID remains in the manifest requires manual review. No controller registry or LocalLow state is copied or changed.

The runner has a read-only plan:

```powershell
& .\tools\headless\Invoke-AzuriteHeadless.ps1 -LabRoot 'E:\aamod\labs\Azurite-20261108-stable' -Mode Logic -Scenario 'logic-smoke' -PlanOnly
```

The prospective child launch uses `-batchmode -nographics -logFile -` for Logic and `-batchmode -logFile -` for Graphics. With credentials, Windows builds the target user's environment; public metadata travels in `-AzuriteRunId`, `-AzuriteResultPath`, `-AzuriteMode`, `-AzuriteScenario` and `-AzuriteAccountSid` arguments. The runner refuses an absent account or credential, unexpected group membership, profile mismatch, lab links, changed preflight, or another `AzureArchive.exe`. Before AA, a harmless child under the dedicated identity must confirm its SID, loaded HKCU, UserProfile, LocalAppData, LocalLow and related environment paths, and reject unexpected token privileges. Runs use unique directories, concurrent stdout/stderr capture, one deadline for process exit and stream closure, and cleanup scoped to the owned PID. The visible-window and foreground probe observes only that PID.

Native acceptance requires a fresh result JSON with the exact run ID, numeric owned PID, mode, scenario and account SID, `status: "passed"`, and unique named boolean-passing checks. The fixed contracts are:

| Scenario | Mode | Required checks |
| --- | --- | --- |
| `logic-smoke` | Logic | `host.binding`, `plugin.lifecycle`, `logic.assertions` |
| `editor-session` | Logic | `host.binding`, `editor.session`, `editor.native-edit`, `editor.reopen` |
| `graphics-frame` | Graphics | `host.binding`, `render.frame` |

`graphics-frame` also needs a fresh PNG in the run directory whose byte count and SHA-256 match the result. The validator checks complete PNG chunks and CRCs, decompresses all bounded pixel rows, and decodes the image. It accepts only non-interlaced 8-bit grayscale, grayscale-alpha, RGB or RGBA PNGs up to 32 MiB, 4096 pixels per side and 4,194,304 pixels total. A clean exit or a generic passed check cannot satisfy the contract. A future lab-only native driver must produce real scenario evidence; a correctly formatted fixture alone is not runtime acceptance.

**Current gate:** the preflight reports `audioIsolationVerified: false`; the runner refuses AA launch until a real OS-level audio isolation proof exists. A human acknowledgement is insufficient. No real AA, GPU, audio or Editor acceptance has been performed. The owned-window monitor can detect a visible window but cannot prove that no frame flashed before observation. There is no visible fallback.

Run the offline fixture suite without AA or account mutation:

```powershell
pwsh -NoProfile -File .\Tests\headless\Run-Tests.ps1
```

# Catalog evidence files — preparation only

This library supplies the missing durable `Action<CatalogEvidence>` sink and an independent file consumer for the existing Catalog observer. It references the frozen Entry.Core, NormalEditor.Policy and EditorIsolation.Policy DLLs recorded by the review packages. It does not rebuild or modify Entry.Core, Entry.Host, adapter/policy assemblies, the production plugin, or RenderControlV1.

The result `EligibleForCatalogOnlyEvidence` means the complete record set and supplied external close inputs are internally consistent. It is **not native acceptance**, startup authorization, proof that an external monitor actually ran, or permission to enter a project. The assembly contains no process launcher, process lookup, native calls, automatic registration, permitting admission implementation, project open, registry access, session fabrication, or external-close implementation.

## Explicit integration surface

```csharp
CatalogEvidenceWriter.CreateNew(string path, CatalogRunIdentity identity,
    NormalBinding admittedBinding, CatalogEvidenceLimits? limits = null);
writer.Record(CatalogEvidence evidence); // use as the observer's synchronous Action
CatalogJournalSeal seal = writer.Seal(); // explicit journal completion only
writer.Dispose();                       // releases file; never implies Seal

CatalogEvidenceConsumer.Evaluate(string path, CatalogRunIdentity expected,
    CatalogEvidenceOrigin expectedOrigin, ExternalCatalogCloseProof? closeProof,
    CatalogEvidenceLimits? limits = null);
```

The caller must first satisfy the original external admission and then supply its existing `NormalBinding`. Passing this binding alone does not grant permission. The writer uses the existing `admittedBinding.Paths.Output`, already defined as `<lab>\runs\<RunId>\output`; it accepts only an explicitly named direct file in that existing directory. It verifies that run/PID/native thread match the Catalog identity and that the supplied run path matches the existing lab/run layout. The library supplies no admission implementation, alternate allowlist or automatic installation discovery. This supports the actual admitted F run output while all development/test IO in this task remains on C.

The caller also supplies actual managed/native thread binding, target process creation time, and the observer clock's actual UTC start time. There are no default paths or manufactured run identifiers. Expected identity and original file origin are supplied separately to the consumer. Process start time is part of identity so PID reuse cannot silently qualify another process. The header records `RunOutputDirectory` and `OriginalEvidencePath`; external close evidence must identify those same original output bytes.

`CreateNew` refuses existing files and rejects paths outside the exact bound output, nested destinations, relative paths, UNC/device paths, alternate streams, dot segments, reserved device names and reparse paths. Existing ancestors are checked from the volume root outward before descendant access. The consumer reads only the explicitly supplied C archive path. It checks the original run-output origin lexically and never opens or probes that original path. These are file-route checks, not a new permission system. They do not establish protection from a hostile concurrent filesystem replacement or mount change. The caller still owns the admitted working directory and process environment.

Each header/event/terminal is UTF-8 JSON followed by LF. Records reuse `CatalogEvidence` without changing its fields. The writer uses `FileMode.CreateNew`, `FileOptions.WriteThrough` and synchronous `FileStream.Flush(true)` before returning from each append. Defaults are 8192 bytes per JSON line, 262144 total bytes, and 64 evidence records; caller-selected bounds cannot exceed 65536 bytes, 4194304 bytes and 1024 records. Strings are bounded before JSON serialization. A failed write or size/count rejection latches failure and prevents sealing. Partial files and failed attempts are retained.

`Seal` writes one final record containing the event count and SHA-256 of all preceding bytes, and returns the resulting whole-file byte count and SHA-256 only after the terminal flush succeeds. `Dispose` never writes this terminal. A record/second seal attempted after sealing latches failure and attempts to append an invalidation byte, preserving the old contents. If the storage failure also prevents this marker, external failure reporting remains mandatory. This library cannot guarantee disk-controller durability or preserve a failure marker after physical storage loss.

The eventual integration must seal only after the evidence producer is quiescent at its admitted shutdown boundary, before the writer is disposed. Existing Entry.Host does not provide that shutdown connection: it is still an integration prerequisite. Do not seal on Ready, stop monitoring on Ready, or treat a successful Seal return as process-exit evidence. No shutdown hook is installed here. An exception from the sink must remain a failure for both the coordinator and external monitor.

## Independent consumer and close inputs

The consumer opens the file with sharing that excludes an active writer, reads the complete bounded contents, and then evaluates every row. It requires one header, consecutive event numbers, the exact Start-enter/return/completion then Ready lifecycle, stable run/PID/thread/Catalog owner, monotonic clocks and frames, an unexpired readiness deadline, a later Ready frame, complete matching Ready observation, the existing Catalog idle predicates, and exactly one matching terminal at EOF. Any `catalog-failed`, unknown or duplicate JSON fields, omitted fields, missing terminal, truncation, trailing data, duplicate terminal, mixed identity, altered digest or bounded-input violation rejects eligibility. A Ready row alone cannot qualify a run.

`ExternalCatalogCloseProof` is a data contract for an independently collected actual close observation. It includes:

- The same complete target identity, including actual process creation time and observer clock start, plus the observed Catalog owner.
- The same original run-output directory and exact original evidence-file path, independently linked to the C archive.
- A different actual monitor PID and its UTC process start time.
- The actual successful normal-close request time, actual exit-observed time, and the time at which the complete evidence-file digest was observed **after exit**.
- The bounded close timeout, actual exit code, normal-close request result, timeout/forced-termination indicators, and whether any observer/writer failure was observed.
- The exact whole-file byte count and SHA-256 collected after that process exited.

The consumer requires all timestamps to be UTC and ordered; close must follow readiness, exit must fit the stated close timeout, the normal request must have succeeded, exit code must be zero, and timeout/force/observer-failure flags must all be false. It requires the close identity and whole-file digest to match the file it independently reads. A missing close proof remains ineligible.

These checks cannot authenticate the physical origin or truthfulness of a caller-provided record. Only the external admitted integration and independent review can establish that the recorded target really exited normally, that no writer failure was lost, and that the proof was collected from this run. No fake/permitting monitor is supplied. The tests use explicitly labeled controlled records solely to exercise file behavior and schema decisions; they are not accepted runtime evidence. Runtime installation, registration, actual native callbacks, shutdown wiring, physical close observation, resource/cache/discovery/NoSound gates, and all normal Editor acceptance remain outstanding.

## Reproduce the C-only checks

From this C worktree, with the recorded frozen dependency DLLs and installed .NET reference pack:

```powershell
$evidenceOutput = Join-Path (Get-Location).Path 'artifacts/normal-editor-entry-evidence-20261113-revision2'
New-Item -ItemType Directory -Path $evidenceOutput -Force | Out-Null
dotnet run --project AcceptanceHarness/NormalEditor.Entry.Evidence.Tests/NormalEditor.Entry.Evidence.Tests.csproj -c Release --no-launch-profile -- $evidenceOutput
```

The test executable creates a unique retained fixture directory beneath the explicitly supplied C output directory, including controlled `lab/runs/GUID/output` paths represented by the existing `NormalBinding`/`RunPaths` types. It exercises the real frozen Core readiness coordinator with controlled observations, the actual writer, actual Windows files, and the actual consumer. An explicitly synthetic F origin is used only as string data in a C archive test; it is not physical external-close evidence. The suite does not load Entry.Host or interop, launch AA, contact native code, read F/E, or access the registry. It does not induce disk-full/controller-loss failures; the verified write-failure cases cover file collision, bounded write rejection, writes after close/seal, and coordinator failure propagation.

Current result: **100 passing checks, zero failures**, preparation only. The review package in `artifacts/normal-editor-entry-evidence-20261113-revision2/review-package.json` binds source snapshots, output assemblies, frozen dependencies, final logs, and retained failure logs. The earlier 86-check C-only-writer candidate remains preserved under `artifacts/normal-editor-entry-evidence-20261113/` as intermediate history; its writer route was not suitable for the admitted F runtime and it is superseded by revision 2. No Git commit, installation, launch or native acceptance is included.

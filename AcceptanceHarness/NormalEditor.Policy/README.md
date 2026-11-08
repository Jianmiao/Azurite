# Normal settings lifecycle policy — preparation only

This is one additive managed component for the next normal Catalog increment. It does not install hooks, start AA, create native owners/sessions/iterators, set native readiness/dirty flags, or provide normal-launch permission. An external admission lease is mandatory on every transition; no production permitting implementation is supplied. A null lease fails closed. Fixture leases exist only in the test project.

The existing route-selftest `ProbeLedger` and its normal-mode refusal remain unchanged. This project references the already-frozen `Azurite.EditorIsolation.Policy.LabOnly.dll` for the exact `RunPaths` and `WindowsFileIdentity` types. It does not reference/build the old project. Tests reference this new production project and exercise its actual filesystem snapshot reader on new C: fixtures. The frozen legacy DLL must be present; there is no fallback copy of its policy logic.

## Bounded design decision

The ledger binds one canonical run ID, controller binding generation, current PID, native thread ID, actual managed callback thread, and observed native owner. Read and SaveTask iterator pointers are observation identities, not objects allocated by this component. All callbacks carry a monotonic observation sequence; stale run/generation/owner/data/path/thread/sequence evidence fails and latches the ledger closed.

Valid startup is original read entry/return, native OnReady entry while `isReady=false`, possible resource initialization observed inside that callback while the flag is still false, OnReady return still false, then a later actual true readiness observation. Resource-start observation is not cache/resource authorization. A genuine SaveTask creation callback can itself supply the later true readiness observation after OnReady returned; it does not require manufacturing an earlier readiness event.

The bound SaveTask must first execute state 0 and yield state 1. Subsequent state-1 resumes may be clean or dirty. A dirty resume must contain exactly one matching native WriteUserSettings entry/return. Fresh path/file/hash validation runs at every write entry, after the original write return, and again before committing that write at the enclosing SaveTask return. Accepted counters and hash lineage commit only when the enclosing original SaveTask return is valid, yielded true and returned to state 1. No refusal is counted as a completed write/cycle. Reentrant callbacks and stale/foreign invocation or write tokens are rejected.

Native details were resolved only from local existing evidence: `GameAssembly-100689392.asm.txt` enters state 0 without writing, checks dirty in state 1, calls WriteUserSettings at `0x7f2662`, then returns true/state 1. `GameAssembly-100691301.asm.txt` deletes bk.3, moves bk.2→bk.3 and bk.1→bk.2, and replaces main with backup bk.1. The accepted CLI-positive run's saved inventory confirms first bk.1 hash equals its original seed hash `29ed8483c6ff3bffd1a11ca0751c43ea36fda93da89c3122d3862c5c6a2c02be`; that one write is not relabelled as repeated normal-runtime proof.

Accordingly, the managed hash lineage expects new bk.1=previous main, bk.2=previous bk.1, bk.3=previous bk.2; control-seed bytes remain fixed. Repeated identical-content saves are allowed. The reader admits only the main/control and contiguous bk.1..3, rejects reparses/hardlinks/unknown sidecars, bounds each file to 1 MiB, and validates persisted workspace/cache paths. It reads fresh snapshots; it does not perform native saves or hold read locks across an original native write.

This is not an OS transaction, same-account adversary boundary or proof that supplied callbacks really came from native code. The future adapter must authenticate those callbacks, establish actual InitTask→SaveTask ownership, provide the independent normal admission/consumer-exclusion contract and synchronously terminate the owned child on refusal. Hash lineage does not distinguish an attacker recreating identical bytes, and the component does not prove actual disk writes when content is identical.

The component's repeated-save observation property is only a managed sequence result. It does not establish Catalog stability, OS/cache/resource/NoSound/discovery isolation, native bindings, integration, graceful exit or ordinary Editor acceptance. Those gates remain separately required and unprovided here.

## Build and test scope

Build the new test project with `dotnet build AcceptanceHarness/NormalEditor.Tests/NormalEditor.Tests.csproj -c Release`; its only project reference is the new policy. The frozen existing policy binary above is a required read-only dependency, not an automatically rebuilt project. The final checked build used the same command with `--no-restore` after the initial restore.

Run the source-linked console tests with `dotnet AcceptanceHarness/NormalEditor.Tests/bin/Release/net6.0/NormalEditor.Tests.dll AcceptanceHarness/NormalEditor.Tests/artifacts/fixtures`. The final result is 56/56 passed, build 0 warnings/errors. All owner/data/iterator values in these tests are managed observation fixtures; the filesystem checks use actual new C: files. No native AA object, session, coroutine or readiness flag is created or assigned. The test-only admission lease is not shipped by the policy library.

Build/test logs, failed controls, new-file review diff and the source/evidence hash manifest are preserved under `NormalEditor.Tests/artifacts/`. Earlier fixture paths remain intact and are ignored by the test project's scoped ignore file. Review artifacts are not launch approval.

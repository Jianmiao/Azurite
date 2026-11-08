# Normal settings native adapter — preparation only

This component connects the reviewed c9 InitTask lifecycle policy to actual host callback signatures. It is uninstalled build preparation, not normal-launch permission or Editor acceptance. No permitting admission lease, plugin attribute, automatic loader, module initializer, native selftest or coroutine driver is supplied.

## Projects and dependency boundary

- `NormalEditor.Adapter.Core`: the coordinator, immutable callback frames, binding catalog, route records and pure installation-authority checks. It references the frozen normal policy and legacy policy **binaries**, not their projects.
- `NormalEditor.Adapter.Host`: the actual Unity/IL2CPP/Harmony binding and callback layer, compiled against explicitly supplied current host interop/core files. Its only project reference is the new adapter Core project. `Register` is an explicit future entry point requiring external installation admission before target class initialization, metadata resolution or patching.
- `NormalEditor.Adapter.Tests`: source-linked coordinator tests with controlled native-accessor/original-execution fixtures and real new C: settings files, plus Cecil-only inspection of pinned interop and the compiled host layer. Tests do not CLR-load the host layer or target interop types.

The existing policy, original CLI-only probe/guard and their outputs remain unchanged. The normal policy DLL is pinned to `9030d49768ad4a8d773eaf2751a2293241f6f2ee65f0695ca742a8cf2bb0f3e6`; the legacy policy DLL is pinned to `2f6297caec213d87464efad1a387230e40943b89eb71333fb65c8e03dc0d7653`. There is no fallback policy copy or project reference that rebuilds either runtime.

## Admission and authentic callback flow

The only eligible future installation is `F:\AzuriteLabs\Editor-20261108-normal`, with the program under `install` and each bound run under `runs/<canonical-run-id>`. Containment alone is not authority. The running executable and adapter deployment directory are checked separately, and external admission remains mandatory. Expected adapter Host/Core binary hashes must be supplied by the external controller; the component cannot approve its own build. This source-only task does not create or use that installation.

`IAdapterAdmission` separates installation, callback, route and resource admission. Its evidence identity must remain stable across callbacks and immediately around route authorization/audit. It extends the policy's admission interface but has no permitting implementation in Core or Host. Test leases exist only in the test project. Independent cache/resource/discovery/NoSound/consumer-exclusion and OS isolation requirements remain outside this adapter and cannot be replaced with a Boolean flag.

The first pristine non-CLI UserSettings.Awake binds the actual owner while its raw fields still contain their original values. A frame remains active through the original Awake return. The shared IEnumerator StartCoroutine prefix captures the already-created InitTask only under that frame. Its genuine state-zero MoveNext return must yield the actual read child. Read entry/return belongs only to that child; actual data pointer, persisted workspace/cache and seed marker are observed after original deserialization. Fallback/default/dialog initialization is refused.

The bound InitTask state-one invocation contains the actual Resource.Initialize entry and natural saver capture. Resource entry is recorded only if it occurs, requires independent resource admission, and supports early false readiness within the continuation or the later direct true-ready path. SaveTask is captured from the incoming IEnumerator, never the returned Coroutine. Its first state-zero wait may run inside the scheduler call before InitTask returns, or after InitTask's terminal return. Later state-one resumes and writes require completed initialization.

Each modeled original call has an immutable, stack-checked frame. Actual iterator results, next state, `__runOriginal`, normal returns and exceptions come through the host callback layer. Every admitted prefix, including unrelated shared traffic, also issues a completion identity containing its method kind, receiver, managed thread and explicit tracked/unrelated disposition. That identity remains registered until exactly one matching finalizer consumes it. WriteUserSettings must be directly nested in the bound SaveTask frame; filesystem freshness, backup lineage and late commit counters remain in the frozen policy. The adapter never invokes a native factory, MoveNext, read, write, current-session getter, Begin or original scheduler itself.

Healthy unrelated traffic at the shared MonoBehaviour coroutine entry passes through unchanged without dereferencing its iterator or generating policy events. Its explicit `UnrelatedShared` token records that prefix disposition; null never means unrelated. A conflicting bound-owner coroutine is denied. Dedicated UserSettings lifecycle/write callbacks with foreign owner or identity are denied, rather than being treated as unrelated MonoBehaviour traffic.

## Truthful two-field routing

Only `settingFilePath` and `_cachedWorkspacePath` have setter methods in the native-access interface. The actual field implementation uses the checked offsets and IL2CPP write barrier. There is no setter for data, readiness, dirty state, iterator state or current.

After policy/admission binding, Awake routes those two fields to the approved run. Before each assignment the coordinator records a raw routing intent, authorizes it, and confirms the native snapshot has not changed. Afterward it reads the native fields again, verifies that only the specified field changed to the exact target, and records the actual post-route snapshot.

Original InitTask state zero overwrites settingFilePath before yielding. At that exact successful original return the coordinator validates the bound invocation, owner, true return/state one, exact read-child class/owner/state zero, unchanged cached workspace, pristine data/readiness/dirty state and the **explicit controller-bound native constructed string**. It records that raw string, reroutes only settingFilePath once, reads back the native value, and then reports the actual routed snapshot to the frozen policy before the child is consumed. No invented lab path is passed while the native field still differs.

The expected native string is compared directly. The adapter never invokes persistentDataPath or reads the daily settings file to discover it. Arbitrary resets, repeated resets and later drift are refused without repair. Routing is not a transaction or rollback mechanism: an error after a field assignment requires the own-process fatal path, and its attempted/applied route records remain evidence.

## Native binding and failure behavior

Bindings use the captured exact tokens, generated pointer-field names, entry RVAs, native class identities, field names/offsets, pinned input files and loaded module/assembly locations. The shared target is native token `100667066`, field `NativeMethodInfoPtr_StartCoroutine_Public_Coroutine_IEnumerator_0`, RVA `0x3408550`, selected with the exact `Il2CppSystem.Collections.IEnumerator` parameter. Token `100667067` / StartCoroutine_Auto must resolve to the same entry. Both managed aliases are checked for patch conflicts, only the canonical entry is patched, and patch ownership/content is rechecked on callbacks. This does not distinguish which source alias the native caller named.

The host common finalizer supplies the actual `__originalMethod` and receiver alongside `__state` to the same managed dispatch exercised by the tests. The coordinator checks issuance, disposition, identity and per-thread nested order. Missing, foreign, out-of-order or already-finalized state latches failure; an original postfix awaiting finalization also blocks the next original on that thread. Normal finalization requires an observed original return. A positively identified unrelated shared call may finalize with its original exception without a postfix; the exception is returned unchanged and no bound policy event is invented. A bound original exception latches failure and is likewise returned unchanged. Close cannot accept an outstanding tracked finalizer.

The approved host fatal sink uses the existing own-process pseudo-handle termination helper only after rechecking this admitted lab child's process/executable/adapter identity. It has no arbitrary PID argument or process cleanup capability. Coordinator failures return false even if a controlled test fatal sink returns or throws, so the affected original cannot proceed. Missing returns/finalizers are detected by invalid nesting, dispatch, a following callback or close; the component supplies no asynchronous missing-callback timeout/watchdog. Preserving the managed exception object does not by itself establish propagation through the outer IL2CPP trampoline.

The host's local NullableAttribute shim mirrors the frozen probe's narrow compiler workaround for the pinned interop attribute. It does not initialize or modify target types.

## Verification and limits

The source-linked suite has 117 passing coordinator/metadata checks, including both alias conflicts, raw routing records, native default-path reroute timing, later drift, immutable non-route fields, original nesting and exceptions, missing/foreign/stale returns, resource admission, nested initial saver waits, clean/dirty/identical-content saves, fresh commit validation, default-denied admission, evidence identity drift and fatal-sink return behavior. The finalizer cases exercise the actual shared managed dispatch for missing state with/without exceptions, foreign/duplicate/mismatched tokens, valid postfix-to-finalizer completion, unrelated shared completion/exception preservation and refusal of following bound originals. Cecil checks the host's method/receiver/state injection and use of that dispatch, plus all nine method/alias bindings and eighteen fields, without executing target initializers.

Metadata checks establish generated signatures and field/token initializer shape. They do **not** establish live native field offsets, runtime MethodInfo values, native hook hits, actual argument conversion, scheduler timing, patch-backend behavior or native exception propagation. The host layer checks offsets/entries only during a future separately admitted binding operation. The unit suite's native values and original calls are fixtures, not evidence that AA executed those callbacks. Original InitTask effects, including its own native path construction, still need the independent environment/consumer admission contract.

The inherited P3 directory-enumeration limitation remains: snapshot files are byte-bounded, but the policy enumerates and sorts the whole directory twice without an entry-count or total enumeration/memory cap. This adapter does not turn that into bounded hostile-directory scanning or a same-account adversary/OS transaction boundary.

Build/test commands require an explicit build-only metadata source:

```text
dotnet build AcceptanceHarness/NormalEditor.Adapter.Host/NormalEditor.Adapter.Host.csproj -c Release -p:AAInstallPath=F:/AzureArchive_101
dotnet build AcceptanceHarness/NormalEditor.Adapter.Tests/NormalEditor.Adapter.Tests.csproj -c Release -p:AAInstallPath=F:/AzureArchive_101
dotnet AcceptanceHarness/NormalEditor.Adapter.Tests/bin/Release/net6.0/NormalEditor.Adapter.Tests.dll <fresh-C-fixtures> F:/AzureArchive_101 AcceptanceHarness/NormalEditor.Adapter.Host/bin/Release/net6.0/Azurite.NormalEditor.Adapter.Host.PreparationOnly.dll
```

Reviewed runs add `--no-restore` to the two build commands. The original review package remains under `artifacts/normal-editor-adapter-preparation-20261108/`. Finalizer fix-round evidence, the preserved 15-source/3-output/18-log checkpoint, RED/GREEN logs, focused diff and hashes are under `artifacts/normal-editor-adapter-fix1-20261109-97fe4aa63b2743efbd6ce7e8c274786c/`. No native registration, host initialization, installation or ordinary Editor acceptance was performed. Independent re-review remains required.

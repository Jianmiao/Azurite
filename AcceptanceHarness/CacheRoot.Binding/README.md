# Additive cache-root binding component — LAB ONLY

This component is not a plugin, patcher, startup hook or controller. It has no automatic execution attributes and changes none of the existing EditorIsolation source, controllers, projects or frozen DLLs. Its live entry is callable only inside the exact approved AA child. **No Unity/AA native entry has been invoked during implementation.**

## Projects and offline verification

- `CacheRoot.Core`: bounded native-string reader, own-process memory-copy checks, path validation, immutable qualification pins, one-shot proof ledger, guarded result storage and consumer guard policy.
- `CacheRoot.Binding`: exact pointer/byte ABI declarations, loaded module/code/data/metadata verification, original-helper/path-builder/local-destructor runner, and a consumer-path inspector for later independent hook qualification.
- `CacheRoot.Tests`: allocated-memory fixtures, real no-access/cross-page memory tests, lifecycle/path/ABI checks and read-only PE/metadata pin verification. Target code is never loaded or executed for these checks.

```powershell
dotnet run --project AcceptanceHarness\CacheRoot.Tests -p:AAInstallPath=F:\AzureArchive_101 -- --host F:\AzureArchive_101
dotnet build AcceptanceHarness\CacheRoot.Binding -c Release -p:AAInstallPath=F:\AzureArchive_101
```

Only these new projects are built. References use the existing installed BepInEx/MonoMod assemblies; no new NuGet detour implementation is introduced. Test `VirtualAlloc`/`VirtualProtect` calls affect only pages allocated by that harmless test process; no code is injected or executed from them.

## Pin and representation contract

The embedded `binding-contract-pins.json` is the exact static qualification artifact, checked against SHA-256 `8769e65accc4752e2cc5ef6fffc88b48cacbd816b7d290eba4def6fb4070ed69`. Parent requirement documents are pinned as binding report `22a77398898356018b53f5d0f211b59bfd0ab055956bc65d6f1666d57fbd4974` and seed assessment `92b3d173581ba5cd19ccc1e72677726e5aec775f793856c7f6e0589a729f83d8`.

Native string slots are **0x28 bytes**; only the meaningful first **0x22 bytes** are decoded. The reader uses `tag & 0xc0`, accepts owned tag `0x5f`, rejects inline tags 32..63 and the reserved kind, bounds UTF-8 length to 4096 bytes and uses overflow-safe address arithmetic. The general reader may copy a borrowed explicit-length view without a terminator/capacity assumption; the root/result/path protocol refuses borrowed forms. Inline/owned paths require strict UTF-8, no interior NUL and the explicit terminator. Root/result/default-cache labels are distinct: `0x49`, `1`, `9`. Padding is neither treated as initialized data nor modified by the decoder.

`CurrentProcessMemory` validates committed readable pages, excludes guard/no-access pages and copies through `ReadProcessMemory` on the current-process pseudo-handle. Code/data pins additionally require the verified PE image allocation; forward target windows must be readable/executable and exactly hashed. These checks do not create lifetime or cross-thread exclusion guarantees. Read only while the synchronous owner and the externally validated interval remain alive.

## Future explicit integration sequence

`PreparedPathProbe.Prepare(request, validatedLease, correlatedSink)`:

1. Refuses a non-approved process without touching a target pointer. Inside the approved child, any failure terminates only that process with exit 86; a catch-and-continue loader cannot silently resume.
2. Requires the actual executable `E:\aamod\labs\Azurite-20261108-editor\install\AzureArchive.exe`, a hash-bound canonical GUID run manifest, `Mode=route-selftest`, the pre-created `<run>\unity-knownfolder` directory, actual headless flags and the resource-free capabilities request directory.
3. Checks on-disk PE/hash/MVID/signature pins, loaded managed module locations/MVIDs, loaded Unity/GameAssembly/Dobby locations and image sizes, Unity live PE identity, exact pre-patch code windows and data identity. The actual native CLI backing-field accessor must report active; no flag is set. The loaded Unity module reference is retained to child exit.
4. Requires a non-null **external `IPreUseAdmissionLease`**, bound to run/PID/OS thread/manifest identity and a concrete evidence hash. The integration must validate allocator/startup ordering, held guardian continuation and exclusion of competing root/cache callers. This library supplies no lease implementation, fake ready flag, default permissive callback or all-thread-exclusivity assertion. CRT=2, non-null allocator, null manager and an empty root are observed necessary conditions, not that missing proof.

Prepare checks the unpatched initializer window. A future independent initializer hook must therefore be installed/qualified **after Prepare and before Run** while the separately qualified pre-use interval remains held. Run rechecks the original helper/converter/builder/constructor/destructor windows; it does not mistake an expected later initializer detour for an unchanged original entry. Actual target/trampoline verification, provider configuration and native hook-hit qualification remain integration work.

`RunPathOnly()` consumes one attempt per child and one local-result lifecycle:

- **SeededPathOnly:** requires exact original-empty root bytes, CRT=2, non-null allocator, null manager and the bound native thread; revalidates before the original helper. It allocates bounded valid UTF-16 input and calls `UnityPlayer +0x4d45c0` with two explicit pointers, always targeting the existing global `+0x1be73a0`. It verifies exact normalized text, allocator label, inline/owned storage, no input alias and null manager, releases its own input buffer, then reads back again.
- **OmittedSeedPathOnly:** makes no helper call. Its original builder may memoize the ordinary known-folder root, but no directory/cache consumer receives that result. It cannot subsequently seed or enter consumer qualification in the same child.
- Both call the **original** builder `+0x4d0170` with an empty, valid two-word suffix span and transform byte **0**. Result storage is a fresh aligned 0x28-byte slot with separate canaries; no native constructor is called first. The returned pointer, canaries, UTF-8, label 1, root/result relationship, manager-null state and non-aliasing ownership must validate.
- Only then does the runner call original `+0x0dfcf0` exactly once on the **local result**, followed by releasing harness-owned storage. It never runs constructor/destructor on the global, frees Unity-owned data through a managed allocator, copies/moves the global as bytes, resets the manager/read-only flag, or retries a populated root.
- An abnormal return, corrupt result, failed lease, alias, path drift or missing seed postcondition aborts the owned child; uncertain objects are not guessed at for cleanup. The existing native root remains under Unity's normal lifetime through process exit.

The returned outcome always has `NativeConsumerQualified=false` and `NormalStartupQualified=false`. Correlated events include the run, PID, raw manifest hash, sequence and monotonic time. The sink is supplied by later integration; this library does not create or alter a controller/evidence file.

## Initializer guard boundary

The compiled signature is `byte CacheInitializeEntry(nint nativeCacheObject, byte readIndex)`, Cdecl/Win64, with **no MethodInfo** and no public Cache handle. Both byte branches 0 and 1 are covered. The embedded native path begins at `cache+0x38` and the default path label is 9.

`ConsumerGuardPolicy` rejects closed path-only and normal phases. It can admit an explicitly owned consumer qualification after an original positive builder proof and exact root/path/label agreement. `InspectInitializerForOwnedQualification` exposes the same decoder/policy against a raw native object only after this prepared component has completed its actual positive path-only run; it does not call the initializer or its trampoline and does not count as interception proof. A later callback must perform this check before its original trampoline and preserve the input byte/result byte unchanged. Refusals in the live inspector terminate the bound child.

The public installed `INativeDetour` type and all required metadata signatures are qualified. This component does **not** call Create/Apply, install Dobby hooks, implement IAT/system API redirection, or switch providers. The installed Dobby wrapper's ignored native return codes mean a future installer must still verify actual executable trampoline/branch and native callback hits; wrapper `IsApplied` is insufficient.

## Remaining gates

Actual pre-use concurrency qualification and lease implementation; live ABI/seeded-vs-omitted original-builder runs in separate fresh children; actual consumer hook installation/hits with only owned lab input; native index/worker/cleanup qualification; then the independent normal Catalog/resource/audio/discovery/repeated-save gates. Existing route scopes and all frozen EditorIsolation artifacts remain separate.

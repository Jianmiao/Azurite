# Local 1.0.4: native resource scheduling

The user confirmed early editor entry but reported the editor freezing while resources loaded. Saved ErrorLog contains a fatal AccessViolation at IL2CPP IEnumerator.MoveNext -> NativeAdapter.MoveNext -> ProgressivePreloadEnumerator.MoveNext -> Il2CppManagedEnumerator trampoline. Loading count had returned to zero and the last log showed completed=0/1418 active=1. This establishes the failing execution boundary, not the exact invalid-pointer cause. Self-yield test fixtures separately demonstrated starvation, but actual native resource handles inspected did not self-yield; do not conflate them.

Production no longer contains NativeAdapter, Run, PaceResource or WrapResource. Historical progressive iterator/gate/budget files are excluded from both SDK and offline plugin compilation. No native resource factory is patched. Immutable, deduplicated resource requests are copied from native indexed project/node/script lists and handed unchanged to EnumeratorAsyncExtensions.ToUniTask, matching AA's native coroutine consumer. Azurite queries UniTask.Status and consumes completed results once. Single admission per Unity frame, at most four active tasks. Native cancellation and native Forget handle scheduler-owned tasks; mod does not manually dispose their iterators.

AA native BGM sentinels 0 and 999 are skipped. Code review found the initially missing 999 guard; a production-linked regression failed before the correction and passes after it. Resource identifiers from different kinds are never merged.

Verification:

- Production integration suite: 25/25, 408 comparisons to installed host signatures. Previous 24-case form ran against archived 1.0.3 production: 10/24, 14 failures, including a sentinel that prohibits mod-driven native resource MoveNext.
- Dialogue virtualization 26/26; host/display policy 73/73; characters 56/56; preview policy 33/33.
- Plugin build: zero warnings/errors; generated binding metadata: 396 types / 1346 members.
- Fixed RenderControlV1 client/provider tests against installed AAVE 0.2.4: 11/11.

The native task fixture models engine-driven progress, cached completion, failure and cancellation; it is not Unity/IL2CPP. A real AA run must finish resources-ready and allow opening/editing script nodes before claiming the fatal defect fixed. No synthetic GPU or timing gains are claimed. Native individual resource decode remains nonpreemptible; the former hard 4ms framing is removed.

Evidence: E:/aamod/diagnostics/Azurite-1.0.3-entryfix/hang-error-20261006.log, hang-evidence.md, hang-repro.md, native-queue-review.md; final verification under E:/aamod/diagnostics/Azurite-1.0.4-nativequeue.

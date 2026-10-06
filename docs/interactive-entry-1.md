# Local 1.0.5: interactive entry and progressive dialogue presentation

## Evidence and diagnosis

The user's 1.0.4 run reached 1418/1418 native task completions after 20793.7ms; ErrorLog was empty. This disproves a permanently stalled resource queue for that run. The screenshot and user report still show a modal Now Loading overlay and no script interaction.

Native Loading.Awake derives its scene-local active state from static LoadingTags. StartLoading increments a scene-local loadingCount, but StopLoading does not decrement that count. StopLoading removes a shared tag and, when the set is empty, hides only its receiver's GameObject. Workbench uses the EditorScene tag and stops it through the current singleton after scene completion.

Azurite retained the pre-scene Loading instance. Releasing on a destroyed instance could leave its tag in the shared list, while releasing on a surviving previous instance could hide the wrong canvas. Both paths marked the lease released unconditionally. A count of zero in the prior diagnostic therefore did not prove an inactive current overlay. Runtime pointer replacement was not instrumented in 1.0.4; this is a verified lifecycle flaw matching the report, not a claim that a particular pointer transition was captured.

## Fix boundaries

Resolve the live singleton at release. Remove only the private Azurite tag. If no scene instance survives, remove that exact tag directly so the next Awake observes the correct state. A surviving stale overlay is reset only after all shared tags are absent; foreign scene/save/mod tags are never removed or bypassed. Log current overlay visibility, tag count, ownership and instance change at entry/progress/completion.

Keep resource tasks native and unchanged; do not wrap or manually advance native iterators. Resource-dependent callbacks remain guarded while structural/text callbacks stay native. Preserve RenderControlV1 protocol/lifecycle and export admission pause.

Virtualized dialogue presentation binds required/selected/restored rows synchronously. Other visible rows are populated in descending visual index, before offscreen overscan. A shared frame budget prevents Update, LateUpdate and scroll callbacks from multiplying work. Slot dirty state preserves required text refresh even when a later dense fallback occurs. Model data and dialogue order are unchanged.

## Regression signal

Before the overlay fix, source-linked LoadingIntegrationTests reported 26/29: current scene overlay remained active during background loading, a destroyed captured instance leaked its tag, and an absent current overlay stranded the lease. After the fix, the same cases passed. Stubs now reproduce static tags with per-instance visibility and Unity destroyed-object equality instead of a tag-only set.

Before incremental dialogue presentation, the added source-linked test reported 26/27: opening created/bound the whole 12-row window at once. Added cases cover shared frame budgets, bottom-up visible order, slow single-row work, rapid scroll cancellation, editing/selection/insertion/deletion before fill completion, nested callbacks, dense fallback refresh and unload.

Validation uses source-linked simulated Unity/IL2CPP dispatch plus installed host metadata checks. Real Unity raycasts, actual resource decode latency and end-user interaction remain runtime acceptance items. No real-time speedup or crash-free claim follows from these tests alone.

Evidence: E:/aamod/diagnostics/Azurite-1.0.4-nativequeue/overlay-run-20261006.log, overlay-error-20261006.log, overlay-evidence.md. Final verification and installation records are kept under E:/aamod/diagnostics/Azurite-1.0.5-interactive-entry and E:/aamod/releases/Azurite-1.0.5-interactive-entry-1.

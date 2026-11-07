# 1.0.34 editor interaction candidate

## Observed evidence

The saved 1.0.33 log has a cold deferred-to-editor interval of 4809.9ms, versus about 250ms on subsequent entries. Resource task admissions were still zero during the cold interval. Native project byte-read calls totaled 30.650ms, payload parsing 161.841ms, Studio.Start 38.721ms. These numbers do not establish a physical disk bottleneck or attribute the missing interval to a particular operation.

The production progressive controller directly suppressed SyncEnvironmentProperties and OpenBackgroundSelector until the entire resource queue finished. That reproduces the missing environment controls independently of GPU load. Native SyncEnvironmentProperties also calls AudioManager.SetBGM(long) / StopAllBGMs, so simply removing every guard would introduce unwanted audio side effects.

## Implemented boundaries

- Allow native environment widget/model synchronization and the background selector once the editor shell exists. Defer only audio calls nested synchronously inside environment sync. A Harmony finalizer restores a thread-local depth even after exceptions. Completion refreshes the currently selected environment; it never replays a saved project value over user edits. Popup, BGM and sound selector resource guards retain their prior behavior.
- Reserve the first 100ms of a new input window from new progressive resource admissions; continuous input admits at most one pending task and leaves 125ms between admissions. Native tasks already running are polled and completed normally. Idle loading keeps the prior one-per-frame / four-active ceiling. This cannot interrupt an individual synchronous native decoding/upload step.
- Native CharacterExplorer.Awake (100690287, RVA00814540) hides/registers its window, then calls CharacterList.LoadCharacters (100690335, RVA00816fb0), which builds all rows. Suppress only that exact list call during its editor Awake. Preserve the rest of Awake/Start; initialize on actual selector demand. Import and alias-change callers remain native.
- Native BackgroundItem.Update/UpdateLoadState already handles visibility. Hook LoadBGAsync instead of repeating six worldCorners accesses per item; bypass its loaded-item callback allocation. New requests are limited to two per frame/eight tracked requests. Generation-aware callbacks/unload release capacity. A lost-callback timeout releases accounting only, not native requests, so eight is not an absolute engine queue bound.
- PreviewTargetLease compares scaled target sizes consistently and retains its valid target while a quality change settles. True viewport growth, export, external ownership and invalid lifecycle retain restoration paths. Do not confuse HostActivity.Blocked.HasInteraction with confirmed user input.
- Global UserWheelScale applies after per-area normalization and updates live. Existing stored per-area values are no longer rewritten by repeating startup migrations.

## Validation and limits

Environment regression initially failed because controls never synchronized while resources were pending; the controller test harness now verifies actual registered hooks, nested audio scope, exception cleanup and export boundaries. The gallery and preview suites reproduced redundant allocation and texture-lease churn against the previous source. Selector tests link the production controller and inspect real host signatures instead of testing a second model.

These are managed simulations plus metadata checks, not Unity frame-time/GPU measurements. First character selection still constructs the native list synchronously. Full background item-object virtualization and complete asynchronous scene activation are not implemented by this patch. Real UI rendering/layout and cold-start timings remain user validation items.

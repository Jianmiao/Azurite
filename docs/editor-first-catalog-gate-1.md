# Local 1.0.15: editor-first catalog gate and dialogue viewport fix

## Runtime finding

The real 1.0.7 log showed the resource queue at `0/1418` when the editor shell and script node became available. Script opening completed in about 73ms. The long first-click stall happened earlier inside AA's `AuthoringEditorSession.Begin`: `catalog-capture-metadata` was 15327ms, `global_content_ms` 13475ms, `ensure-native` 1302ms. This is the native authoring catalog path, before Azurite's background resource tasks can run.

## Gate and boundaries

Native `AuthoringEditorSession` construction calls `Refresh(true)`. On the verified host, `Refresh(false)` captures the project and fingerprint and observes the existing catalog version field; `Refresh(true)` calls `CatalogVersion`, which performs the cold catalog capture. Azurite scopes a Harmony prefix to the initial `StudioCommon.Start`/`AuthoringEditorSession.Begin` call and to `StudioCommon.OnChangesMade` while its progressive session is pending. Only the `bool checkResources` argument in those calls is changed from true to false.

The gate does not patch `BeginSave`, `CheckSave`, `Context`, `Apply`, or other explicit authoring endpoints. Saving therefore retains AA's authoritative catalog-version check. When the background queue completes, Azurite does not automatically invoke `Refresh(true)`, because that would simply move the same long freeze into the user's editing session. An explicit save or context operation remains the point where AA performs its consistency check.

The gate leaves the file lease, project fingerprint, captured snapshot, session revision and native resource catalog state under AA's control. It does not fabricate a catalog version, clear the catalog, bypass save validation, or move native IL2CPP work to a worker thread. If the session is disabled or the gate is unavailable, AA's original `Refresh(true)` path remains unchanged.

## Tail and list behavior retained

Opening a script selects the last logical dialogue. The real NGUI bottom normalized value is `1`; Azurite disables the scroll spring, publishes full mathematical bounds, and holds a target-specific guard for four frames so the deferred native `CenterOn` callback cannot pull the viewport back to row zero. Selecting another row clears that guard. Visible rows are created from the tail upward in cooperative batches; data order is unchanged.

## Validation

LoadingIntegrationTests now cover the initial catalog capture gate, first change gate, post-resource non-blocking behavior, explicit save observation, overlay lifecycle, native task scheduling and editor actions: 34/34. VirtualizationTests cover deferred NGUI tail centering and list editing: 42/42. DriverTests: 10/10. SelectorTests: 14/14. The production build has zero warnings and errors; installed host and AAVE RenderControlV1 checks are run before installation.

This candidate still does not promise zero-time scene activation. The measured native scene/editor startup remainder must be observed again after the catalog gate is installed.
The 1.0.15 candidate also corrects the NGUI tail anchor: AA's `SetDragAmount(..., true)` refreshes the clipping offset without translating the content transform. Azurite follows native `ResetPosition` and calls the same endpoint with `false` first and `true` second, so the virtual rows are physically inside the camera viewport.

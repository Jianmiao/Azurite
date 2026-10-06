# Progressive character and emotion entry

Candidate: 1.0.17, progressive-character-entry-1.

## Behavior boundary

During progressive resource loading, `OpenCharacterSelector`, `OpenEmotionSelector`, and `SetCharacter` remain native. `SetCharacter` is allowed to mutate the script model; its nested `SyncSlots` and `SyncOrClearSlotProperties` calls remain guarded, so incomplete assets are not touched. `OpenBackgroundSelector`, `SyncEnvironmentProperties`, BGM, sound and popup/background resource operations remain guarded until the queue completes.

This boundary is intentional: selector entry and model edits do not require the selected asset to be instantiated, while environment/resource synchronization does.

## Startup preview safety

AA `ScriptNodeInspector.Start` can follow early script loading. Its native `OnChildSelect` calls `SyncAll` and `PlayPreview`; `PlayPreview` synchronously enters `Test.AdvanceScenario`, which may recursively traverse dialogue and resource/UI work. Azurite therefore rejects `PlayPreview` while `inspector.loading` is true. The existing progressive update path records the pending inspector and performs one preview after the load call yields. This removes synchronous scenario advancement from `Start` and avoids duplicate preview work.

## Validation

`LoadingIntegrationTests` source-linked production controller: 36/36. `VirtualizationTests`: 53/53. `DriverTests`: 12/12. Tests use simulated Unity/IL2CPP dispatch and do not replace live AA interaction testing.

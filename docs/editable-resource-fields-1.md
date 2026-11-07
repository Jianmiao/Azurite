# Editable resource-backed fields during progressive loading

Candidate: 1.0.23, editable-resource-fields-1.

The old progressive gate treated field synchronization and resource acquisition as one operation. That made the right-hand editor appear unavailable while the project queue was still loading. Native `SyncSlots` and `SyncEnvironmentProperties` read model data and update labels/inputs; they do not need to instantiate the selected background, voice, BGM or character asset. They now remain native during loading.

`SyncOrClearSlotProperties`, background/popup/BGM/sound selectors and other resource-dependent paths stay guarded. This keeps data entry responsive while preventing incomplete assets from entering native resource code. After the queue completes, the pending inspector refresh runs the resource paths.

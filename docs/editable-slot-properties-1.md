# Editable slot properties during loading

Candidate: 1.0.24, editable-slot-properties-1.

The previous gate suppressed `SyncOrClearSlotProperties(bool)` along with resource acquisition. Native `SlotProperties.SyncAndFixProperties` maps the selected Script data into the role, emotion, display order, position, action and effect controls; it does not call the resource factories. Keeping this call native allows the red-box editor to initialize and accept model edits while the project preload is pending.

Resource selectors and audio/background/popup operations remain guarded. The split preserves the loading priority: model/UI synchronization first, resource instantiation after the queue completes.

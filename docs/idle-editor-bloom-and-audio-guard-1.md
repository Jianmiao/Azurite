# Editable slot properties and idle Bloom reduction

Candidate: 1.0.25, idle-editor-bloom-and-audio-guard-1.

The first red box is now initialized through native SlotProperties synchronization while resource preload is pending, so role, emotion, position, order, action and effect model controls can be edited. Background, popup, BGM and sound selectors remain guarded.

The environment sync path is deliberately still gated: AA's native SyncEnvironmentProperties also queries BGM metadata, initializes transition/effect properties and calls AudioManager. It is refreshed after resources are ready rather than entering that mixed path during preload.

EditorBloomLease now disables shared editor Bloom only during idle, and restores it for interaction, scroll, loading, save and export. Static preview caching remains scoped to the inspected Fix host.

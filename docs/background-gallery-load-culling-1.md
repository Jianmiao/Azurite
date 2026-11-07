# Background gallery load culling

Candidate: 1.0.29, background-gallery-load-culling-1.

`BackgroundExplorer` keeps the full logical `items` list for search, folder filtering and selection. Azurite only intercepts `BackgroundItem.Update` when an item is still `unloaded` and its texture bounds are outside the current scroll panel. The native update is skipped for that frame, so `LoadBGAsync` is deferred until the item enters the viewport. Loaded items continue through native update/unload behavior.

The category label scroll remains at the native multiplier to avoid UIGrid text overlap. This is not full object-pool virtualization yet; it is the lower-risk resource admission stage.

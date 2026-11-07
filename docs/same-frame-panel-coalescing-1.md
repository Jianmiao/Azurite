# Same-frame panel coalescing

Candidate: 1.0.31, same-frame-panel-coalescing-1.

Virtual row binding can call `UIPanel.SetDirty`/`UpdateSelf` more than once during a single Unity frame. Azurite now keeps every dirty mark but limits immediate `UpdateSelf` to one call per panel per frame. The first rebuild remains synchronous; later calls in that frame leave the panel dirty for native NGUI processing. The coalescer is scoped to the virtual dialogue presentation path and covered by a 54-case regression suite, including visibility, selection, scrolling, mutations and fallback.

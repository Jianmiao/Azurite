# Static preview cache

Candidate: 1.0.22, static-preview-cache-1.

`PreviewCameraCadence` already had a scoped static-cache path. 1.0.22 enables it by default on the verified 1.0.0-fix host by enabling the scoped ownership opt-in and `CacheStaticPreview`. The live lease still validates PreviewScene, Base camera, empty camera stack, exact render target, single UITexture consumer, callback subscriptions and export restoration.

Content panel geometry/clip callbacks, camera matrix changes, target rebinding, animation/activity detection, loading, scrolling and mutation protection invalidate the cache. The camera is restored for the next draw; AAVE ownership and export restore paths remain unchanged.

The global NGUI panel suppression experiment remains opt-in because skipping arbitrary `UIPanel.UpdateSelf` calls could delay text or selection updates. This candidate focuses on the verified preview camera boundary.


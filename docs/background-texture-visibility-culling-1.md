# Background texture visibility culling

Candidate: 1.0.32, background-texture-visibility-culling-1.

The gallery retains every native BackgroundItem and its logical resource state. For items whose UITexture bounds are outside the scroll panel, Azurite defers unloaded acquisition and disables only the already-loaded texture widget. The original enabled state is tracked and restored when the item enters the viewport or the Mod unloads. Search, folder filtering, ordering, selection and native resource unloading are untouched.

# AA Render Coordination Protocol v1

This protocol defines the public handoff between Azurite and AAVideoExport. Internal fields, implementation classes, encoder versions, and UI layout are not part of the contract. Compatibility is determined by the protocol major version, not by plugin versions, DLL hashes, or MVIDs for a conforming v1 provider.

## Provider type

The provider is `AAVideoExport.dll`. Its complete public type name is `AAVideoExport.Integration.RenderControlV1`. The type is a public static class with these members:

```csharp
int ProtocolVersion { get; }       // must be 1
bool IsReady { get; }              // lifecycle and restore state are coordinated
bool IsRenderingOwned { get; }     // export, panel, capture, or cleanup owns rendering
event System.Action BeforeAcquire;
event System.Action AfterRelease;
```

Names, parameter types, return types, and meanings above are fixed for v1. Event types must remain `System.Action`; they must not be changed to `Il2CppSystem.Action`. Existing members must remain properties and events, not fields. Optional members may be added without changing v1. Semantic changes require a v2 type while v1 remains available.

## Handoff order

1. Before the first Unity-thread acquisition, the provider sets `IsRenderingOwned=true` and synchronously raises `BeforeAcquire`.
2. The consumer restores its owned frame-rate, VSync, on-demand interval, camera, and preview-texture state in that callback. The callback must not start another acquisition.
3. After all acquisition callbacks return successfully, the provider snapshots Unity state and applies its own state. A callback failure aborts acquisition; the provider must not snapshot a partial state.
4. Panel, preparation, capture, and restore stages may hold ownership in a nested manner. Intermediate stages must not publish `false` between nested holders.
5. After the final holder has restored global state, the provider sets `IsRenderingOwned=false` and then raises `AfterRelease`. The consumer starts observing idle state again and does not reuse pre-export idle time.
6. A failed restore must not publish a false handoff. The provider enters an uncoordinated state and consumers stop optimization until recovery or restart.

State queries must be lightweight and free of rendering side effects. Unity state writes and handoff events run on the Unity main thread; worker threads may handle file and encoding work only.

## Discovery and compatibility

Azurite locates the public type by its fully qualified name, validates the v1 shape, and caches delegates. If the type exists but its version or signature is incompatible, Azurite disables rendering scheduling conservatively. It must not pretend that unstable private members are a compatible fallback.

Older AAVideoExport builds without the public type may use the restricted legacy adapter only on hosts that explicitly verify that adapter. The legacy adapter is a compatibility path, not the v1 contract.

`IsReady=false` blocks new Azurite rendering optimization. A late-loaded consumer subscribes before reading state. If export already owns rendering, the consumer must not restore or overwrite export state. Disposal removes the same managed delegate instances that were subscribed.

## Versioning and limits

AAVideoExport updates should preserve the v1 type and handoff order. Changes to UI, encoding, or enhancement parameters do not require a new Azurite DLL hash. Public semantic changes require a new protocol major version with a compatibility layer.

The protocol coordinates ownership; it does not guarantee GPU utilization, GPU clocks, frame time, or export speed. Azurite does not set GPU clock frequency. Scrolling remains protected at normal cadence, including wheel input, inertia, and viewport movement.

The runtime consumer is implemented in `Plugin/Azurite/RenderControlClient.cs`; the provider implementation belongs to AAVideoExport and is intentionally not compiled into Azurite.

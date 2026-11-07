# Wheel tuning regression tests

```powershell
dotnet run --project WheelTuningTests/WheelTuningTests.csproj -c Release
```

Compiles the production `EditorScrollTuning.cs` directly; `WheelSource` can point
to an earlier copy. The harness exercises actual discovery, lease ownership,
native baseline capture, periodic updates, and restoration with small host
stubs. Its reflection adapter also runs against the older overload lacking a
global scale. The initial twelve tests passed 4/12 against the old source and
12/12 against the extension; reset-to-native coverage was added afterward.

The final `Update` overload accepts optional `userScale = 1f`. It normalizes
per-area values first, applies the global 0.5–2 scale afterward, and detects
changes immediately rather than waiting for the normal one-second discovery
interval. The combined factor is deliberately not clamped to the old per-area
maximum. Direction and external-writer ownership remain independent of this
user preference. Background categories retain their native base factor, but
participate in explicit global scaling.

These tests verify factor application and rollback; they do not measure Unity
frame time or claim a wheel speed setting fixes expensive rendering.

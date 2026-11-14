# Deferred selector construction toggle

```ini
[Large Projects]
DeferEditorSelectors = false
```

This independent switch defaults to **false**. Opting in may defer unused
character, background and popup-image selector construction until first demand.
First-open construction remains the original synchronous native operation; this
does not introduce incremental loading, background threads or a performance claim.

The existing host whitelist is unchanged. Installation still requires a verified
host with `EditorPatches` and without `LegacyExporter`. The current AA 1.0.1 profile
does not grant `EditorPatches`, so setting this key to true does not enable selector
deferral on that profile. Unsupported and legacy-exporter profiles remain refused.

The configured initial value and current global/host/export gates are applied
before the first selector hook can defer. Every existing Driver Tick first pauses
new deferral, then reads the current setting after the existing configuration
reload point and a fresh healthy host probe. Global `Adaptive Rendering/Enabled`
off, session disable, an unhealthy/unsupported probe, export activity or the export
hold prevents new deferrals. Both synchronous export acquisition and late public
export ownership pause the feature immediately. The controller reference remains
published during installation so reentrant export callbacks can pause it.

Turning the option off or pausing it **does not flush or discard pending work**.
Previously deferred character/background/popup objects still finish exactly once
when their original native Init/Show demand arrives. New objects use native
startup while paused. Existing scene-destruction, terminal-shutdown and live-unload
behavior remains owned by the unchanged controller.

Live ConfigEntry changes take effect on the next safe Tick. File reload behavior
is unchanged: the existing `Diagnostics/ProfileEditor` path checks the config
file every two seconds when that diagnostic option is enabled. This change adds
no watcher, worker-thread callback or new reload mechanism and does not modify any
other optimization defaults.

SOURCE validation covers the actual policy/controller with bounded dependency
stubs, C-copied host metadata and compiled plugin wiring. Real BepInEx persistence,
native startup/demand, UI correctness, and the required cold/warm small/medium/large
project measurements remain pending. The switch stays default-off until those
separate acceptance gates are completed.

# Deferred selector regression

`dotnet run --project SelectorTests/SelectorTests.csproj -c Release -p:AAInstallPath=C:\AA-reference-mirror -- C:\AA-reference-mirror <fresh-plugin-dll> <repository-root>`

Checks the C-copied AA method signatures and compiles the actual
`DeferredEditorSelectors` against a deterministic Harmony dispatcher and native
callback stubs. Tests preserve background/popup deferral and cover the character
Awake scope, singleton/hide/Start behavior, matching-list-only suppression,
selection-before-Show readiness, failure/retry, imports/alias reloads, configuration
changes, scene teardown and live plugin unload. The previous independent
`SelectorState` model has been removed.

Historical character deferral tests ran against their prior production source:
27 assertions passed and 6 failed; the controller-only suite then passed 35 assertions.
It does not launch Unity or claim measured entry savings. The first character
selector opening still runs AA's native list population synchronously.

The selector-toggle checks also link the actual `DeferredSelectorPolicy`,
`HostProfile` and snapshot source. A narrow ConfigFile/Value dependency stub
exercises the real binder and policy; it does not model BepInEx disk persistence.
The Harmony dispatcher exercises the actual controller, including a callback at
the first installed hook, default off/on/off, all three pending selector families,
global/session/export/probe holds, unchanged unsupported/legacy/AA1.0.1 refusal,
scene destruction and live unload. Mono.Cecil inspects the explicitly supplied
fresh plugin for Load, initial-before-hook, Tick, reload, export and late-yield
wiring. If the DLL argument is omitted, the normal Host suite's just-built
`Plugin/bin/Release/netcoreapp6.0/Azurite.dll` is used; missing outputs fail.

Initial toggle RED against the real 0d9 DLL: 35 passed / 8 failed. No native
startup, UI responsiveness, asset timings or saved-settings acceptance is inferred.
The completed toggle suite passes 71 assertions against the freshly built plugin;
the same final tests reject the old 0d9 plugin with 60 passed / 9 failed.

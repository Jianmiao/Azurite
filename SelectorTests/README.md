# Deferred selector regression

`dotnet run --project SelectorTests/SelectorTests.csproj -c Release -p:AAInstallPath=F:\AzureArchive_100_fix`

Checks the installed AA method signatures and compiles the actual
`DeferredEditorSelectors` against a deterministic Harmony dispatcher and native
callback stubs. Tests preserve background/popup deferral and cover the character
Awake scope, singleton/hide/Start behavior, matching-list-only suppression,
selection-before-Show readiness, failure/retry, imports/alias reloads, configuration
changes, scene teardown and live plugin unload. The previous independent
`SelectorState` model has been removed.

Character deferral tests were run against the prior production source first:
27 assertions passed and 6 failed. The complete updated suite passes 35 assertions.
It does not launch Unity or claim measured entry savings. The first character
selector opening still runs AA's native list population synchronously.

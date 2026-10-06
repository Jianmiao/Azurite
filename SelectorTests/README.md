# Deferred selector regression

`dotnet run --project SelectorTests/SelectorTests.csproj -c Release -p:AAInstallPath=F:\AzureArchive_100_fix`

Checks the installed AA method signatures used by `DeferredEditorSelectors` and
exercises its state-machine invariants as a small deterministic fixture. It does
not launch Unity or claim first-open selector latency; the first gallery opening
still runs AA's native population synchronously.

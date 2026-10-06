# Progressive preload source-linked tests

Run:

```powershell
dotnet run --project ProgressiveTests/ProgressiveTests.csproj -c Release
```

These tests exercise the production editor gate, cooperative budget and managed
wrapper with fake `IEnumerator` instances. They cover early structure/dialogue
unlock, resource-dependent preview gating, export handoff, failure/cancel
deadlock boundaries, frame budget waits, the four-resource in-flight cap,
nested enumerators, completion accounting and cancellation disposal. They do
not execute Unity, IL2CPP, Harmony, AA or AAVideoExport; the real host
state-machine order still requires a disposable matching-host test.

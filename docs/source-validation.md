# Source validation

Run the portable suite from any checkout location:

```powershell
pwsh -NoProfile -File tools/Test-Source.ps1 -Suite Portable
pwsh -NoProfile -File tests/Test-SourcePlan.ps1
```

It builds Core and runs 19 source-linked or pure-model test projects, including wheel-control, single-speaker mutation and host authoring admission behavior. It does not launch AA or validate rendered UI, native callbacks, saving or actual performance.

External integration checks require explicit, user-supplied dependencies:

```powershell
pwsh -NoProfile -File tools/Test-Source.ps1 -Suite Host -AAInstallPath '<AA installation>' -AaveSourceRoot '<AAVideoExport source checkout>'
```

The host suite builds the plugin, checks generated host bindings with Driver/Selector/LoadingIntegration/Interop tests and runs CpuTests against the supplied production RenderControlV1 source. No computer-specific fallback is used. The AA game and generated bindings are not redistributed. Run `-PlanOnly` to inspect exact jobs without creating artifacts or starting processes. Every actual run gets fresh logs and an aggregate result under `artifacts/source-validation-*`; failures are never silently skipped.

The retained `VirtualizationTests/InspectNative.py` and historical offline compiler scripts are research/archive helpers, not portable suite entry points. Their older hardcoded paths do not define this workflow.

# 1.0.3 candidate: project entry and row initialization

The latest user-run log (2026-10-06 00:39) proves that the authoring-probe-2 candidate unpatched the entire progressive module after requesting the nonexistent StudioCommon.Load(). This error occurred before its probes could observe any load. Existing pure-state tests did not execute Install and therefore did not catch this bug.

Current native evidence identifies CatalogFileInfo's project button callback -> AuthoringWorkbench.OpenProject -> AuthoringWorkbench._Load_d__47.MoveNext. The preload yield is state 1; scene loading is state 2. The candidate now hooks only this verified interactive project state machine. CoLoadSave is appreciation playback and is deliberately excluded. StudioCommon.Load(ProjectData) is not needed as a probe; Start completion is used for the editor-ready boundary.

Before letting the parent continue, ScriptData resource fields and character names are copied into a separate native collection and assigned only to the unstarted preload iterator. Runtime project structures are not changed. The background coroutine begins two frames after StudioCommon.Start. Resource-dependent native inspector methods have actual prefixes, while text/list operations remain native. On completion only current property state and preview are refreshed; historical actions are not replayed.

The 4ms budget is cooperative: an indivisible native resource step can exceed it. The first resource schedule enumeration is not preempted. Project scene creation also retains its native cost. No fixed latency or GPU savings are claimed.

Dialogue virtualization previously rejected a valid prefab because child is filled by ScriptListItem.Init, not object creation. The test fixture now starts child as null, matching native initialization. Twelve tests failed before production repair; 26/26 pass after initialization-order repair. Native add/delete data logic remains unchanged.

Verification on the current local host:

- LoadingIntegrationTests: old production 0/10; new production 17/17, including 379 exact host metadata signature comparisons, resource snapshots, native preview deferral/replay, cancellation, resource-wrapper cleanup, export suspend/resume, and disabling new admissions while an owned preload finishes.
- VirtualizationTests: 26/26.
- Fifteen source-linked suites: pass, with the simulation/engine boundary stated in each test output.
- Generated host binding check: 392 type references, 1343 member references.
- Actual compiled RenderControlClient against installed AAVE 0.2.4 RenderControlV1: 11/11 lifecycle cases; no private provider fields or hashes used for compatibility.
- Plugin build: zero warnings/errors.

These checks do not execute Unity or validate live IL2CPP hook dispatch. Runtime acceptance requires phase=deferred -> phase=editor-unlocked with originalPreloadStarted=False -> phase=resource-loading with editorUnlocked=True -> phase=resources-ready. It also requires real text/add/delete/scroll checks while loading. Do not substitute hooks-ready or a green stub test for this acceptance.

Original detailed native evidence and the preserved run log are in E:/aamod/diagnostics/Azurite-1.0.2. This is a local candidate, not a GitHub release.

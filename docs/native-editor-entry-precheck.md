# Normal Editor entry precheck — 2026-11-08

This is static preparation for the approved normal-Editor acceptance gate. No AA process, account logon, project load, instrumentation plugin or runtime acceptance was performed.

The current catalog Edit callback `CatalogFileInfo.__c__DisplayClass18_0._InitProject_b__0` (token 100666038, RVA `0x9c5840`) tail-jumps to `AzureArchive.Automation.AuthoringWorkbench.OpenProject(string sourcePath)` at `0x9c5846`. Current interop exposes OpenProject as public static void; its native token is 100695341 and RVA is `0x951670`.

That genuine host path canonicalizes the project path, calls `PrepareOpen(path, false)`, performs the native project handoff, loads local resource overrides, preloads project assets and changes to `ScenarioEditorScene`. The normal `StudioCommon.Start` lifecycle then owns session initialization. A future harness must not fabricate a session, call Begin manually, assign PersistentData or invoke Studio lifecycle methods itself.

The narrow proposed seam is a one-shot main-thread observer after genuine `Catalog.Start` completion and native readiness. Before OpenProject, require CLI inactive, settings/resources ready, the real catalog and workbench, no Current or control-opening operation, and the native catalog-idle guard. OpenProject returns void and catches failures into a native error path; successful return is not successful loading.

After asynchronous loading and genuine StudioCommon.Start, independently require the expected scene, the real Current/Studio owner, copied canonical fixture path, matching project/node/row persistent IDs and snapshots, edit-session/revision/catalog identity, and settled loading/save/apply/export state. Failure must preserve evidence without retries or manual initialization.

Current static startup branches skip the creator splash and startup BGM in batch mode. They do not establish successful headless resource loading, later audio isolation or Editor viability. Normal startup also creates native control-pipe/discovery state and loads settings/resources. `OpenControlled` additionally starts MCP. None of those side effects is covered by the resource-free capabilities/schema query contract.

Before implementing a normal-entry harness, separately verify the copied fixture/resource closure, target-account settings/cache/discovery paths, normal-lifecycle NoSound scope and the observer integration. First prove catalog-only startup; then copied-project entry and End cleanup; only then test native edits, Undo/Redo and save/reopen. No visible-window or global-input fallback is allowed.

Local detailed evidence: `.superpowers/sdd/azurite-authoring-20261108/editor-entry-next-gate.md` and `artifacts/editor-entry-static/`. The static read covered 45 selected native methods and six current interop types; 419 token declarations matched the existing mapping. Input hashes were unchanged:

| Input | SHA-256 |
| --- | --- |
| GameAssembly.dll | `0776516c59811c724c450e205930546c5d102975f279e33c46c96b878fa06c8b` |
| Assembly-CSharp interop | `d95062f3a49fba645986dc7fe0cee749197c9d0c84ff381fc3ca2d9d2823c013` |
| global-metadata.dat | `b4aeee165e7e10e0f46f85e095fd94487fdb041215f6dcb7ae9de5d28a364f43` |

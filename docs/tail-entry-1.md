# Local 1.0.6: resume writing at the tail and avoid needless row rebuilds

## User evidence

The 1.0.5 run confirms the overlay fix: editor-entered reports overlayActive=False, loadingTags=0, ownTag=False, sceneInstanceChanged=True. The user can enter script. Remaining reports are a long project-entry freeze, a script-entry pause, and opening at the first dialogue instead of the last.

The same runtime log records:

- Deferred-resource capture to editor-entered: 15674.4ms, nativeTasksStarted=0.
- PrepareOpen: 282.238ms, including file read274.050ms, byte reads25.534ms and payload parse244.391ms (inclusive nested timings).
- StudioCommon.Load: 20.794ms. ScriptNodeInspector.Load: 212.129ms.
- Three generic dense fallback events, with 713 row Init calls in one summary window and 1283 in the next, despite only 640 logical rows.
- A final contiguous block of 629 LocalizedUILabel exceptions immediately before shutdown's dense restoration log; the last pool had 11 real rows out of 640. These exceptions occur after the interactive session and must not be presented as the measured initial-load cause.

## Row presentation corrections

The previous implementation filled the top viewport bottom-to-top. It did not open at the tail. The entry scope now pins and positions the last row and lets native Load perform its own selected-row restore while loading=true; the model order stays unchanged. Incremental visible fill continues toward earlier dialogue.

Audited native Load/Unload lifecycle paths do not enumerate the sparse row cache before rebuilding/after deselecting, so leaving a node can release only its bounded presentation pool. It no longer fills every missing row merely to destroy them on the next load.

AuthoringEditorSession Capture, Save, BeginSave, CheckSave, CompleteSave and ReleaseSave read/modify model and save-session state. Their audited CaptureProject chain clones ScriptNode.scripts, not ScriptListItem objects. Exclude those exact verified signatures from the broad reflection-based dense guard. Keep Apply/Restore, history, reordering, live disable and export restoration guarded. Include actual method identity when a remaining guard materializes the list.

Application quit uses an explicit driver OnApplicationQuit callback to abandon row ownership without Init/Refresh or dereferencing native UI objects. Destroyed still performs regular disposal; live tick failures still restore and are never mistaken for application quit.

## Project-entry gap remains under diagnosis

Native Workbench already calls SceneManager.LoadSceneAsync(string) with mustCompleteNextFrame=false. Changing it to another asynchronous overload is not an evidenced optimization. The unmeasured gap includes engine load/activation and Studio.Start's AuthoringEditorSession.Begin, which initializes a resource catalog, computes fingerprints and captures project state.

Add six low-frequency operation counters: scene-load-request, studio-start, editor-session-begin, catalog-bind-project, catalog-capture-metadata, catalog-ensure-native. After Begin, read the existing lastCaptureMetrics field without invoking CatalogVersion/capture. This differentiates Unity scene work from cold catalog initialization on the next user run.

Do not bypass CatalogVersion or defer Begin merely to hide the wait: catalog versions participate in revision/save validation and Begin transfers file-lease ownership. Do not send native Unity/IL2CPP state to a managed worker. The 15-second stall is not claimed fixed by this candidate.

## Verification boundaries

Source-linked row tests reproduce missing tail selection and unnecessary 640-row lifecycle rebuild before the changes. Additional regressions exercise sparse model-only capture/save, legitimate dense consumers, native deselection order, tail visibility and terminal shutdown without UI initialization. Driver tests invoke the actual production message handlers with a simulated Unity host and check real installed profiler signatures. Native binding and built-binary RenderControlV1 checks run separately.

No AA UI was launched or manipulated by the agent for this candidate. Runtime position, perceived latency, hooks and project preservation remain user acceptance items. Evidence is in E:/aamod/diagnostics/Azurite-1.0.5-user-run and final verification in E:/aamod/diagnostics/Azurite-1.0.6-tail-entry.

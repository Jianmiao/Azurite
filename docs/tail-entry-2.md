# Local 1.0.7: stable tail anchoring and deferred selector galleries

## Runtime evidence used

The 1.0.6 run loaded the active candidate and reached `dialogue-open ... tail=639`, but later viewport probes showed `firstVisibleIndex=0`. The target index was recorded, yet the visible position had returned to the first rows. Resource readiness and the loading overlay were healthy; `ErrorLog.log` was empty.

Native `CenterableUIScrollView.CenterOn(Transform,bool,bool)` allocates `Utils.Util._CoExecuteNextFrame_d__12` and schedules the private centering operation for a later frame. The previous implementation immediately refreshed the sparse window before that callback and then allowed the callback to overwrite the logical position. That was a real ordering bug, not a missing data selection.

## Tail fix

During native `ScriptNodeInspector.Load`, Azurite writes the last logical index before AA reads its saved selection, binds only required geometry/tail rows, and suppresses the transient CenterOn call for virtual large lists. After Load returns it clears stale centering state, publishes the full mathematical list bounds and sets normalized vertical drag to the logical bottom. The next frame then computes the current viewport and fills it from bottom to top. For small native lists, AA's own CenterOn remains enabled and the same bottom anchor is applied through native bounds.

No second pre-anchor window refresh remains. The model and save order stay unchanged; normal CenterOn behavior resumes after the open scope.

## Deferred galleries

On the verified host, BackgroundExplorer.Start and PopupImageExplorer.Start enumerate and instantiate their complete galleries before hiding their own objects. Background/Popup Init only stores references, and inspector OpenBackgroundSelector/OpenPopupImageSelector calls Init before Show. Azurite now suppresses editor-scene Start while preserving hidden state, then runs the original Start once on first demand before allowing Init/Show. The first gallery open retains the native construction cost. This optimization reduces project-entry work only when those selectors are not used.

All native selector work stays on Unity's main thread. A failed first demand remains suppressed and can be retried; application quit drops references without touching torn-down UI. Live disable restores pending native galleries before unpatching.

## Verification

VirtualizationTests: 42/42, including not-ready CenterOn, tail selection, deferred bottom anchor, descending visible fill, rapid scroll, mutation, dense fallback, and shutdown. SelectorTests: 14/14 installed metadata and deterministic demand state. LoadingIntegrationTests: 31/31. DriverTests: 10/10. Plugin Release build: 0 warnings, 0 errors. Interop and AAVE 0.2.4 RenderControlV1 binary checks remain required before installation.

These are source-linked simulated Unity/IL2CPP tests plus installed host signature checks. The 7.249-second project-entry runtime wait remains measured rather than claimed eliminated; the 1.0.6 catalog report is preserved under `E:/aamod/diagnostics/Azurite-1.0.6-user-run`.

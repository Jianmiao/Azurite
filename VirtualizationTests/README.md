# Dialogue virtualization native contract and tests

This is an experimental presentation replacement for AA Fix's fixed-height script list. It does not change the `ScriptNode.scripts` data structure, asynchronous execution, file format or undo records. Enable only for the separately verified host profile. It is intentionally **not** a general compatibility contract for another mod that directly enumerates `scriptNodeListItemsCache`.

`DialogueVirtualization.cs` is compiled directly into this harness. The fixture drives the production Harmony callbacks around the verified native ordering. It asserts actual row creations, refresh counts, logical binding, selected object identity and dense fallback boundaries. It does not run Unity, IL2CPP detours or validate rendered images; passing results do not establish elapsed-time or GPU gains.

Run:

```powershell
dotnet run --project VirtualizationTests/VirtualizationTests.csproj -c Release
```

`InspectNative.py` reads the installed `GameAssembly.dll` without starting AA. It resolves the actual Assembly-CSharp code-registration method-pointer table, uses the generated interop method tokens, and writes disassembly with target names to `native.txt`. `native-grid-evidence.json` independently decodes serialized level2 inspector 1979's grid reference (path 1526). The actual grid is Vertical, Top pivot, 175 unit pitch, unlimited rows per column and no smooth/fade animation. No old RVA is used as a runtime patch address.

## Audited access paths

| Native path | Evidence | Treatment |
|---|---|---|
| `ScriptNodeInspector.SyncScriptList` | token 100689953, inlines Init, destroys all children, appends N rows, Reposition, selection or FirstOnTop callback | Replaced with 2-row native geometry calibration at entry, then bounded visible pool. Node name, deselection and requested selection retained. |
| `InsertScript(int)` | 100689963, native model insertion/history/LinkScripts, Sync, direct `cache[index].Select()` | Original executes unchanged; prefix pins required new logical index across Sync. |
| `DeleteScript()` | 100689964, native history/resource cleanup/remove/LinkScripts, Sync, `cache[index-1].Select()` | Original executes unchanged; prefix records previous-index target before deselection. |
| `Load` | 100689939, invokes Sync and **inlines** RestoreStatus cache lookup, re-reading inspectorInfo after Sync | In the original loading scope, Sync pins the final model row, writes `lastScriptIndex`, and instantly shows it. Native Load performs exactly one selection. Previous pool is released after native deselection; no dense rebuild. |
| `RestoreStatus` overloads | 100689940, 100689941, direct indexed cache read | Prefix materializes the target row without rebuilding every row. |
| `OnChildSelect`, `OnChildDeselect`, `DeselectAllChildren` | 100689969, 100689973, 100689974 | Work on the selected object only. Selected object stays pinned even outside viewport; it is never rebound by scrolling. |
| `SyncScriptText`, `SyncScriptName`, `SyncSlots`, `SyncEnvironmentProperties`, `SyncAll`, `SetCharacter`, `ApplyScriptText`, `PlayPreview` | 100689954-961, 100689970-971 | Access model and current selected row, not the full sparse cache. Original runs with real bound selected row. |
| Row click/insert/context properties | 100689922, 100689929 and selection methods | Visible rows carry actual model index; callbacks keep native behavior. |
| Row drag / inspector rearrange / update cache | 100689926-928, 100689977-979, 100689984-985 | Materialize dense list before drag or rearrangement. Existing dragged/selected row object survives. Native reflow/history then runs. |
| Scenario Add/Delete/InspectorModify/StateChange/Rearrange Undo and Redo | 100690644-645, 100690649-650, 100690653-654, 100690661-662 plus named methods | Entry guard materializes all rows; nested Sync stays native for the entire scope. |
| `AuthoringEditorSession` Capture/Save/BeginSave/CheckSave/CompleteSave/ReleaseSave | 100694589, 100694597-601; audited exact parameter signatures | Work from model snapshots and files, so they keep sparse presentation. Apply/Restore and unknown matching authoring methods retain dense guards. |
| `Unload` | 100689942: MaintainDummyForNexts, Deselect, RequestActive(null); no cache iteration | Original performs model/history lifecycle work, then the pooled presentation is discarded without constructing missing rows. |
| Export collaboration Suspend, live disable/disposal | named entrypoints | Dense cache is restored before leaving ownership. No rendering ownership protocol is changed. |
| Application quit | explicit `AbandonForShutdown` invoked by owner before disposal | Drops managed scheduling references only; no Init/Refresh or access to possibly destroyed localization/UI services. |
| `UIGrid.Reposition` | 100663664, actual serialized fixed layout | While virtual, repositions only bound rows by absolute logical index. Native grid animation is not used. |
| `UIScrollView.bounds` | virtual accessor 100663882 | Entire logical height is represented mathematically; actual widgets exist only for visible/overscan/pinned rows. |

After a dense fallback, a later allowed stable update re-enters virtualization. Dragging, loading, unloading and rearrangement postpone that transition. Reordering and undo therefore retain their native cost at the operation boundary. Unknown direct native/third-party cache readers remain an experimental compatibility limitation.

Initial allocation and text layout are bounded by the visible region and three overscan rows on each side, with a hard maximum of 128 real row objects. Native cache length still equals model count, using null offscreen slots; its pointer array is O(N), but expensive GameObjects/labels are O(visible). This is actual virtualization, not hiding N pre-created rows. The first row remains pinned because native FirstOnTop uses physical geometry. Selection, insertion/deletion destination and restored selection are additional pins.

## Incremental bottom-to-top presentation

Opening a script now selects and shows its final dialogue for continued writing. After the two native geometry-calibration rows and the immediately pinned last row, the post-Load anchor clears any deferred native center target and sets the audited vertical scroll to its normalized logical bottom. The next frame binds remaining visible rows from the bottom of the **current viewport** toward its top. Overscan binds afterward. Logical indices, script data, physical bound-child order, and the full scroll extent keep their original order. Small scripts below the virtualization threshold also open at their last dialogue but retain AA's native row construction. Empty scripts retain no selection.

All Update, grid and scroll hooks share a Unity-frame budget of at most two deferred row operations or four milliseconds of row work. This is cooperative: an individual native Init cannot be interrupted after four milliseconds, and the two calibration rows plus required native cache-read pins are synchronous. Selected, inserted, deletion-destination, restored and first-geometry rows never wait for this budget. Unfilled rows remain empty spaces in the full logical scroll area; the populated rows and the native editor remain usable.

Every refresh recomputes the current viewport, so scrolling midway drops obsolete work without growing a queue. Init/Refresh notifications cannot recursively spend another batch. Dense fallback refreshes any pending text before save/history/unknown cache consumers resume. The harness now advances actual fake Unity frame counts between pending work, exercises the linked production hook implementation, and checks descending fill order, shared per-frame limits, rapid viewport changes, edits/selection/restore during fill, nested callbacks, dense fallback, and unload.

`dialogue-open totalMs=... rowSyncMs=... tail=... live=... created=... pending=... success=...` distinguishes row-presentation work from the rest of native inspector Load. Dense fallback diagnostics include the actual native type and method. These measured host-hook durations still need runtime observation; the standalone fixture does not establish user-perceived timings.

Production fault handling returns to a dense list and disables the experiment after an exception. Sparse-state materialization must finish before incompatible native code is permitted to proceed. A failure to materialize is reported and must not be treated as a successful export handoff.

## Required user runtime checks

Verify virtual-mode diagnostics on the actual host, scroll top/middle/end, select then scroll selection out of view, add/delete at boundaries, edit text/character, undo/redo, drag reorder, resize, save/reopen and export/cancel. Check both row content and persisted project order. Use a disposable copy for first-run verification. Do not infer success from the standalone harness alone.

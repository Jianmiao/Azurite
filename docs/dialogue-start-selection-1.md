# Dialogue selection after early inspector activation

Candidate: 1.0.16, dialogue-start-selection-1.

## Evidence

User 1.0.15 log confirms the selected package, active sparse list, normally completing resource queue, and no selection exception. Screenshots show two gold rows and a selected row with the pane asking to select a dialogue. The exact runtime Load/Start order was not instrumented in that version.

Native evidence in VirtualizationTests/native.txt:
- ScriptNodeInspector.Start, token100689938, RVA00802e7f clears [this+0xb8] selectedScriptItem, without clearing any row's selected flag.
- Selectable.Select, token100689996, returns false if already selected.
- DeleteScript, token100689964, returns when selectedScriptItem is null.
- DeselectAllChildren, token100689974, only touches selectedScriptItem.
- OnChildSelect, token100689969, sets owner and pane, runs SyncAll then PlayPreview.
- SyncAll uses loading to suppress history while retaining state synchronization.

A Load-before-Start source-linked replay with the original production code reproduces owner loss, delete no-op, two highlights after selecting a neighbor, and ignored click on the old tail. The production patch makes this same replay pass. It also covers native small lists since the initialization order does not depend on pool size.

## Scope

Start prefix snapshots exact row/node/data/index/cache identity. Postfix restores only after successful Start and only if those bindings still agree, the row is active and selected, and no newer owner exists. Native OnChildSelect restores pane and preview; temporary loading=true suppresses duplicate history and is restored in finally. Exceptions are logged without constructing a dense list. No model data, resource scheduling, or AAVE contract changes.

Diagnostics: E:/aamod/diagnostics/Azurite-1.0.16-selection. Baseline.csproj uses the saved pre-fix production file with the current regression fixtures; red-tests.log and green-tests.log retain both outcomes. Tests are a simulated host call chain, not live Unity validation. A one-time runtime message records whether the delayed Start repair actually executes.

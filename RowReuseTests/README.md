# Dialogue row reuse contract tests

Run `dotnet run --project RowReuseTests/RowReuseTests.csproj -c Release`.

This executable links the production `DialogueRowReuse.cs` and identity planner
against a narrow host stub. The hook runner dispatches the production prefixes,
postfixes and finalizers. It does not claim to emulate Harmony's IL2CPP patch
installation or Unity's renderer. Installation and visible performance still
require validation in the verified AA Fix host.

The host call order comes from the captured native disassembly
`E:/aamod/diagnostics/Azurite-list-native-20261004/list-native.txt`:

- `InsertScript` (`008004a0`) delegates to indexed insertion (`00800580`).
- Insert/Delete mutate Script data, invoke `SyncScriptList` (`00803bd0`), then
  select the inserted row / previous row respectively. Delete at zero clears
  selection. The optimization does not replace these operations.
- Sync remembers the old selected index, deselects, calls
  `NGUITools.DestroyChildren`, clears the cache, loops through `AddChild`,
  initializes each row, calls `Refresh`, fills the cache, then runs native grid
  repositioning and optional selection callbacks.
- Fix **inlines Init** at `00803ed0`–`00803f44`, then calls `Refresh` at
  `00803f4e`. The suite also covers a non-inlined Init call. Suppressing the Init
  method alone would not bypass Fix's text layout; suppressing all Refresh calls
  would risk stale text, speaker names and host state.

Assertions cover identity and order, data/text/name preservation, original
selection behavior, one sibling move on insertion and zero on deletion,
boundary/nesting guards, hook-installation rollback, fault recovery and a
deterministic sequence of 120 edits. Healthy insertion into 64 existing rows
constructs only one new row, and healthy deletion destroys only one row. Native
Refresh and the final grid reposition still process the entire list; these are
call-count guarantees, **not** measured latency or a full virtualization claim.

Bounded diagnostics report top-level mutation `attempts`, successful `captured`
snapshots, `eligible` single-edit plans, validated `commits`, fallback events and
`lastRejectedReason`. Reasons distinguish scene/activity, parent/index/geometry
barriers and Sync identity/plan mismatches. Reports occur at most every five
seconds when work occurred; rows never allocate diagnostic strings individually.

An early red test showed 65 Refresh calls through Fix's inlined Init, confirming
that skipping the managed Init entry point would not remove per-row layout.
The final candidate deliberately preserves Refresh and limits its promise to
avoiding unnecessary row destruction/instantiation.

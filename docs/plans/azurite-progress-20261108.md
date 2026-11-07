# Azurite staged implementation — approved plan

## Authority and constraints

The user approved implementation on 2026-11-08, in order: stable baseline, authoring, performance. This file records that approved scope; runtime acceptance is not inferred from offline tests.

- All AA runs must be in a full isolated installation under a dedicated low-privilege Windows account, with independent Profile, LocalLow, HKCU and resource/workspace state.
- Strictly headless: no visible windows, no focus capture, no system keyboard/mouse injection. One AA process at a time; do not launch when daily AA is running. Stop only owned processes on timeout.
- Use `-batchmode -nographics` for compatible logic tests and `-batchmode` with graphics for offscreen rendering. Hidden-window flags alone are insufficient. Keep audible output off daily devices without changing internal audio samples.
- Each run has a unique ID, fresh logs, bounded timeout, low priority and explicit results. High-load measurements run only while the foreground is idle and stop/pause on activity.
- Preserve RenderControlV1/v1. Do not overwrite published artifacts, the current mixed 1.0.39 installation, or original development directories.
- Five card families must all work before card-pack delivery: transition, combat, action, language, sound. Language means supplied story-dialogue language variants, never editor UI locale; initial examples are zh-CN/en and no automatic translation.
- Maximum batch size is 1024 operations; successful native transaction is the only successful mutation result. Stale selection/project/catalog, save/export activity and missing resources reject before mutation.

## Task 1 — headless environment and recovery

Inventory and hash installed Azurite, active Profile, configuration and user-level state; make a verified recovery copy. Prepare a full isolated AA copy and fixture workspace, with no links to daily writable state. Provide reviewed administrator-only account preparation and an unprivileged headless launcher. Current controller is not elevated and AzuriteTest does not exist: do not launch AA until this prerequisite is fulfilled. A secure credential must never enter source, command arguments, logs or chat. Prove account identity and data directories with a harmless child before AA startup. Validate CLI/schema/resource availability separately from ordinary Editor runtime and Mod lifecycle. Offscreen graphics requires nonempty captured frames; pure logic success is not rendering success. A missing headless/editor capability blocks only the dependent acceptance work and must not trigger visible fallback.

## Task 2 — stable code, tests and packaging

Start from v1.0.38 / eb43469b143de87f4cef363fb53aa72dacef3508 in an independent branch/worktree. Import the wheel-caption repair and production-source-linked control tests. Repair single-selection Alt+1..5 using verified native mutation-before-history order, then test real model change, one Undo/Redo, no-op/error paths, editor/focus/drag/playback/export protections. Keep unverified batch mutation out of the stable candidate. Preserve original Core (SHA256 76A46C4A896DFBDFFD66C2BE85E9A0E07D2D0523FAAEF0841763D997C65DF1A5). Unify all version identities, README, file checksums and source provenance. Use a new unused candidate version starting at 1.0.40. Repair portable-test external path configuration and separate portable vs host-required checks. Produce source/package hashes and a recovery procedure; do not claim runtime acceptance while Task 1 is blocked.

## Task 3 — native transaction and authoring

First prove ordinary headless Editor Studio/session/project/node/row identity and revision/snapshot synchronization following real native edits and reopen. Establish the actual current-host operation JSON/schema rather than guessing it. Use the proven native whole-snapshot Apply path only when it owns the active project. If Current is null, investigate lifecycle; do not manufacture a session or replace atomicity with sequential mutations. Import selection, batch toolbar and native adapter in separately tested increments. Cover speaker, exact text replacement, duplicate, delete and multiline insertion, with one native undo unit, save/reload and unchanged model/history on failure. Separate authoring capability from deep-render/EditorPatches switches.

## Task 4 — complete five-family card packs

Build selection/parameters/resources/preview/explicit-confirm UI and native insertion for all five families. Transition and sound use real catalog resources; combat has visible hit/reaction choreography plus audio; action applies a supported action to an explicit character slot; language inserts the explicitly selected supplied text variant. Provide at least one genuinely usable template per family. Unsupported durations/actions/resources must be explained before confirmation, never dropped. Extend the data schema for target slots and language content with explicit legacy migration; a language-only code without text is not a valid story-language card. Keep card content data-only, without arbitrary scripts or file paths. Revalidate preview/project/catalog before confirm. Switch to rebuilt Core only at the first required authoring API integration, then compile/package/install/ABI-check the same identified Core artifact.

## Task 5 — performance in independent increments

Start with audited selector deferral and gallery request admission, then dialogue virtualization, progressive loading, text layout, preview ownership/textures and Bloom. Each has an explicit host whitelist and independent switch. Original RT release is last, after all consumers and export restoration are proven. Use fixed small/medium/5000-line fixtures, cold/warm cases separately, five paired on/off trials. Record loading/operation latency, P95/P99 frame time, CPU/GPU/memory. Do not enable by default when benefit is within repeat noise or another key metric has a repeatable >5% regression. No high-load measurements while the user is active.

## Test and delivery gates

Each coherent change has behavior regression evidence and independent review. Each candidate has matching identity/provenance, host plugin build, portable checks, package checksum and rollback verification. Native acceptance additionally covers editor switching, save/export guards, stale selections, repeated edits, Undo/Redo, save/reload, failure rollback, and AAVE acquire/cancel/fail/complete/repeat release. Each phase delivers traceable source commits, a unique candidate, evidence and recovery instructions; missing native gates remain explicitly incomplete. No visible-window behavior claims from offscreen/logic tests. No new GitHub release or daily-profile switch is implicit in preparing a local candidate.

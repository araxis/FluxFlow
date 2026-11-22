# Scheduler Schedule selector

## Change

The Sources plugin supplies a derived Schedule selector through the existing portable
`PropertyGroup` and `ProjectedField` contract. Studio renders projected fields through
the shared Property Grid before the group's ordinary options. No saved Schedule
property, new scheduling engine or component-specific Studio editor was introduced.

Interval, Cron and Once expose their relevant fields. Selecting another mode removes
incompatible fields in one history operation. Existing values for the chosen mode,
property spelling and unknown properties survive. Once requires an explicitly entered
At value; selecting it does not invent an execution time. Conflicting saved schedules
are left intact and visible until an explicit selection repairs them. Advanced JSON
remains available. Sources package version is 1.0.0-alpha.3.

## Verification

- Full Studio suite: 6,135 passing tests across .NET 8, 10 and 11. Existing build/analyzer
  warnings remain; no build errors.
- Final focused Scheduler/editor run: 84 passing tests across the three frameworks.
  Covers conditional fields, native five/six-field Cron validation, invalid drafts,
  mode switching, stale callbacks, offset spelling, conflicting saved configuration,
  unknown fields, no-op selections, atomic Undo/Redo and project reload.
- Scheduler runtime: 21 passing tests across the three frameworks. Includes positive
  intervals, one-off completion, cancellation of long waits, expired schedules, skipped
  missed ticks, UTC, Stockholm spring DST gap and autumn overlap, and explicit offsets.
- Runner and log/trace boundary checks: 63 passing tests across the three frameworks.
  Includes Interval and Cron output, future/expired At, invalid runner time zones,
  Stop/rerun identities and retained outputs, repeated Stop and unchanged run requests.
- Published .NET 11 browser with an isolated plugin store and temporary project:
  installed Sources package loads; Schedule displays Interval/Cron/Once; dependent Cron
  validation works; switching hides unrelated fields; Undo restores the full original
  Cron settings; Redo and Once date entry work. Flat Property Grid layout inspected.

Time zone names continue to be resolved by the runner. The browser does not replace
runner-specific names or infer a zone for the user. DST behavior remains the existing
Cronos behavior. Runtime scheduling, Core and WFENG were not changed. Existing user
tabs, saved projects and plugin stores were not used for the browser trial.

## Follow-up review

Found one editor inconsistency: Advanced JSON also offered a Visual tab built from
the static option list, bypassing the Schedule selector and using five-field Cron
validation even for a saved six-field schedule. Group-based component editors now
keep visual editing in the Inspector and use a JSON-only advanced dialog, matching
the existing object-group editor pattern. No plugin contract or runtime change was
needed.

Added Scheduler to the shared component JSON regression matrix. Covers Save, Cancel,
unchanged saves, stale edits, component switching and disposal, with mixed-case keys,
unknown properties, six-field Cron and runner time zones preserved. Conflicting
schedules, invalid Cron and an empty Once date cannot be saved through Advanced JSON.

Verification on .NET 8, 10 and 11: 282 editor tests, 21 Scheduler runtime tests and 63
runner/log/trace tests passed. Existing analyzer warnings remain. Protected-source
comparison: 63 Core/WFENG files checked, zero changed. No browser restart or user
project edits were needed for this follow-up review.

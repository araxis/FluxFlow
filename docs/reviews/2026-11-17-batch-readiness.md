# Batch readiness verification

## Scope

Closed two verification gaps in the existing Batch component: its runtime suite did
not target .NET 11, and Studio Run lacked a focused test for partial batches on Stop
and subsequent reruns. No new settings, production editor or runtime behavior were
needed. Existing shared Property Grid and plugin registration APIs remain unchanged.

## Coverage

- Runtime suite now targets .NET 8, 10 and 11. Existing tests cover size/deadline
  competition, ordered delivery, original message references, error bypass, canceled
  timers, fault/cancellation and bounded broadcast behavior.
- Runner integration uses Scheduler → Source → Batch → identity Mapper. Five mixed
  JSON values form a full batch of three and a remainder of two. Graceful Stop delivers
  the remainder through the downstream component without waiting for the one-hour
  collection deadline. A separate scenario checks deadline-based partial batches.
- Complete member envelopes, order, null/object/array payloads and an integer above
  JavaScript's exact integer range are preserved. Outer batches retain independent
  identities. Stop is repeatable; reruns have fresh message identities and do not
  change observations retained from the preceding run.
- Batch participates in the shared Advanced JSON Save/Cancel, no-op, stale-edit,
  component-switch and disposal regression matrix. Mixed-case keys, duration spelling,
  string-encoded counts, unknown fields, wiring, project reload and Undo/Redo survive.
  Invalid sizes, overflow, invalid durations and invalid output capacities are rejected.
- Existing numeric/duration Property Grid contract tests and Transformation plugin
  preservation tests were included in the editor run.

## Results

All selected tests passed across .NET 8, 10 and 11:

| Suite | Total |
| --- | ---: |
| Batch runtime | 27 |
| Runner, log and trace boundaries | 54 |
| Editor and Transformation plugin regressions | 405 |
| Total | 486 |

An initial test compared raw JSON spellings and treated `+` and `\u002B` in a timestamp
as different data. It now compares parsed envelope values and separately checks integer
precision. No serialization change was needed. Existing Studio analyzer warnings remain.

No production defect was found in this pass. No application configuration, runtime,
Core or WFENG source changed; 63 protected files were checked with zero differences.
The run trial used in-memory temporary configurations. User tabs, saved projects and
plugin stores were not modified. This pass used rendered editor tests rather than a
new interactive browser trial.

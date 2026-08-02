# vNext System Events, Diagnostics, And Status

Date: 2026-07-17

## Status

The fifth bounded vNext milestone is implemented on local branch
`work/runtime-system-streams-vnext`. No push, tag, publication, pull request,
or merge was performed.

This milestone adds canonical runtime status, reliable system events, and
best-effort diagnostics. It does not add DI provider snapshots, runtime
revisions, resource activation, component-family migration, or MQTT redesign.

## Canonical System Streams

- `ApplicationPortRuntimeBuilder` now registers the exact reserved outputs
  `System.Events.Output` and `System.Diagnostics.Output`, each with finite
  capacity `256`, and exposes their exact compiler metadata through
  `SystemOutputs`.
- `ApplicationSystemEvent` is a workflow-friendly transport record with a
  stable name, category, subject, optional `FluxFlow.Data.FlowError`, and
  `FlowValue` details. Publication is ordered and backpressured, and accepted
  events drain during normal runtime completion.
- System events remain normal `FlowMessage<ApplicationSystemEvent>` values.
  Hosts may subscribe directly while compiled workflow links independently
  consume the same event from `System.Events.Output`.
- Link condition failures, rejected link targets, and unexpected component
  source or target faults become system events. A failed event subscriber is
  detached without stopping healthy subscribers or the runtime, including if
  it throws while receiving propagated completion or fault signals.

## Diagnostics And Instrumentation

- `ApplicationDiagnostic` carries stable kind/level/name fields, timing or
  measurement data, optional `FlowError`, and `FlowValue` attributes inside the
  normal message envelope.
- Diagnostics are bounded best effort: accepted records preserve order, while
  overflow and post-completion publication return `false` immediately.
- Input acceptance, output emission, request completion timing, delivery
  rejection, and system-event subscriber failure are reported without
  recursively diagnosing the diagnostics stream itself.
- Accepted diagnostics integrate with optional `ILogger`, `ActivitySource`,
  `Meter`, and `DiagnosticSource` surfaces. Host listeners/providers are
  isolated so their exceptions cannot fault workflow processing.

## Runtime Status And Isolation

- `ApplicationRuntimeStatus` snapshots runtime state plus each stable port's
  direction, payload type, capacity, pending count, active attachments, and
  availability. This remains a pull snapshot and does not create a State port.
- Runtime state moves through Active, Completing, Completed, Faulted, and
  Disposed. Direct input sends reject immediately after completion begins while
  internal output work drains.
- Unexpected component source or target failure detaches only that attachment,
  marks the stable port unavailable, and leaves the application runtime active.
- Signal payloads contain transport-safe errors instead of exception objects;
  the low-level bounded rejection stream remains available for local audit.

## Compatibility And Versioning

- `FluxFlow.Engine` moves from local `2.1.0` to additive `2.2.0`.
- Existing Engine definition/runtime behavior and established diagnostic
  contracts remain unchanged. The new surface is additive under
  `FluxFlow.Engine.Ports` and `FluxFlow.Engine.Signals`.
- Engine now directly references logging abstractions for optional standard
  logging integration. No provider implementation or service-provider
  ownership was added.
- The reviewed public source-declaration baseline changes only for Engine, from
  503 to 584 declarations. Deterministic JSON snapshots cover both new signal
  payload records.

## Verification

- Engine tests: 92 passed, including bounded delivery, ordering, cancellation,
  workflow-link routing, component fault isolation, lifecycle/status,
  recursion guards, standard .NET integrations, hostile listener isolation,
  and JSON contract snapshots.
- Composition tests: 116 passed.
- Composition.Hosting tests: 17 passed.
- Release convention tests: 93 passed.
- Complete Release solution test sweep: 1,911 tests passed across 63 projects
  with zero failures and zero warnings.
- Controlled Debug and Release builds: all 130 projects passed with zero
  warnings and zero errors.
- Changed-file formatting passed. Full-solution formatting continues to report
  two unrelated pre-existing whitespace findings in the Metrics and State
  composition test projects; those files were left untouched.
- Binary package compatibility passed against local Engine `2.1.0`; release
  preflight, archive/symbol inspection, local-source dry-run, feed-style restore,
  and the standard net8 package smoke test passed for Engine `2.2.0`.
- A separate package-only net8 consumer compiled and executed the new system
  event, diagnostic, status, and lifecycle APIs and printed
  `SYSTEM_SIGNALS_API_OK`.

## Next Gate

Implement keyed DI resource/provider snapshots as a separate bounded milestone.
Keep provider ownership explicit, publish immutable snapshots atomically, and
register stable Dataflow ports as keyed services. Do not combine that pass with
transactional runtime revisions, component migration, or MQTT redesign.

# vNext Projection Results

Date: 2026-07-19

## Status

The twenty-second bounded vNext milestone is implemented on local branch
`work/projections-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone preserves Projections as a typed domain component while making
snapshots and expected failures normal linkable results.

## Canonical Runtime

- `FlowEventProjectionNode` consumes typed `ProjectionEvent` messages and emits
  one `FlowResult<EventProjectionSnapshot>` Output plus Events.
- Matching snapshots use `snapshot`; configured final snapshots use
  `final-snapshot`; expected projection failures use `projection-failed` with
  stable `projection.failed` error codes and immutable details.
- Filtered events intentionally produce no result but increment observed count
  for the next matching or final snapshot.
- Ordered single-worker processing preserves observed/matched counts, latest
  event summaries, preview limits, event-time rolling rates, and output order.
- Normal completion drains accepted events before emitting exactly one
  configured final snapshot. It retains full lineage from the last matching
  event, and replayed event timestamps retain meaningful final rates.
- Missing/invalid event data is a normal error result and later accepted events
  continue. Unexpected Dataflow faults remain observable through Completion.

## Composition And Designer

- `RegisterEventProjection()` now owns the canonical fixed result contract. The
  descriptor keeps typed `ProjectionEvent` Input, one result Output, Events,
  and no universal Errors surface.
- A configured final snapshot is part of normal composition completion; no
  host-specific final-flush lifecycle hook is required.
- The optional clock remains host-owned and resolves from an exact resource
  address.
- Designer metadata reports `FlowResult<EventProjectionSnapshot>` Output while
  retaining existing option and clock-picker hints.
- Package examples use the flat `Resources` and `Workflows` application shape.

## Compatibility And Versioning

- `FluxFlow.Components.Projections` moves from `3.0.2` to `4.0.0`.
- `FluxFlow.Components.Projections.Composition` moves from `1.4.0` to `2.0.0`
  because its fixed Output and lifecycle contract change.
- `EventProjectionNode`, its direct snapshot Output, Errors port, Events, and
  explicit final-snapshot API remain unchanged for code-authored compatibility.
- The source-declaration baseline records the additive runtime declarations;
  no released declaration was removed or signature-changed.
- SDK package validation passes for Projections `4.0.0` against published
  `3.0.2` and Projections Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked state.

## Verification

- Projections runtime tests: 17 passed.
- Projections Composition tests: 12 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,064 tests across 63 projects with no
  failures or warnings.
- Controlled Debug and Release builds completed across 130 projects with zero
  warnings and zero errors. The Debug cold traversal exceeded its command bound
  before a clean incremental rerun. Release completed in about ten minutes
  after orphaned SDK build servers were shut down; unrelated active .NET
  processes in other workspaces were not touched.
- A package-only net8 consumer restored Projections Composition `2.0.0`, used
  its transitive runtime/Data/Composition contracts, asserted canonical port
  types and result/error names, constructed the canonical node, and printed
  `PROJECTIONS_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- No FlowValue conversion was added because ProjectionEvent and snapshot are
  stable domain contracts used directly by Expectations and workflow hosts.
- No implicit mapper, universal error port, persistence store, renderer,
  polling API, or Engine dependency was introduced.
- Legacy Composition `1.x` remains the definition compatibility line; this
  milestone does not rewrite existing documents automatically.

## Next Gate

Assess Metrics as the next bounded component-family pass. Preserve typed metric
sample/snapshot semantics while moving expected aggregation failures and
completion snapshots to canonical normal-result behavior where appropriate.

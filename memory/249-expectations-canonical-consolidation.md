# Expectations Canonical Consolidation

Date: 2026-07-23

## Status

The Expectations family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`221-vnext-expectations-flowresult.md`. Expectations now has one maintained
runtime and Composition contract.

## Canonical Contract

- `EventExpectationNode` accepts `FlowMessage<ProjectionEvent>` and resolves
  exactly once through one `FlowResult<EventExpectationResult>` Output.
- Expect and Guard matches, timeout, and ordered input completion remain normal
  successful variants. Expected filter-evaluation failures remain normal error
  variants; there is no universal Errors port.
- The node snapshots its filter, bounds retained event summaries and preview
  text, processes accepted input serially, and preserves fan-out, deterministic
  clock behavior, diagnostics, correlation, trace, causation, and headers.
- Match, timeout, evaluation failure, and completion claim and publish their
  result under one gate. Concurrent timeout/completion can therefore emit one
  result exactly once without posting after Output completion.
- Composition retains canonical `event.expect`, the hidden
  `event.expectation` load-time alias, fixed Input/Output/Events ports, and an
  optional host-owned clock resolved through an exact resource address.

## Removed Compatibility Surface

- Removed the temporary `FlowEventExpectationNode` name after its implementation
  assumed the concise `EventExpectationNode` name.
- Removed the direct-result `EventExpectationNode` implementation inherited
  from `FlowNode<ProjectionEvent, EventExpectationResult>` and its Errors
  output.
- Removed numeric `ExpectationsErrorCodes`; expected failures expose stable
  string codes from `ExpectationErrorCodeNames` inside normal `FlowResult`
  values.
- Migrated Composition tests from the obsolete composition runtime to flat
  canonical application definitions, revision hosting, stable ports, and exact
  `Resources.*` addresses.
- Updated the clock picker pattern from `clock:{name}` to
  `Resources.{name}`.

Consumers now construct `EventExpectationNode`, inspect `FlowResult.Kind`,
`IsError`, and `Error.Code`, and read the optional `EventExpectationResult`
from `Value`.

## Versioning And Compatibility

- `FluxFlow.Components.Expectations` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Expectations.Composition` moves from `2.2.0` to `3.0.0`.
- The source-declaration baseline changed only at manifest index 41, from 55 to
  46 public declarations; Composition index 42 is unchanged.
- SDK package validation against runtime `4.0.0` reports only the intentional
  `ExpectationsErrorCodes` and `FlowEventExpectationNode` removals plus the
  concise node's direct-result base-type change on both target frameworks.
- Expectations Composition remains binary compatible with `2.2.0`. No
  compatibility suppression was generated.

## Verification

- Expectations runtime tests: 15 passed with no warnings.
- Expectations Composition tests: 18 passed through canonical hosting with no
  warnings.
- Projection runtime tests: 9 passed; Projection Composition tests: 12 passed.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with 11 existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 25 warnings and no errors.
- Controlled Release build: succeeded with 63 warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both changed packages,
  including archive inspection, isolated smoke restore/build, and feed checks.
- A package-only `net8.0` consumer restored Expectations `5.0.0` and
  Expectations Composition `3.0.0`, built with warnings as errors, loaded flat
  `Resources`/`Workflows` JSON, resolved the keyed clock, and printed
  `EXPECTATIONS_CANONICAL_API_OK` after verifying the result contract, message
  lineage, Events, and absence of Errors.
- `graphify update . --force` refreshed ignored local graph output after the
  implementation and memory updates.

## Next Gate

Audit Sessions independently. Preserve exact `FlowContent` record ownership,
store round trips, query filtering and paging, replay-source lifecycle,
deterministic clocks, diagnostics, and normal failure routing before removing
typed compatibility contracts.

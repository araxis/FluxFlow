# Timers Canonical Consolidation

Date: 2026-07-23

## Status

The Timers family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`229-vnext-timers-flowvalue-results.md`. Timers now has one maintained runtime
and Composition contract.

## Canonical Contract

- `TimerIntervalNode` and `TimerScheduleNode` are source facades with
  `FlowValue` Output, Events, source lifecycle, fresh message identity,
  deterministic clocks, bounded fan-out, and no universal Errors property.
- Interval tick objects contain `timestamp`, `name`, `sequence`, `startedAt`,
  `dueAt`, `elapsed`, `interval`, and `drift`. Schedule tick objects contain
  `timestamp`, `name`, `sequence`, `startedAt`, `dueAt`, `cron`, `timeZoneId`,
  and `drift`.
- `TimerDelayNode`, `TimerThrottleNode`, and `TimerDebounceNode` accept
  `FlowValue` and emit one `FlowResult<FlowValue>` Output. Expected timing and
  missing-input failures remain normal result data; later inputs continue.
- Delay retains arrival-relative due times and ordered burst release. Throttle
  retains ordered queued rate limiting. Debounce retains latest-only
  suppression and serializes timer expiry with completion for exact-once
  publication.
- Successful and failed transform results preserve correlation, trace,
  causation, headers, and immutable input details. Unexpected faults remain on
  Completion; lifecycle and result diagnostics remain on Events.
- Composition retains `timer.interval`, `timer.schedule`, `timer.delay`,
  `timer.throttle`, and `timer.debounce` with fixed canonical ports. Optional
  clocks resolve through exact `Resources.{name}` addresses.

## Removed Compatibility Surface

- Removed `TimerTick`, `ScheduleTick`, generic `TimerDelayNode<T>`,
  `TimerThrottleNode<T>`, and `TimerDebounceNode<T>` contracts.
- Removed the temporary `FlowValueTimerIntervalNode`,
  `FlowValueTimerScheduleNode`, `FlowValueTimerDelayNode`,
  `FlowValueTimerThrottleNode`, and `FlowValueTimerDebounceNode` names after
  their canonical behavior assumed the concise public names.
- Removed numeric `TimerErrorCodes`, duplicate `TimerEventNames`, inherited
  public source Errors surfaces, and the redundant source projection pipeline.
- Removed `TimersTypedRegistrationExtensions` and generic transform
  registration overloads.
- Migrated Composition tests from the obsolete Composition runtime to flat
  canonical application definitions, revision hosting, stable ports, and exact
  resource addresses.

Consumers now use concise Timer nodes, read source tick fields from immutable
objects, convert typed values to `FlowValue` at the application boundary, read
successful transform values from `FlowResult.Value`, and route failures using
`Kind`, `IsError`, and `Error.Code` on the normal Output.

## Versioning And Compatibility

- `FluxFlow.Components.Timers` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Timers.Composition` moves from `2.0.0` to `3.0.0`.
- Source-declaration baseline manifest index 29 changed from 188 to 132 public
  declarations; Composition index 30 changed from 25 to 19.
- SDK package validation against runtime `4.0.0` reports only intentional
  removals of typed contracts, generic and temporary node types, numeric and
  duplicate diagnostics, plus the concise source base-contract changes on both
  target frameworks.
- SDK package validation against Composition `2.0.0` reports only intentional
  removal of typed registration extensions and generic transform overloads on
  both target frameworks.
- No API compatibility suppression was generated.

## Verification

- Timers runtime tests: 72 passed with no warnings.
- Timers Composition tests: 14 passed through canonical hosting with no
  warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with 11 existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 11 warnings and no errors.
- Controlled Release build: succeeded with 49 warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both changed packages,
  including archive inspection, isolated smoke restore/build, and feed checks.
- A package-only `net8.0` consumer restored Timers `5.0.0` and Timers
  Composition `3.0.0`, built with warnings as errors, exercised interval tick
  shape and delay result lineage, verified the fixed non-generic registration
  surface and absence of Errors, and printed `TIMERS_CANONICAL_API_OK`.
- The ignored local graph was refreshed after implementation and memory
  updates.

## Next Gate

Audit Sources independently. Preserve generated-item normalization and order,
sequence shape and limits, looping, timing, deterministic clocks, source
lifecycle, fresh identity, diagnostics, fan-out, and pre-canceled startup
before removing typed compatibility contracts. Audit Serialization in its own
subsequent bounded pass.

# vNext FlowValue Timers And Results

Date: 2026-07-20

## Status

The twenty-sixth bounded vNext milestone is implemented on local branch
`work/timers-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone makes Interval and Schedule canonical FlowValue sources and
makes Delay, Throttle, and Debounce canonical FlowValue-to-FlowResult
transforms. Released typed standalone nodes and explicit typed Composition
registrations remain available for compatibility.

## Canonical Runtime

- `FlowValueTimerIntervalNode` and `FlowValueTimerScheduleNode` emit immutable
  tick objects on one normal Output plus Events. They retain natural zero-input
  source lifecycle, deterministic clocks, bounded output, fresh message
  identity, finite tick limits, and pre-canceled startup without exposing a
  universal Errors port.
- Internal typed-tick projection preserves the original source envelope rather
  than creating a hidden workflow hop. Interval objects contain `timestamp`,
  `name`, `sequence`, `startedAt`, `dueAt`, `elapsed`, `interval`, and `drift`;
  Schedule objects contain `timestamp`, `name`, `sequence`, `startedAt`,
  `dueAt`, `cron`, `timeZoneId`, and `drift`.
- `FlowValueTimerDelayNode`, `FlowValueTimerThrottleNode`, and
  `FlowValueTimerDebounceNode` consume FlowValue and emit one
  `FlowResult<FlowValue>` Output plus Events. Success values preserve message
  correlation, trace, headers, and causation.
- Expected timing failures are normal result variants with stable
  `TimerResultKinds`, `TimerErrorCodeNames`, and immutable FlowError details;
  later accepted inputs continue without a universal Errors port.
- Delay retains absolute arrival-time due stamps and ordered draining. Throttle
  queues every accepted input and preserves order. Debounce intentionally emits
  no result for superseded values, flushes the selected latest value on normal
  completion, and serializes timer/completion claims for exact-once output.

## Composition And Designer

- Parameterless `RegisterTimerInterval()`, `RegisterTimerSchedule()`,
  `RegisterTimerDelay()`, `RegisterTimerThrottle()`, and
  `RegisterTimerDebounce()` own the canonical fixed contracts.
- Interval and Schedule descriptors expose FlowValue Output. Delay, Throttle,
  and Debounce expose FlowValue Input and `FlowResult<FlowValue>` Output. All
  canonical descriptors expose Events and no Errors surface.
- `RegisterTimerIntervalTicks(nodeType)` and
  `RegisterTimerScheduleTicks(nodeType)` preserve typed source contracts from a
  separate compatibility extension class. Existing generic transform
  registrations remain unchanged and explicit.
- The optional clock remains an exact host-owned keyed `TimeProvider` resource.
  Schedule metadata still reports `timeZone` as an intentionally omitted typed
  option; no time-zone conversion was introduced.
- Designer metadata and package examples now describe canonical fixed ports and
  the flat `Resources` / `Workflows` application document.

## Compatibility And Versioning

- `FluxFlow.Components.Timers` moves from local `3.1.3` to `4.0.0` because the
  package adds the canonical runtime surface. The latest public stable package
  is `3.1.2`, which is the SDK package-validation baseline.
- `FluxFlow.Components.Timers.Composition` moves from `1.6.0` to `2.0.0`
  because default fixed port and error surfaces change.
- `TimerIntervalNode`, `TimerScheduleNode`, `TimerDelayNode<T>`,
  `TimerThrottleNode<T>`, and `TimerDebounceNode<T>` retain their released typed
  ports, settings, lifecycle, Errors, Events, and direct-use behavior.
- The source-declaration baseline records additive canonical runtime and
  compatibility registration declarations; no released declaration was
  removed or signature-changed.
- SDK package validation passes for Timers `4.0.0` against published `3.1.2`
  and Timers Composition `2.0.0` against published `1.6.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked state.

## Verification

- Timers runtime tests: 72 passed.
- Timers Composition tests: 15 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,110 tests across 63 projects with no
  failures or warnings. Its first traversal observed one existing asynchronous
  Routing fault-assertion race; the exact test passed immediately and the full
  rerun was clean.
- Final controlled Debug and Release builds completed across 130 projects with
  zero warnings and zero errors. The first bounded Debug and cold Release
  traversals exceeded their command windows without compiler errors; warm
  controlled reruns completed cleanly.
- A package-only net8 consumer restored Timers `4.0.0` and Timers Composition
  `2.0.0`, asserted canonical and typed registration contracts, ran canonical
  Interval and Delay nodes, verified source identity and transform causation,
  and printed `TIMERS_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- Timer outputs remain live broadcast data, not durable schedule or replay
  storage.
- Debounce suppression remains intentional: a superseded input has no output
  operation and therefore no result.
- No fake input, implicit mapper, universal error port, polling/latest-value
  API, time-zone id conversion, renderer, Engine dependency, or host-specific
  service framework was introduced.
- Legacy Timers Composition `1.x` remains the stored-definition compatibility
  line.

## Next Gate

Assess HTTP as the next bounded component-family pass. Preserve one typed
request input and one polymorphic result output while migrating transported
bodies to FlowContent, keeping client lifetime host-owned, and separating
expected HTTP outcomes from runtime/system faults.

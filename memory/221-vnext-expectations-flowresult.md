# vNext Expectation Results

Date: 2026-07-19

## Status

The eighteenth bounded vNext milestone is implemented on local branch
`work/expectations-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone gives Expectations a canonical normal-result contract over the
existing Projections domain event while preserving the released standalone node
as an explicit compatibility surface.

## Canonical Node Contract

- `FlowEventExpectationNode` consumes `ProjectionEvent` and resolves exactly
  once through `FlowResult<EventExpectationResult>` on Output, plus lifecycle
  and result diagnostics through Events.
- Matching an Expect rule emits successful `Matched`; matching a Guard emits
  successful `Unmet`. `EventExpectationResult.Satisfied` remains the rule
  decision.
- Timeout and ordered input completion emit successful `TimedOut` and
  `Completed` variants. Either satisfies a Guard and leaves an Expect unmet.
- Expected filter evaluation failure emits one normal `EvaluationFailed` error
  result with stable string code and immutable `FlowValue` details. There is no
  universal Errors port on the canonical node.
- Completion resolves only after the ordered input block drains. Match,
  evaluation failure, timeout, and completion race through one exact-once
  claim; later triggers cannot emit another result.
- Results derived from an observed event preserve correlation, trace, and
  headers, create a new message identity, and record the event envelope as
  causation. A timeout or completion before any event starts a new exchange.

## Compatibility Boundary

- Existing `EventExpectationNode`, `EventExpectationOptions`, direct
  `EventExpectationResult` Output, Errors, Events, and completion API remain
  available for code-authored compatibility.
- Existing Projections contracts remain the domain input and evidence model;
  this pass does not convert them to `FlowValue` or mix projection state with
  runtime diagnostics.
- `FlowResult<EventExpectationResult>` is a real typed payload. Links never
  implicitly unwrap its Value.

## Composition And Designer

- `RegisterEventExpectation()` now registers canonical `event.expectation`
  with `ProjectionEvent` Input and one
  `FlowResult<EventExpectationResult>` Output.
- The canonical descriptor exposes Events only. Expectations Composition `2.x`
  intentionally does not register the legacy direct-result/Error-port node;
  existing hosts may remain on the published `1.x` line while migrating.
- The optional clock is host owned and resolved through an exact
  `Resources.{name}` address.
- Designer metadata describes only the canonical fixed ports and keeps the
  existing option section/editor and clock-picker hints.
- Package examples use only flat `Resources` and `Workflows` root sections.

## Compatibility And Versioning

- `FluxFlow.Components.Expectations` moves from `3.0.2` to `4.0.0` for the
  additive canonical node and stable result/error string constants.
- `FluxFlow.Components.Expectations.Composition` moves from `1.4.0` to `2.0.0`
  because fixed Output changes to `FlowResult<EventExpectationResult>` and the
  canonical Errors surface is removed.
- Source-declaration baseline entry 41 is
  `41|55|77F78F118AFCC881459AEC1956EDE3A34786D29660CB2A5EEE7B6DBD2B3C149E`.
  Entry 42 remains
  `42|11|D7241D3FD4EB80B826FFB10CBF45200D2919781E70F5D3D920D6B6C084F463CC`.
- SDK package validation passes for Expectations `4.0.0` against published
  `3.0.2` and Expectations Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked repository state.

## Verification

- Expectations runtime tests: 24 passed, including all preserved legacy tests
  and canonical matched, unmet, timeout, completion, evaluation-failure,
  message-lineage, diagnostic, exact-once, and option regressions.
- Expectations Composition tests: 15 passed, including canonical metadata,
  hosted matching, timeout, normal evaluation errors, and activation failures.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,041 tests across 63 projects with no
  failures, skips, or warnings.
- Final controlled Debug and Release solution builds completed across 130
  projects with zero warnings and zero errors. Each initial full traversal
  reported one transient warning hidden by errors-only output; immediate
  controlled incremental reruns were clean.
- A package-only net8 consumer restored Expectations `4.0.0` and Expectations
  Composition `2.0.0`, exercised the canonical result and message lineage,
  checked fixed registration and Designer metadata, and printed
  `EXPECTATIONS_VNEXT_API_OK`.
- Binary validation initially required the normal NuGet cache and dry-run feed
  verification required network access outside the sandbox. The approved final
  commands passed without package or source changes.

## Deferred Boundaries

- No implicit result extraction, universal error port, alternate projection
  event model, or automatic lifecycle supervision was introduced.
- The existing compatibility node is not part of the canonical Composition or
  Designer catalog.
- Remaining component families retain their current contracts until separate
  bounded migrations.

## Next Gate

Assess Routing as the next bounded component-family pass. Deprecate Switch,
Fork, and Merge where canonical conditional fanout and multi-source inputs now
replace them, while preserving and migrating Window, Correlation, and Join as
explicit domain operations.

# Routing Canonical Consolidation

Date: 2026-07-25

## Status

The remaining Routing algorithm compatibility is consolidated on local branch
`work/canonical-vnext-cleanup`. No push, tag, package publication, pull request,
or merge was performed.

## Canonical Runtime

- `FlowValueWindowNode`, `FlowValueCorrelationNode`, and `FlowValueJoinNode`
  are the only public stateful Routing components.
- All consume immutable `FlowValue` messages and emit one normal
  `FlowResult<T>` Output plus Events.
- Window count, time, and completion boundaries; correlation matching and
  timeout; two-input FIFO join and timeout; pending limits; expected-failure
  continuation; deterministic clocks; fan-out; diagnostics; and message
  lineage remain covered.
- The mature generic algorithms remain internal runtime collaborators behind
  the canonical facades, not a second component model.

## Removed Parallel Surface

- Removed public `FlowWindowNode<TInput>`, `FlowCorrelationNode<TInput>`, and
  `FlowJoinNode<TLeft,TRight>` compatibility components.
- Removed generic `RegisterWindow<TInput>()`,
  `RegisterCorrelation<TInput>()`, and `RegisterJoin<TLeft,TRight>()`
  Composition overloads and their direct match, timeout, and Errors ports.
- Removed unreferenced `RoutingComponentPorts` and compatibility-only
  `RoutingCompositionPortNames.Matched` and `.Timeouts` constants.
- Renamed algorithm files and tests to internal runtime terminology so the
  source tree does not imply a supported generic component family.

## Versions And Compatibility

- `FluxFlow.Components.Routing` remains at the already-selected unpublished
  major version `5.0.0`.
- `FluxFlow.Components.Routing.Composition` remains at `3.0.0`.
- Source-declaration baseline package index 21 moved from 205 to 191 and index
  22 moved from 27 to 22; only reviewed Routing declarations changed.
- SDK package validation against Routing `4.0.0` and Routing Composition
  `2.2.0` reported only the documented structural, typed-component,
  registration, option, diagnostic, and constant removals. No suppressions
  were added.
- Release preflight and complete local-source dry-runs passed for both packages.

## Verification

- Routing: 51 passed, zero warnings.
- Routing Composition: 13 passed, zero warnings.
- Release: 99 passed, zero warnings.
- Controlled Debug and Release builds completed 129 projects with zero errors
  and zero warnings. Cold builds exceeded their command windows; completed
  artifacts were followed by successful controlled confirmation builds.
- All 58 manifest packages were packed from the current Release build into a
  fresh temporary source outside the repository.
- A fresh net8.0 consumer with 58 direct package references restored from the
  complete source plus the public feed and built in Release with zero warnings
  and zero errors.

## Migration

Convert CLR payloads to `FlowValue` at the application boundary. Use the
canonical node names and parameterless registrations, then route `matched`,
`timed-out`, and `operation-failed` outcomes from normal `Output`. Canonical
links replace removed Switch, Fork, and Merge structure.

## Remaining Program Work

Perform the final requirement-by-requirement cleanup audit, reduce
`memory/01-current-state.md` to current facts, refresh Graphify output, and
record overall completion in a separate closeout commit.

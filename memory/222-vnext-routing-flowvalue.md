# vNext FlowValue Routing

Date: 2026-07-19

## Status

The nineteenth bounded vNext milestone is implemented on local branch
`work/routing-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone makes Window, Correlation, and Join canonical FlowValue/result
operations while deprecating structural nodes now owned by canonical link
semantics.

## Canonical Node Contracts

- `FlowValueWindowNode` consumes `FlowValue` and emits one
  `FlowResult<FlowWindow<FlowValue>>` Output plus Events. Count, time, and final
  completion windows are successful result kinds.
- `FlowValueCorrelationNode` consumes one `FlowValue` input and emits matched
  or timed-out `FlowCorrelationOutcome<FlowValue>` values through one normal
  `FlowResult` Output plus Events.
- `FlowValueJoinNode` consumes separate `Left` and `Right` FlowValue inputs and
  emits matched or timed-out `FlowJoinOutcome<FlowValue,FlowValue>` values
  through one normal `FlowResult` Output plus Events.
- Expected selector, key, side, pending-capacity, and operation failures use
  stable `operation-failed` results with `routing.operation_failed`; the
  retained numeric routing code and context are immutable error details.
- Message lineage is preserved for windows, matches, timeouts, and operation
  failures. Correlation failures keep the current input id; Join selector and
  capacity failures now do the same.
- Adapter completion waits for all retained output/error adapters to drain
  before closing the canonical output. Unexpected faults remain completion
  faults while sibling adapter cleanup continues.

## Structural Routing Boundary

- `FlowSwitchNode<TInput>`, `FlowForkNode<TInput>`, and
  `FlowMergeNode<TInput>` remain binary/source compatible but are marked
  obsolete.
- Their Composition registrations and Designer entries remain available and
  are marked deprecated with migration reasons.
- Canonical conditional links replace Switch, output broadcast links replace
  Fork, and shared target inputs replace Merge. No automatic graph rewrite or
  implicit value conversion was introduced.

## Compatibility Boundary

- Existing generic `FlowWindowNode<TInput>`,
  `FlowCorrelationNode<TInput>`, and `FlowJoinNode<TLeft,TRight>` contracts keep
  their direct match, timeout, Errors, and Events surfaces for code-authored
  compatibility.
- The parameterless Composition registrations own canonical fixed FlowValue and
  FlowResult ports. Explicit generic overloads remain for host-selected custom
  node type names.
- Typed results are real payloads. Links never implicitly unwrap
  `FlowResult<T>` or convert `FlowValue` to arbitrary CLR types.

## Composition And Designer

- `RegisterWindow()`, `RegisterCorrelation()`, and `RegisterJoin()` register
  canonical factories with one Output and no universal Errors descriptor.
- Correlation and Join resolve exact keyed `Func<FlowValue,string?>` selector
  resources; all retained nodes can resolve an optional keyed `TimeProvider`.
- Designer metadata uses canonical FlowValue/result ports for retained nodes
  and preserves option, editor, selector, clock, and dynamic-output hints.
- Package examples use the flat `Resources` and `Workflows` document with exact
  case-sensitive addresses.

## Compatibility And Versioning

- `FluxFlow.Components.Routing` moves from `3.0.3` to `4.0.0`.
- `FluxFlow.Components.Routing.Composition` moves from `1.4.0` to `2.0.0`
  because the fixed retained-node descriptors change to canonical FlowValue and
  FlowResult ports.
- Source-declaration baseline entry 21 is
  `21|257|7F6C5E1B1C74BAC00E69205EE243D1D2575A325A63829BDCB1B3C65E5A0183F3`.
  Entry 22 is
  `22|35|EA13C1194BFE845B1096C903F72FAE2E6FC6407ADCD959FD3B77AF482548B397`.
- SDK package validation passes for Routing `4.0.0` against published `3.0.3`
  and Routing Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked repository state.

## Verification

- Routing runtime tests: 86 passed, including canonical window,
  correlation-match/timeout/failure, join-match/timeout/failure, message
  lineage, and all retained generic regressions.
- Routing Composition tests: 19 passed, including canonical metadata and a
  hosted keyed-selector FlowValue correlation with one Output and no Errors
  descriptor.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,048 tests across 63 projects with no
  failures or warnings.
- Final controlled Debug and Release solution builds completed across 130
  projects with zero warnings and zero errors. Initial errors-only traversals
  exceeded their command bounds without reporting compiler failures; SDK build
  servers were shut down and the controlled incremental reruns completed
  cleanly.
- A package-only net8 consumer restored Routing `4.0.0` and Routing Composition
  `2.0.0`, compiled the canonical nodes and registrations, and printed
  `ROUTING_VNEXT_API_OK`.

## Deferred Boundaries

- No structural-node removal, implicit result extraction, automatic mapper,
  universal error port, new expression language, or alternate host lifetime
  was introduced.
- Existing generic nodes remain available until a separately planned removal
  decision.
- Remaining component families retain their current contracts until separate
  bounded migrations.

## Next Gate

Assess Control as the next bounded component-family pass. Determine whether
`flow.filter` and `flow.when` should be deprecated in favor of canonical link
conditions or retained only where they provide a distinct result-producing
domain operation.

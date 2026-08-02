# Assertions Canonical Consolidation

Date: 2026-07-23

## Status

The Assertions family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`220-vnext-assertions-flowvalue.md`. Assertions now has one maintained
component runtime and Composition contract.

## Canonical Contract

- `FlowValueAssertionNode` accepts `FlowValue` and emits one
  `FlowResult<FlowValueAssertionResult>` Output.
- Passed and failed rule evaluations are successful result kinds. Missing
  input and expression evaluation failure are normal error results; later
  messages continue.
- Expressions still compile once through the host-owned
  `IFlowExpressionEngine`.
- `IFlowMapContextFactory<FlowValue>` preserves custom variables and receives
  the exact immutable input value.
- Results and Events retain expression id/name, engine name, semantic input
  type, deterministic clocks, stable result/error codes, correlation metadata,
  ordered fan-out, and exact original input identity.

## Removed Compatibility Surface

- Removed `FlowAssertionComponent<TInput>`, `AssertionOptions`,
  `FlowAssertionResult`, `AssertionFailure`, `AssertionResultMetadata`,
  `FlowAssertionStatus`, and numeric `AssertionErrorCodes`.
- Removed generic `RegisterAssertion<TInput>()` and the Passed and Failed
  Composition port constants.
- Removed the generic component's Passed, Failed, and Errors workflow surfaces.
- Removed unused `FlowValueAssertionOptions.Engine`; the required keyed
  `engine` resource remains the only engine-selection contract.
- Removed generic-only emitted-branch metadata and migrated Assertions
  Composition tests to the canonical application revision and stable-port
  runtime.

CLR consumers now convert values explicitly at the application boundary and
route outcomes through conditions over `FlowResult.Kind`, `IsError`, and
`Error.Code`.

## Versioning And Compatibility

- `FluxFlow.Components.Assertions` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Assertions.Composition` moves from `2.2.0` to `3.0.0`.
- Source-declaration baseline entries changed only for manifest indexes 17 and
  18: Assertions from 94 to 43 declarations and Assertions Composition from 17
  to 14.
- SDK package validation against the preceding versions reports only the seven
  removed runtime types, dead engine option accessors, generic registration,
  and Passed/Failed constants on both target frameworks. No suppression was
  generated.

## Verification

- Assertions runtime tests: 8 passed with no warnings.
- Assertions Composition tests: 14 passed with canonical hosting and no
  warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with 11 existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 31 warnings and no errors.
- Controlled Release build: succeeded with 69 warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both changed packages,
  including archive inspection, isolated smoke restore/build, and feed checks.
- A combined package-only `net8.0` consumer restored Assertions `5.0.0` and
  Assertions Composition `3.0.0`, built with warnings as errors, activated the
  canonical factory from flat `Resources`/`Workflows` JSON, resolved the keyed
  expression engine, and verified exact input identity plus one normal result
  Output.
- `graphify update . --force` refreshed the ignored local graph to 16,615
  nodes, 35,791 edges, and 962 communities; HTML generation was skipped at the
  configured size limit.

## Next Gate

Audit State as a separate bounded pass. Prove FlowValue reducer behavior,
factory/context parity, normal result variants, exact state/value preservation,
diagnostics, and canonical composition activation before removing object-based
compatibility contracts.

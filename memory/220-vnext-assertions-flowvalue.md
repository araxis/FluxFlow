# vNext FlowValue Assertions

Date: 2026-07-19

## Status

The seventeenth bounded vNext milestone is implemented on local branch
`work/assertions-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone gives Assertions a canonical `FlowValue` contract while
preserving its existing generic standalone component, options, result,
Passed/Failed/Errors ports, and generic Composition registration as explicit
compatibility surfaces.

## Canonical Node Contract

- `FlowValueAssertionNode` consumes `FlowValue` and emits one
  `FlowResult<FlowValueAssertionResult>` through Output, plus lifecycle and
  message diagnostics through Events.
- Passed and failed rules are successful result kinds. A failed assertion is
  workflow data and has `IsError = false`.
- Missing input and expression evaluation failure are stable normal error
  results with string error codes and immutable `FlowValue` details. They do
  not stop later messages.
- The result preserves the exact input `FlowValue`, decision, description,
  message, expression metadata, actual engine name, semantic input type, and
  evaluation timestamp.
- Output envelopes preserve correlation, trace, and headers while `With(...)`
  records the consumed message as causation.
- The canonical node has Input, Output, and Events only. It does not expose
  Passed, Failed, or Errors.

## Expression And Option Boundaries

- The host supplies `IFlowExpressionEngine`; the package remains expression
  language neutral and compiles the Boolean predicate once at construction.
- The default context exposes the exact immutable value as `input` and `value`.
  An optional `IFlowMapContextFactory<FlowValue>` can add data-shaped variables.
- `FlowValueAssertionOptions` owns only canonical settings. The existing
  `AssertionOptions.EmitPassedInput` and `EmitFailedInput` settings remain
  specific to the generic compatibility node.
- `FlowResult<FlowValueAssertionResult>` is a real typed payload. Links do not
  implicitly unwrap its value; extraction or routing requires an explicit
  result-aware component or mapper.

## Composition And Designer

- Parameterless `RegisterAssertion()` registers canonical `flow.assert` with a
  `FlowValue` Input and one `FlowResult<FlowValueAssertionResult>` Output.
- Existing `RegisterAssertion<TInput>(customNodeType)` remains available for
  explicit generic compatibility and retains its branch/error surfaces.
- The required expression engine and optional FlowValue context factory and
  clock are host-owned resources using exact `Resources.{name}` addresses.
- Designer metadata exposes only the canonical fixed ports. Routed-input flags
  are explicitly documented as omitted generic compatibility options.
- Package documentation uses flat `Resources` and `Workflows` sections and
  documents normal result and typed-result boundaries.

## Compatibility And Versioning

- `FluxFlow.Components.Assertions` moves from `3.0.2` to `4.0.0` for the
  additive canonical node, options, result, diagnostics, and string constants.
- `FluxFlow.Components.Assertions.Composition` moves from `1.4.0` to `2.0.0`
  because its parameterless fixed contract now uses canonical FlowValue/result
  ports and no universal branch/error outputs.
- Source-declaration baseline entry 17 is
  `17|94|798E71FDB7351FE64B5066588159FC8C5C6848B923DDD50D84FDF6BEB1809912`.
  Entry 18 is
  `18|16|CAFACF736B5C5E8EDCE50919B15179871FB2CC9B77B1BD2A19C0581C17035E6A`.
- SDK package validation passes for Assertions `4.0.0` against published
  `3.0.2` and Assertions Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside the repository.

## Verification

- Assertions runtime tests: 19 passed, including all 12 preserved generic tests
  and 7 canonical result, continuation, identity, lineage, context, event,
  completion, and option/port regressions.
- Assertions Composition tests: 14 passed, including canonical metadata and
  hosted activation plus every existing generic registration test.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Component Composition metadata convention tests: 28 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,030 tests across 63 projects with no
  failures, skips, or warnings. The one sandbox-only sample config failure was
  isolated, rerun successfully, and the complete sweep then passed under the
  inherited local NuGet configuration.
- Final controlled Debug and Release solution builds completed across 130
  projects with zero warnings and zero errors.
- A package-only net8 consumer restored Assertions `4.0.0` and Assertions
  Composition `2.0.0` from the temporary source, verified normal pass/fail
  results, exact `FlowValue` identity, message lineage, absence of legacy ports,
  canonical registration and Designer metadata, and printed
  `ASSERTIONS_VNEXT_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 16,559
  nodes, 26,143 edges, and 1,694 communities. `graphify-out/` remains excluded
  from tracked repository state.

## Deferred Boundaries

- The package does not select expression syntax, infer link mappings, or own
  expression-engine resources.
- The generic assertion component remains available but is not the canonical
  fixed Composition contract.
- Expectations and the remaining component families retain their current
  contracts until separate bounded migrations.

## Next Gate

Migrate Expectations as the next bounded family. Represent matched, unmet,
timeout, completion, and expected evaluation failures as normal result variants
while preserving existing projection-event compatibility and lifecycle
semantics where practical.

# Mapping Canonical Consolidation

Date: 2026-07-23

## Status

The Mapping family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`216-vnext-mapping-flowvalue.md`. Mapping now has one maintained component
runtime and Composition contract.

## Canonical Contract

- `FlowValueMapperNode` accepts `FlowValue` and emits one
  `FlowResult<FlowValue>` Output for mapped values and expected failures.
- Expressions still compile once through the host-owned
  `IFlowExpressionEngine` and evaluate with the exact immutable input value.
- `IMappingContextFactory` and `MappingNodeContext` remain available, preserving
  custom variables, resolved options, and canonical input/output type context.
- Failures retain the original value, stable string error code, exception
  details, message lineage, and later-message continuation.
- Diagnostics retain engine, semantic input/output type, expression id/name,
  timestamp, and correlation metadata. Output fan-out remains ordered.

## Removed Compatibility Surface

- Removed `FlowMapperNode<TInput,TOutput>`,
  `TypedMappingContextFactory<TInput>`, and numeric `MappingErrorCodes`.
- Removed generic `RegisterMapper<TInput,TOutput>()` and
  `MappingCompositionPortNames.Failed`.
- Removed the generic node's `Failed` and `Errors` workflow surfaces.
- Removed ignored `MapperOptions.Engine` and legacy `MapperOptions.TargetType`;
  the keyed `engine` resource and canonical `OutputType` remain.
- Migrated Mapping Composition tests from obsolete Composition hosting to the
  canonical application revision and stable-port runtime.

CLR consumers now convert values explicitly at the application boundary and
route failures through conditions over `FlowResult.Kind`, `IsError`, and
`Error.Code`.

## Versioning And Compatibility

- `FluxFlow.Components.Mapping` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Mapping.Composition` moves from `2.2.0` to `3.0.0`.
- Source-declaration baseline entries changed only for manifest indexes 13 and
  14: Mapping from 51 to 35 declarations and Mapping Composition from 16 to 14.
- SDK package validation against the preceding versions reports only the three
  removed runtime types, two removed option properties, generic registration,
  and `Failed` port constant on both target frameworks. No suppression was
  generated.

## Verification

- Mapping runtime tests: 7 passed with no warnings.
- Mapping Composition tests: 12 passed with canonical hosting and no warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with 11 existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 46 existing warnings and no errors.
- Controlled Release build: succeeded with 84 existing warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both changed packages,
  including archive inspection, isolated smoke restore/build, and feed checks.
- A combined package-only `net8.0` consumer restored Mapping `5.0.0` and Mapping
  Composition `3.0.0`, built with warnings as errors, activated the canonical
  factory from flat `Resources`/`Workflows` JSON, resolved the keyed expression
  engine, and verified exact value identity plus one normal result Output.
- `graphify update . --force` refreshed the ignored local graph to 16,716
  nodes, 35,995 edges, and 957 communities; HTML generation was skipped at the
  configured size limit.

## Next Gate

Consolidate Validation as a separate bounded pass. Prove schema loading,
selector behavior, valid/invalid result parity, input preservation, and
canonical composition activation before removing its generic validator and
branch/Error surfaces.

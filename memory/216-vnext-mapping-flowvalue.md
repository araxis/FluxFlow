# vNext FlowValue Mapping

Date: 2026-07-18

## Status

The thirteenth bounded vNext milestone is implemented on local branch
`work/mapping-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone migrates the canonical Mapping component to `FlowValue` and
normal result variants while preserving the existing strongly typed mapper as
an explicit compatibility surface.

## Canonical Node Contract

- `FlowValueMapperNode` consumes `FlowMessage<FlowValue>` and emits
  `FlowMessage<FlowResult<FlowValue>>` through one normal output.
- The expression compiles once during construction through the existing
  host-provided `IFlowExpressionEngine` contract.
- The exact immutable input `FlowValue` is supplied to the default or custom
  mapping context. No JSON, dynamic-object, or CLR-object conversion is used.
- Success uses result kind `Mapped`. Expected expression failures use result
  kind `MappingFailed`, error code `mapping.mapper_failed`, retain the original
  input in `Value`, and do not stop later messages.
- Result messages preserve correlation, trace, and headers while `With(...)`
  creates the next message/causation identity. Diagnostics remain on `Events`;
  the canonical node has no universal Error or separate Failed port.
- `FlowMapperNode<TInput,TOutput>` remains unchanged for code-authored typed
  workflows, including its established Output, Failed, Errors, and Events
  behavior.

## Composition And Designer

- Parameterless `RegisterMapper()` now owns the default `flow.mapper` type and
  registers `FlowValue` Input plus `FlowResult<FlowValue>` Output.
- `RegisterMapper<TInput,TOutput>(...)` remains available for explicit typed
  compatibility registrations. A distinct node type is required when both
  forms share one registry.
- Flat canonical components can reference a host-owned keyed expression engine
  directly, for example `engine: Resources.Expressions.Primary`; optional
  context factory and clock references use the same exact address model.
- Designer metadata now describes only the canonical ports, normal result
  semantics, existing option hints, and `Resources.{name}` host-owned picker
  patterns.
- Release conventions prefer a non-generic canonical registration when a
  generic compatibility overload has the same default node type. Port metadata
  conventions validate the canonical default registry while compatibility-only
  constants must still be used by registry source.

## Compatibility And Versioning

- `FluxFlow.Components.Mapping` moves from `3.0.2` to `4.0.0` for the additive
  canonical FlowValue node and public result/error-name constants.
- `FluxFlow.Components.Mapping.Composition` moves from `1.4.0` to `2.0.0`
  because the default `flow.mapper` payload and port contract changes.
- The reviewed source-declaration baseline changes only manifest entries 13
  and 14: Mapping grows from 35 to 51 declarations and Mapping Composition from
  14 to 15 declarations.
- SDK package validation passes for Mapping `4.0.0` against published `3.0.2`
  and Mapping Composition `2.0.0` against published `1.4.0`; the legacy binary
  declarations remain available.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside the repository.

## Verification

- Mapping runtime tests: 18 passed.
- Mapping Composition tests: 14 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed after documenting a test-only
  `ISourceBlock<T>.ConsumeMessage` return with `[MaybeNull]`; this removed the
  repository-wide nullable warning without changing package code or APIs.
- Release convention tests: 93 passed.
- The complete Release sweep passed 1,989 tests across 63 projects with zero
  warnings and no skipped tests.
- Final controlled Debug and Release solution builds each covered 130 projects
  with zero warnings and zero errors.
- A package-only net8 consumer restored Mapping Composition `2.0.0` and its
  current dependencies from the temporary package source plus NuGet, created a
  standalone mapper, activated the canonical factory from flat
  `Resources`/`Workflows` JSON, verified exact FlowValue identity and the
  no-error/no-Failed shape, and printed `MAPPING_VNEXT_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 16,128 nodes
  and 25,308 edges. `graphify-out/` remains excluded from tracked repository
  state.

## Deferred Boundaries

- Generic host orchestration for discovering component/resource registration
  extensions and building complete provider snapshots remains part of the
  later Hosting/Designer stage.
- Mapping does not insert itself into links. Payload shape changes remain
  explicit workflow components.
- The other normal component families retain their existing contracts until
  migrated in separate bounded milestones.

## Next Gate

Migrate Payloads as the next bounded family. Make payload inspection consume
`FlowContent`, preserve exact ingress bytes and lazy decode semantics, represent
expected classification/formatting failures as normal data where applicable,
and update its Composition/Designer/package surfaces without combining the
Serialization migration into the same milestone.

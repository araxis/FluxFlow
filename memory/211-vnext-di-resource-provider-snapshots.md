# vNext DI Resource And Provider Snapshots

Date: 2026-07-17

## Status

The eighth bounded vNext milestone is implemented on local branch
`work/di-resource-snapshots-vnext`. No push, tag, publication, pull request, or
merge was performed.

This milestone adds immutable Microsoft DI provider snapshots, canonical keyed
resource/component/port registration, and explicit ownership bridges. It does
not add provider publication, dependency-closure planning, transactional
resource/workflow updates, component migration, or MQTT redesign.

## Address And Signal Contracts

- `ApplicationAddress` now represents canonical `Workflow.Component`
  addresses in addition to resource, workflow-port, and reserved system-port
  addresses. Existing enum numeric values remain unchanged and
  `ResolvePort("Component.Port", workflow)` preserves local-port resolution.
- `IFlowSignalTarget` lives in standalone `FluxFlow.Nodes`, accepts any
  `FlowMessage<T>`, and reports acceptance without requiring Engine or one
  registered payload type.
- The initial design placed the signal target in Engine. Release boundary tests
  correctly rejected the resulting Composition.Hosting to Engine dependency;
  the final contract preserves the standalone-first package boundary.

## Provider Snapshots

- `CompositionServiceProviderSnapshotBuilder` copies explicitly supplied
  service descriptors and builds an immutable normal Microsoft DI provider.
  Build and scope validation are enabled by default.
- `CompositionProviderBoundary` distinguishes host, resource-revision, and
  workflow-revision snapshots. `CompositionProviderSnapshotInfo` records the
  stable name, boundary, creation time, provider ownership, and optional
  service count for later revision events.
- Snapshots expose normal keyed and unkeyed resolution plus optional explicit
  scopes. They do not scan assemblies, reflect over component types, merge
  providers, fall back to arbitrary providers, or create scopes per message.
- Owned snapshots dispose their provider once. External-host snapshots and
  explicit bridges retain external ownership.

## Canonical Keyed Registration

- `ApplicationAddress.Value` is the ordinal keyed-service identity for nested
  resources, `Workflow.Component` blocks, typed input/output ports, and
  payload-independent signal targets.
- Factory registrations transfer lifetime and disposal ownership to the built
  provider. `...View` registrations create non-owning aliases of another
  provider-owned service. `AddExternal...` and `BridgeExternal...` methods keep
  exact external instances externally owned.
- Component, typed Dataflow port, and signal views forward completion and
  delivery without implementing disposal, preventing duplicate ownership.
- Canonical resource strings resolve directly through the existing
  `CompositionNodeFactoryContext` keyed-resource helpers.

## Compatibility And Versioning

- `FluxFlow.Nodes` moves from local `2.0.0` to additive `2.1.0`.
- `FluxFlow.Composition` moves from local `2.1.0` to additive `2.2.0`.
- `FluxFlow.Composition.Hosting` moves from `1.1.0` to `2.0.0` for the new
  provider-snapshot responsibility and direct full DI implementation
  dependency. It remains free of `FluxFlow.Engine`.
- The reviewed public source-declaration baseline changes only for Nodes,
  Composition, and Composition.Hosting.

## Verification

- Nodes tests: 41 passed.
- Composition tests: 116 passed.
- Composition.Hosting tests: 32 passed, including descriptor copying, safe
  validation defaults, scopes, concurrent resolution, deterministic snapshot
  metadata JSON, canonical resource lookup, mixed-payload signals, and exact
  owned/view/external disposal behavior.
- Release convention tests: 93 passed, including the standalone composition
  dependency boundary and updated public API baseline.
- Complete Release solution sweep: 1,926 tests passed across 63 projects with
  zero failures and zero warnings.
- Controlled Debug build: 130 projects, zero warnings and zero errors.
- Controlled Release build completed with zero errors. Its first summary
  reported one unsurfaced transient warning; an immediate identical
  warning-only controlled rerun covered all 130 projects with zero warnings.
- Binary package compatibility passed for Nodes `2.1.0` against `2.0.0`,
  Composition `2.2.0` against `2.1.0`, and Composition.Hosting `2.0.0` against
  `1.1.0` using a complete temporary local dependency source.
- Release preflight and isolated package dry-runs passed for all three changed
  packages, including archives, symbols, package restore/build, and feed-style
  verification.
- A package-only net8 consumer restored from the temporary source, exercised
  provider snapshots plus resource/component/typed-port/signal registration,
  and printed `DI_SNAPSHOT_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 14,333
  nodes and 28,232 edges.

## Next Gate

Implement transactional resource and workflow revisions as a separate bounded
milestone. Build and validate complete candidates away from live routing,
publish one immutable routing/resource snapshot atomically, preserve the old
revision on pre-activation failure, and drain replaced revisions. Do not combine
that pass with MQTT or broad component-family migration.

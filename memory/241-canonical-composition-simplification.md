# Canonical Composition Simplification

Date: 2026-07-22
Branch: `work/canonical-composition-simplification`

## Outcome

- `ApplicationDefinitionNormalizer` rewrites registered component aliases and
  known resource aliases, including `resilience.retry` to `retry.policy`, with
  deterministic structured diagnostics. Repeated normalization is unchanged.
- Canonical load, revision, Designer, and runtime paths normalize before use.
  Alias-only revisions compare equal, and Designer serialization emits canonical
  names.
- Package-internal typed descriptors now define canonical type, aliases, and
  processing capabilities once. Registry and Designer alias metadata derive
  from those descriptors; public constants remain compatible.
- `CompositionNodeFactoryContext` executes canonical `ComponentDefinition`
  directly. The component object key is `ComponentName` and defaults an absent
  canonical options `Name`; explicit legacy names and independent legacy
  configuration/resource slots remain accepted.
- Every canonical registration reserves `Events` as an addressable
  `FlowMessage<CompositionComponentEvent>` output. Existing `FlowEvent` sources
  are bridged with available correlation and normal tracing. Delivery is bounded
  and fault-isolated. Component events are not duplicated into Engine-owned
  `System.Events.Output`.
- Failure semantics are explicit: `Output` carries normal success/expected
  failure values, normally `FlowResult<T>`; `Events` carries observations;
  `Completion` carries unrecoverable faults. No universal Errors port was added.
- Optional `processing.profile` resources expose semantic `Mode`, `Order`, and
  `Buffer`. A flat `Processing` reference resolves through revision DI and an
  overridable `ICompositionProcessingProfileMapper`. Defaults require no
  profile; unsupported parallel/order combinations fail before factory work.
- Canonical Designer catalogs add `Events` and processing hints while hiding
  legacy `name`, `boundedCapacity`, `maxDegreeOfParallelism`, and `ensureOrdered`
  fields. Raw package metadata remains stable for provider-level compatibility.
- New guidance uses conditional links, ordinary fan-out, and shared-input
  fan-in. Structural routing compatibility types remain loadable/renderable but
  are not recommended for new workflows.

## Versions

- `FluxFlow.Composition` `2.7.0`
- `FluxFlow.Composition.Hosting` `2.3.0`
- `FluxFlow.Engine` `2.7.0`
- `FluxFlow.Components.Designer` `2.21.0`
- Assertions, Expectations, FileSystem, HTTP, Mapping, Metrics, Observability,
  Projections, Routing, Sessions, Sources, State, Validation, and MQTT
  Composition packages `2.2.0`
- `FluxFlow.Components.Storage.Composition` `2.1.0`

## Verification

- Focused Composition (`144`), Hosting (`46`), Engine (`106`), Designer
  (`111`), MQTT, and all component Composition package suites passed after
  implementation.
- The complete Release suite passed (`95`). Documentation boundaries now
  enforce the canonical application model, and the sample-process harness
  drains redirected output while waiting so compiler warnings cannot block a
  child process.
- The source-declaration public API baseline was reviewed and accepted. SDK
  package validation initially found four CLR signatures displaced by added
  optional parameters. Exact compatibility overloads were restored for
  `CompositionNodeRegistration`, `CompositionNodeRegistry.Register`,
  `ApplicationRevisionHost`, and `ApplicationRevisionCoordinator`; all `19`
  changed packages then passed binary compatibility against their immediately
  preceding public versions.
- All `19` changed packages passed release preflight and isolated archive,
  consumer restore/build, and feed verification dry-runs against a complete
  temporary source containing all `58` manifest packages.
- Controlled Debug build initially hit orphaned MSBuild file locks. Only the two
  verified orphaned FluxFlow workers were stopped before continuing. Final
  controlled Debug and Release solution builds passed with no errors after a
  build-server shutdown.

## Deferred

- Remove obsolete `CompositionDefinition` and Node-oriented public terminology
  only in the next major release. Do not add parallel duplicate Component APIs
  before that cleanup.
- No tag, package publication, push, pull request, or merge is part of this pass.

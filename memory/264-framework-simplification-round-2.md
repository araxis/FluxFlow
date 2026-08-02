# Second Framework Simplification Round

Date: 2026-07-26

## Scope

This pass simplified duplicated execution, persistence, Designer metadata,
processing configuration, and stateful runtime internals after the typed
flow-data migration. It remained local on
`work/framework-simplification-round-2`; no branch push, tag, package publish,
pull request, or merge occurred.

Across production C# source, 47 files changed with 1,362 inserted and 1,593
deleted lines, a net reduction of 231 lines. Tests and focused internal helpers
were added where extraction made previously implicit state directly testable.

## Shared Node Execution

- `FlowNode<TInput,TOutput>` now owns intake and execution through one bounded
  `ActionBlock`. The redundant intake `BufferBlock` and its link/completion
  plumbing were deleted.
- Configured capacity, parallelism, ordering, cancellation, completion, output
  broadcast, diagnostic flush, and disposal now have one lifecycle owner.
- Storage, FileSystem, and Observability operation pipelines delegate their
  common behavior to `FlowNode`. Sessions uses the same path and preserves its
  finalization through the existing input-completion hook.
- Incoming errors still bypass ordinary processing unless `HandlesErrors` is
  explicitly enabled. Observability retains that deliberate override.
- Serialization and Timers retain their dedicated pipelines because delayed
  emission, pending-item ownership, and completion races are domain behavior,
  not duplicate execution shells.

## Persisted FlowContent

- `FlowContent` now has one built-in deterministic `System.Text.Json` converter.
  Version 1 persists `formatVersion`, Base64 `bytes`, `contentType`, and
  `encoding` and rejects malformed, duplicate, unknown, or unsupported input.
- `StorageContentEnvelopeCodec` and `SessionContentEnvelopeCodec` were deleted.
  Small record mappers hand typed `FlowContent` to stores and use the shared
  converter for persisted JSON.
- Storage and Sessions continue to read the prior private envelope shape,
  including legacy `JsonElement` records. Exact bytes and metadata round-trip.
- No codec registry, polymorphic persistence abstraction, lazy conversion, or
  alternate content model was introduced.

## Designer Metadata

- Added `OptionDesignMetadataFactory` and `ResourceDesignMetadataFactory` for
  repeated option and host-owned resource shapes.
- Seventeen composition metadata providers now share bounded-capacity and
  representative text, number, selector, clock, store, client, and delegate
  construction where their emitted contracts are identical.
- Providers remain explicit about node types, names, defaults, required flags,
  ordering, ports, sections, editor hints, and dynamic metadata. No metadata
  DSL, reflection, discovery, source generation, or registration framework was
  added.
- Added test-only `ComponentDesignMetadataAssertions` and migrated
  representative provider suites while retaining component-specific expected
  values. Catalog and convention tests prove emitted metadata is unchanged.

## Processing Configuration

- Added internal `CompositionProcessingConfiguration` as the single mapping
  point from semantic `processing.profile` settings to Dataflow capacity,
  parallelism, and ordering.
- `CompositionNodeFactoryContext` and Designer compatibility filtering share
  the centralized technical option names.
- Case-insensitive direct C# overrides for `BoundedCapacity`,
  `MaxDegreeOfParallelism`, and `EnsureOrdered` remain accepted and take
  precedence as before, but canonical JSON and normal Designer editing continue
  to prefer semantic profiles.
- `InputType`, `OutputType`, `LeftInputType`, and `RightInputType` remain
  diagnostic/direct-C# compatibility metadata. They do not select CLR types,
  trigger reflection, or perform conversion. Removal remains a separate
  breaking decision.

## Stateful Runtime Decomposition

- `ApplicationOutputPort<T>` now delegates receive registrations/waiters,
  output links/routes, and revision attachments to focused internal types. The
  port remains the lifecycle and publication owner.
- Correlation and Join delegate pending queues, capacity accounting, stale
  deadlines, expiration selection, and next-due calculation to typed internal
  stores. The nodes retain selectors, timers, command ordering, emissions,
  diagnostics, completion, and lifecycle ownership.
- New focused tests cover capacity release, FIFO matching, stale reused-key
  deadlines, and exact-once forced expiration.
- `ApplicationPortRuntime` and `ApplicationInputPortCore` were reviewed and
  retained because their remaining input pump, revision, lookup, and lifecycle
  state is cohesive. A mechanical file split would not remove behavior.
- `PendingExchangeCoordinator` and resilience primitives remain the shared
  implementations only where timeout, duplicate, late-result, and ownership
  semantics already match. Join, Correlation, RequestReply, Retry, and MQTT
  acknowledgement were not forced into one generic coordinator.

## Package Versions

The additive contracts use minor versions:

- `FluxFlow.Data` `2.1.0`.
- `FluxFlow.Components.Designer` `3.1.0`.

Behavior-preserving internal changes use patch versions:

- `FluxFlow.Nodes` `3.0.1`, `FluxFlow.Composition` `4.0.1`, and
  `FluxFlow.Engine` `4.0.1`.
- FileSystem, Observability, Routing, Sessions, and Storage runtime packages
  `6.0.1`.
- Assertions, Expectations, FileSystem, HTTP, Mapping, Observability, Routing,
  Sessions, Sources, State, Storage, Timers, and Validation Composition
  packages `4.0.1`.
- Metrics, Payloads, Projections, and Serialization Composition packages
  `3.0.1`.

Package release notes, applicable READMEs, the top-level changelog, flow-data
guidance, composition guidance, and the component coverage matrix were updated.

## Verification

- Phase-focused suites passed for Nodes, Data, Composition,
  Composition.Hosting, Engine, Storage and adapters, Sessions, FileSystem,
  Observability, Serialization, Timers, Routing, Designer, all affected
  Composition packages, and Release.Tests.
- Complete Release solution sweep: 1,719 tests passed in 65 projects with zero
  warnings.
- Final Release.Tests: 99 passed with zero warnings.
- Controlled Debug and Release builds: 137 projects, zero errors, zero warnings
  in each configuration.
- Reviewed source-declaration baselines passed. Engine and Routing fingerprints
  changed only because public members moved between internal implementation
  types; Designer contains the reviewed additive public factories.
- A temporary source outside the repository was seeded with all 62 current
  package artifacts.
- All 27 changed packages passed release preflight, SDK binary compatibility
  validation against their preceding published versions, and package dry-run
  consumer checks against the complete temporary source plus the public feed.

## Deferred Boundaries

- No Gate, supervision, polling/latest-value API, cyclic data execution, hot
  reload, renderer UI, RabbitMQ, or new component feature was added.
- No general pipeline framework, codec subsystem, metadata DSL, custom
  container, reflection discovery, or universal pending-state coordinator was
  introduced.
- Further removal of technical type/configuration compatibility requires a
  separate breaking plan with deterministic replacement behavior.

## Commits

1. `db6b143 Simplify shared node execution`
2. `a8693ab Normalize persisted flow content`
3. `a2811c4 Consolidate designer metadata construction`
4. `f3004f9 Clarify processing configuration boundaries`
5. `97b8dc8 Decompose stateful runtime internals`
6. `Record second framework simplification round` closes package metadata,
   documentation, memory, Graphify, and verification.

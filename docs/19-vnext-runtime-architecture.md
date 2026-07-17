# vNext Runtime Architecture

Status: accepted direction, implemented incrementally.

This record defines the target architecture for the next major FluxFlow line.
The data foundation and canonical definition/address phases are implemented
locally. Link compilation, runtime updates, and MQTT redesign remain pending.

## Package Ownership

- `FluxFlow.Data` owns transport-neutral values, content, and result contracts.
- `FluxFlow.Nodes` owns `FlowMessage<T>` and standalone Dataflow node plumbing.
- `FluxFlow.Composition` owns the canonical application document and address
  resolver. Binding, link normalization, and canonical static validation are
  the next bounded Composition milestones.
- `FluxFlow.Engine` will execute compiled compositions and own stable ports,
  direct port interaction, runtime revisions, system events, and diagnostics.
- `FluxFlow.Composition.Hosting` will own definition sources, DI provider
  snapshots, transactional updates, and hosted lifecycle.
- Component runtime packages remain usable without Composition or Engine.
- Concrete adapter packages translate public contracts to private client
  library types and own those library-specific lifetimes.

The existing public definition models in Composition and Engine overlap. The
new flat definition is introduced in Composition. Engine's duplicate
definition model is removed only in an Engine major release, after a legacy
reader exists and the canonical Composition model is proven.

## Runtime Invariants

- TPL Dataflow remains the internal push-processing mechanism.
- Input capacity is finite. A full or unavailable target rejects new work as a
  normal runtime outcome rather than allowing unbounded memory growth.
- Outputs fan out to every matching link. One failed target does not stop its
  siblings.
- A shared input is never completed by one individual upstream link.
- Port addresses and subscriptions remain stable while component revisions
  attach and detach behind them.
- Messages accepted by an old revision finish there. Messages still in the
  stable mailbox are dispatched to the active revision.
- Ordinary component, resource, workflow, and link failures do not terminate
  the host. The application can remain running in a degraded state.
- Expected operation failures are data on the normal output, not a universal
  error port.

## Definition Boundary

The canonical document has exactly `Resources` and `Workflows` at the root.
Workflow objects directly contain components. Resource groups are namespace
objects without `Type`; resource leaves require `Type`. Component settings,
resource references, and port links are flat properties.

`FluxFlow.Composition.Model` now implements this document boundary, and
`FluxFlow.Composition.Addressing.ApplicationAddress` implements the shared
ordinal, case-sensitive address value for local workflow ports, absolute
workflow ports, nested resources, and system streams. Runtime APIs, Designer
persistence, and keyed DI will adopt the same value in later milestones. Names
containing dots are invalid.

Links may be declared once on either an input or output property as a string,
an array, or an object containing `Port` and optional `Condition`. Metadata
determines direction. The compiler will normalize both forms into one internal
source/target link representation before validating types, duplicates,
conditions, exclusive claims, and cycles.

## Runtime Updates

An update will parse and validate a complete candidate, compute its dependency
closure, build and start replacements away from routing, pause only affected
mailbox dispatchers, atomically swap one immutable routing/resource snapshot,
resume dispatch, and drain the old revision. Failure before activation leaves
the old revision unchanged.

Standard DI remains the activation and ownership mechanism. Packages register
explicitly through `IServiceCollection`; no assembly scanning, reflection
discovery, arbitrary provider merging, or parallel registration framework is
introduced.

## Delivery Sequence

1. Data, envelope identity, and result contracts. Complete locally.
2. Canonical Composition definitions and addressing. Complete locally.
3. Link normalization and condition compilation. Next.
4. Stable ports and direct send/receive/observe APIs.
5. Fault isolation, system events, and diagnostics.
6. DI resource snapshots and transactional revisions.
7. MQTT as the first complete resource/component/adapter vertical slice.
8. Remaining component families, Designer, hosting, and coordinated releases.

Supervision, polling or latest-value APIs, durable mailboxes, broker clusters,
automatic mapper insertion, custom containers, and cyclic graphs remain
explicitly deferred.

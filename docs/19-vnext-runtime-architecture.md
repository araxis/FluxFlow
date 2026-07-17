# vNext Runtime Architecture

Status: accepted direction, implemented incrementally.

This record defines the target architecture for the next major FluxFlow line.
The data foundation, canonical definition/address, link compilation, and stable
port phases are implemented locally. System diagnostics, runtime updates, and
MQTT redesign remain pending.

## Package Ownership

- `FluxFlow.Data` owns transport-neutral values, content, and result contracts.
- `FluxFlow.Nodes` owns `FlowMessage<T>` and standalone Dataflow node plumbing.
- `FluxFlow.Composition` owns the canonical application document, address
  resolver, link normalization, compile-once conditions, and canonical static
  link validation. Runtime activation remains an Engine responsibility.
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
workflow ports, nested resources, and system streams. Engine stable-port APIs
now use the same value. Designer persistence and keyed DI will adopt it in later
milestones. Names containing dots are invalid.

Links may be declared once on either an input or output property as a string,
an array, or an object containing exact `Port` and optional `Condition` names.
Metadata determines direction. `ApplicationLinkCompiler` normalizes both forms
into absolute source/target links, preserves declaration side, compiles each
condition string once per activation, and validates exact types, duplicates,
explicit single-link claims, and cycles. Engine-owned system streams supply
their payload metadata to the compiler rather than creating an Engine
dependency in Composition.

## Stable Port Runtime

`FluxFlow.Engine.Ports` owns additive address-stable input mailboxes and output
broadcast hubs. Hosts register exact payload types with
`ApplicationPortRuntimeBuilder`, attach component
`ITargetBlock<FlowMessage<T>>` and `ISourceBlock<FlowMessage<T>>` instances
behind those addresses, and activate `CompiledApplicationLink` values without
reparsing definitions or recompiling conditions.

Input intake reports accepted, full, unavailable, and completed states without
waiting for component capacity. During replacement, the dispatcher pauses,
allows a message already claimed by the old target to finish, swaps the target,
then sends stable-mailbox work to the new target. A stale attachment lease
cannot detach a newer revision. Rejected target delivery retains the claimed
message for a later attachment.

Outputs broadcast each message to all compiled links, one-shot receives, and
bounded observations. Link conditions receive `input`, `payload`, and `message`
variables. Condition exceptions, full or unavailable targets, source faults,
and overflowing observations are isolated from siblings and reported through a
bounded best-effort rejection stream. Source completion only detaches that
source; it never completes the stable output or shared downstream input.

`SendAsync`, `ReceiveAsync`, `ObserveAsync`, and `SendAndReceiveAsync` use the
same canonical addresses. Receive and observation do not steal workflow data;
request/reply installs a `TraceId` waiter before input acceptance. Expected
availability and timeout states are result values while caller cancellation is
still cancellation. The rejection stream is a milestone-local precursor, not
the final system-event or diagnostics model.

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
3. Link normalization and condition compilation. Complete locally.
4. Stable ports and direct send/receive/observe APIs. Complete locally.
5. Fault isolation, system events, and diagnostics. Next.
6. DI resource snapshots and transactional revisions.
7. MQTT as the first complete resource/component/adapter vertical slice.
8. Remaining component families, Designer, hosting, and coordinated releases.

Supervision, polling or latest-value APIs, durable mailboxes, broker clusters,
automatic mapper insertion, custom containers, and cyclic graphs remain
explicitly deferred.

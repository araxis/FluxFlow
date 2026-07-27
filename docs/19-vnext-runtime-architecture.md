# Runtime Architecture

FluxFlow is a general-purpose workflow toolkit whose normal execution model is
typed, push-based TPL Dataflow. Standalone nodes are the foundation;
Composition, Hosting, Engine, Designer, and transport adapters are optional
layers with explicit ownership boundaries.

## Layering

```text
Typed component nodes
    -> optional Composition registration and Designer metadata
    -> optional hosted application revisions
    -> optional Engine activation, stable ports, and system signals
    -> host-owned clients, stores, clocks, secrets, and adapters
```

`FluxFlow.Nodes` contains the transport-neutral raw content and error contracts,
under their retained `FluxFlow.Data` namespace, together with the typed workflow
envelope and Dataflow node lifecycle.
Components do not need Engine, and Engine does not own component resources.

## Canonical Application

The persisted root has exactly two object sections:

```json
{
  "Resources": {},
  "Workflows": {}
}
```

Resource, workflow, and component identity comes from object keys. Components
are direct properties of their workflow. Options and resource references are
flat component properties. No maintained `Configuration`, `Composition`,
`Nodes`, or root `Links` wrapper exists.

`ApplicationAddress` is ordinal and case-sensitive. `Component.Port` is local
to one workflow, `Workflow.Component.Port` is cross-workflow, and
`Resources.Group.Resource` addresses nested resources.

Links may be declared from an output or into an input as a string, array, or
`{ "Port", "Condition" }` object. Fan-in, fan-out, conditional routing, and
cross-workflow links compile into one canonical model. Ordinary data cycles are
rejected. Feedback into explicitly registered bounded signal ports is a signal
relation, not a data-processing cycle.

## Typed Message Processing

Each port declares its actual `FlowMessage<T>` type. A message contains either
T or `FlowError`; there is no nested result wrapper and no universal error port.
Trace identity remains stable through a lineage while each emitted message gets
a new message identity and immediate causation.

Known commands, results, and events remain CLR records. Explicit JSON nodes use
detached `JsonElement`. Exact transport bodies use `FlowContent`. Dynamic CLR
objects are mapper outputs only when a workflow explicitly requests them.

Dataflow inputs provide bounded buffering and semantic processing profiles map
user-facing mode/order/buffer choices to technical block settings. Outputs
broadcast accepted messages. Immutable payloads are shared rather than cloned.

## Runtime Failure Model

Per-message failures normally become `FlowError` on Output so workflows can
route, retry, persist, or inspect them. Expected negative domain outcomes stay
typed values. Incoming errors bypass ordinary business operations and are
propagated to the output type.

`Events` carries diagnostic and lifecycle observations. `Completion` reports
block lifecycle and unrecoverable invariant/infrastructure faults. One
component fault does not define application host lifetime. Broad supervision
is intentionally outside this architecture pass.

## Composition and Revisions

Composition registers factories explicitly; there is no reflection discovery
or assembly scanning. Every active component family owns one authoritative
`*ComponentDefinition`; its descriptor declares exact types, ports, options,
resources, and activation, while an exact `ComponentDesignDeclaration` pairs
that descriptor with presentation metadata. There is no parallel metadata
provider or split identity registry. Host-owned resources are resolved by exact
keyed DI addresses.

Composition also owns the complete canonical link grammar. One compiler pass
parses declarations, resolves `ApplicationAddress` values, validates structure,
types, exclusivity, conditions, and cycles, and emits both executable links and
immutable `ApplicationLinkDeclarationProjection` values for persistence.
Designer maps those projections and serializes through Composition; Engine
consumes the public compiled result. Neither assembly has production friend
access to Composition internals.

Hosting prepares a complete candidate revision using an immutable service
provider snapshot. Component add/update/remove and port-surface changes are
validated before commit. A successful candidate is published atomically and
the old revision drains afterward. A failed candidate leaves the active
revision untouched.

Shared fan-in inputs complete after every upstream succeeds and fault once on
the first upstream failure. Disposal attempts every link, node, and owned scope,
then aggregates cleanup failures without duplicating runtime completion faults.

## Engine Responsibilities

Engine provides canonical application preparation, resource/component
activation, complete link binding, stable addressable input/output/signal
ports, revision generations, system events, diagnostics, and rollback. It is
not a broker, web server, storage provider, expression engine, or component
container.

Diagnostic queues are bounded and best effort; accepted diagnostics preserve
order. System events and accepted workflow messages retain their stronger
delivery guarantees.

## DI and Resource Ownership

Registration uses standard `IServiceCollection`, keyed services, and explicit
provider snapshots. A host may compose multiple service collections into a
revision provider, but it does not create a provider per message.

Resource ownership remains explicit:

- hosts own externally supplied clients, clocks, credentials, certificates,
  stores, and secrets;
- adapter packages own concrete provider sessions they create;
- component packages own only their node-local Dataflow blocks and state;
- externally supplied resources are non-owning from the workflow runtime;
- disposal follows the scope that created the resource.

## Content and Expression Boundaries

`FlowContent` owns exact bytes plus content type and encoding. Serialization is
performed by explicit JSON/text/Base64 nodes. Decode once before fan-out, or
branch before decoding when exact raw bytes must also continue.

Expression engines receive typed values through `FlowMapContext`. C# engines
can use normal CLR properties and methods; JSON-oriented engines can consume or
project `JsonElement`. An internal read-only dynamic view is allowed only during
expression evaluation and must not leak into the core public model.

## MQTT as a Component Family

MQTT is one optional family in the general engine. Broker resources own endpoint
defaults. Logical client resources own identity, credentials, certificates,
reconnect, autoconnect, subscriptions, and one shared lifecycle. Multiple
clients can use one broker, and multiple components can share one client.

The canonical nodes are command/control, publish, receive, and client events.
Command results stay on the control output; received application messages stay
on the receive output. Workflow Ack/Nak signal coordination is distinct from
broker acknowledgement. Concrete transport adapters own provider sessions,
while core MQTT owns policy and lifecycle.

## Runtime Modification

Applications may add, remove, or update workflows, components, links, and
resources through transactional revisions. Existing direct port handles remain
stable where their address and contract remain compatible. Resource revisions
are isolated through provider snapshots.

## Explicit Non-goals

This architecture does not add polling/latest-value APIs, durable mailboxes,
automatic mapper insertion, arbitrary cyclic data execution, reflection
registration, a custom DI container, per-message providers, universal payload
cloning, preview language unions, a universal dynamic object, or broad
application supervision. Each requires a separate behavior contract.

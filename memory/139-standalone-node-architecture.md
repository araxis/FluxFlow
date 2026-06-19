# 139 - Standalone node architecture (kit + envelope + request/reply)

Date: 2026-06-18

Status: migration COMPLETE on branch `work/http-simplify` (as of 2026-06-19). NOT
merged to main, NOT published — versioning/publishing of this new major line is a
pending owner decision. A deliberate re-architecture after the 2.0 GA cut
([[138-2.0-ga-remediation-and-cut]]) that supersedes the engine-coupled component
model. HTTP was the template; all 18 dataflow-node component packages are now
engine-free on the kit, and the engine is an optional runtime (only the infra
packages `Designer` and `Journal` still reference it).

## The owner's principle (why this exists)

The project is about building **nodes that connect to each other**, not a framework.
A node must be a self-contained dataflow processor that works out of the box with
**no engine**: `new SomeNode(...)`, post input, link output — done. Complexity is
delegated to .NET (`System.Threading.Tasks.Dataflow`) and to the real library for the
job (`HttpClient` for outbound HTTP, the host's server for inbound, an MQTT client,
JSONata/C# for mapping). Composing a workflow (read config, create nodes, link them)
is a **separate layer**. The previous design inverted this: nodes derived from the
engine's `FlowNodeBase`, referenced `FluxFlow.Engine`, and the fan-out lived in the
engine's `OutputPort` — so a node could not run without the engine.

## Three layers

1. **`FluxFlow.Nodes` (the kit)** — the one tiny shared piece (~5 files). Everything
   else is `new` + `LinkTo`.
2. **Component nodes** — self-contained TPL Dataflow processors that depend only on the
   kit + their delegated library. No engine.
3. **Composition/host (optional)** — config -> `new` nodes -> `LinkTo`; endpoint/trigger
   wiring. This is where today's engine becomes an *optional* runtime, not a prerequisite.

## The kit (`FluxFlow.Nodes`, v0.1.0)

- `FlowNode<TInput, TOutput>`: base node. Input is a **bounded `BufferBlock`**
  (backpressure on intake); Output/Errors/Events are **`BroadcastBlock`** (fan-out,
  latest-wins, no backpressure). `ProcessAsync(FlowMessage<TInput>)`; a handler throw
  is caught and surfaced on `Errors` (stamped with the in-flight correlation id), never
  a dead pump. `Complete`/`Fault`/`Completion`/`DisposeAsync` lifecycle.
- **Broadcast, not lossless fan-out** (owner decision): `BroadcastBlock` is built-in and
  simple; a consumer that keeps up sees every message, one that lags may miss some.
  "No-loss" is handled case-by-case by a node that needs it (the request/reply trigger
  is the first — its request `Output` is a reliable bounded buffer, not broadcast).
- `FlowMessage<T>` envelope: **every inter-node message** carries a `CorrelationId` +
  `Payload` (+ per-hop `MessageId`, `Timestamp`, `Headers`). Immutable (safe to
  broadcast). `With<TOut>(payload)` preserves the id + headers, so correlation flows
  through a graph with no node copying it. `FlowMessage.Create` mints the first one.
- `CorrelationId`: guarded `readonly record struct` (non-empty), string-backed (a host
  can flow an existing trace/request id; `New()` = GUID), structural equality (keys the
  request/reply in-flight map directly), and a `JsonConverter` so envelopes persist as a
  bare string. `FlowError`/`FlowEvent` carry a nullable `CorrelationId`.

## Components reworked on the kit

- **`FluxFlow.Components.Http`** (was engine-coupled): now ONE node, `HttpClientNode :
  FlowNode<HttpRequestInput, HttpResponseOutput>` — a "blockified" injected `HttpClient`.
  References only the kit. Deleted: the `http.client` connection-resource node + state
  machine, the sender-factory layer, the in-node SSRF guard (now a `DelegatingHandler`
  concern on the injected client), and the engine glue (factory/module/types/registration/
  design-metadata). All transport policy lives on the injected `HttpClient`.
- **`FluxFlow.Components.RequestReply`** (new): `RequestReplyCoordinator<TRequest, TResponse>`
  bridges request/reply onto a one-way graph. Host feeds `IRequestContext` (request +
  `ReplyAsync`/`FailAsync` to the real transport) into `Incoming`; the bridge mints/honours
  a `CorrelationId`, holds the context in-flight, emits `FlowMessage<TRequest>` on a
  **reliable bounded** `Output`; the graph returns `FlowMessage<TResponse>` (same id, via
  `With`) to `Responses`; the bridge matches and replies. A `TimeProvider` sweep fails +
  evicts timed-out requests (no leak, no hung caller). Transport-agnostic.
- **`FluxFlow.Components.Http.AspNetCore`** (new): the ONLY ASP.NET-aware package. The
  trigger is a component (`HttpTriggerNode`) that is *given* its inbound request source
  (injected, keyed) and uses a `RequestReplyCoordinator` internally — the host does not
  hand-wire a coordinator. `AddFluxFlowHttpTrigger(name, configure)` registers a keyed
  request source + the trigger + a hosted service (wires the graph in `configure`);
  `MapFluxFlowTrigger(pattern, name)` feeds the keyed source (a `MapFluxFlowTrigger(
  pattern, coordinator)` overload remains for DI-less/tests). `HttpRequestContext` writes
  the correlated reply (or 504/500/503) back, holding the response open until the graph
  answers. (Trade noted: resolving the keyed source in the endpoint is a mild
  service-locator seam — accepted for DI-first host ergonomics.)
- **MQTT request/reply** (the SAME bridge driving MQTT): `MqttRequestContext` publishes
  the reply to the MQTT5 response topic (echoing correlation data) via a host-supplied
  `IMqttResponsePublisher`. The transport-neutrality proof: bridge + `IRequestContext` +
  envelope reused verbatim across HTTP and MQTT. Unlike HTTP — where inbound (ASP.NET Core)
  and outbound (`HttpClient`) are different heavy deps, so `Http`/`Http.AspNetCore` split —
  MQTT uses one client library for everything, so this trigger lives **inside the single
  `FluxFlow.Components.Mqtt` package** (connection + publish + subscribe + trigger), not a
  separate package.

## Triggers (the inbound shape)

A trigger is NOT a server. The host owns the server/broker (Kestrel, an MQTT client) and
feeds requests in via an `IRequestContext` whose `ReplyAsync` writes to the real transport.
The bridge implements request/reply over the dataflow realm by correlating on
`CorrelationId`. The reply travels back as a normal `FlowMessage` (the id is echoed by the
handler via `With`), never as a delegate threaded through the graph.

A trigger's core job is **correlate + publish**; awaiting a reply is optional. Two modes
(`RequestReplyOptions.Mode`): **RequestReply** (publish, hold in-flight, await the correlated
response, reply, time out) and **FireAndForget** (publish into the graph, acknowledge the
caller immediately, no in-flight/timeout/sweep). The ack is transport-specific via
`IRequestContext.AcknowledgeAsync` — HTTP writes `202 Accepted`, MQTT sends nothing. A late
response to a fire-and-forget request is reported `Unmatched`.

Triggers are first-class nodes: `HttpTriggerNode` and `RequestReplyCoordinator<,>`
implement `IFlowNode` (`Completion`/`Complete`/`Fault`/`DisposeAsync`), so a host drives a
trigger with the same lifecycle as any node. The coordinator's `Fault` fails in-flight
callers (no hung request), faults its data blocks so `Completion` surfaces the fault, and
flushes Errors/Events per the kit rule.

## Kit extensions added during the full migration

The kit grew (still tiny) to cover every node shape the migration needed:
- `AddOutput<T>()` — extra **domain** output ports beyond the primary `Output` (e.g.
  Mapping `Output`+`Failed`, Control `When`→`WhenTrue`/`WhenFalse`, Assertions
  `Result`/`Passed`/`Failed`, Validation `Valid`/`Invalid`). Errors/Events were always
  there; this is for *data* fan-out.
- `FlowSource<TOutput>` — zero-input producers (`StartAsync`→`RunAsync(ct)`, `Emit`,
  `OnDisposeAsync`, stop on `Stopping`): Timers interval/schedule, FileSystem
  enumerate/watch, Sessions replay, Sources generated/sequence, MQTT subscribe.
- `OnInputCompletedAsync()` drain hook — flush work a node held back, after the input
  drains and before outputs complete (debounce's pending item, Metrics' coalesced final
  snapshot, the Timers delay line). Removed the fragile `new`-hiding of `Complete`/`Dispose`.
- **Fault rule**: on fault the DATA outputs Fault, but Errors/Events are **Completed
  (flushed)** — faulting a `BroadcastBlock` discards its buffered message, which would drop
  the very `FlowError` a consumer needs. (Bug found + fixed mid-migration.)

`IFlowNode`/`IFlowSource` interfaces unify lifecycle. The 2-input `FlowJoin` is hand-rolled
on these primitives (no speculative 2-input base).

## FluxFlow.Mapping (extracted)

The expression/mapping abstraction (`IFlowExpressionEngine`, `IFlowMapper`,
`IFlowPredicate`, `FlowMapContext`, the Expression/Delegate adapters, …) was moved out of
`FluxFlow.Engine` into a leaf package **`FluxFlow.Mapping`** (`0.1.0`) so the
expression-using components (Mapping/Control/Assertions/Observability/State/Routing) and
`FluxFlow.Components.Expressions` go engine-free; the engine references it instead.

## Migration outcome

All 18 dataflow-node packages migrated engine-free across waves: Wave 1 (Metrics, Payloads,
Projections, Validation, Serialization, Expectations), Wave 2 (Mapping, Control, Assertions,
Observability, State), Wave 3a (Timers, FileSystem, Sessions), Wave 3b (Routing, Storage,
Sources), Wave 3c (Mqtt — connection-node dispose-race hardening ported verbatim). The 5
engine-based composition samples (MappingControl, Mqtt, State, Sessions, Storage) were
retired (the standalone model is shown by `HttpTriggerSample`); their memory records too.
The Designer per-component coverage test went away with the providers (Designer package +
its catalog test remain). Inter-component coupling was only Expectations→Projections.

An **adversarial review+verify pass** (per-package reviewers → skeptical verification) found
3 real regressions the agent migration introduced, all fixed + regression-tested: Metrics
final-snapshot drop under load (→ drain hook), Timers delay accumulating per item instead of
constant-offset-from-arrival (→ restored 2-stage stamp+delay-line), Sessions replay dropping
the `ReplayFailed` error on mid-stream store failure (→ wrapped the read loop).

## Verification + state

Full solution green at **741 tests, 0 warnings**; flake-prone suites survive heavy
oversubscription stress. Real-server end-to-end proven via ASP.NET Core TestServer. New
packages in `eng/packages.json` + CHANGELOG at `0.1.0` (Nodes, Mapping, RequestReply,
Http.AspNetCore); the MQTT trigger is folded into `FluxFlow.Components.Mqtt` (no separate
package); migrated components keep their current versions (unflipped, unpublished).

## Open / next (owner decisions)

- **Versioning/publishing** of this new major line — still deferred; the whole branch is
  unpublished. Needs an explicit call on version numbers + what to publish.
- **PR/merge** `work/http-simplify` → `main`.
- The engine is now an optional Layer-3 runtime. A generic "register a `FlowNode` into the
  engine" adapter (instead of the deleted per-component glue) is the future composition
  piece; `Designer` (visual-designer `NodeType`/`PortName`) and `Journal` (engine
  `FlowEvent`→record mapper) are the only infra packages still engine-coupled.

Builds on [[135-architecture-review-and-roadmap]] and the 2.0 line in
[[138-2.0-ga-remediation-and-cut]].

# 139 - Standalone node architecture (kit + envelope + request/reply)

Date: 2026-06-18

Status: in progress on branch `work/http-simplify`. NOT merged to main, NOT
published. This is a deliberate re-architecture explored after the 2.0 GA cut
([[138-2.0-ga-remediation-and-cut]]); it supersedes the engine-coupled component
model for the packages reworked so far (HTTP first as the template).

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
- **`FluxFlow.Components.RequestReply`** (new): `RequestReplyBridge<TRequest, TResponse>`
  bridges request/reply onto a one-way graph. Host feeds `IRequestContext` (request +
  `ReplyAsync`/`FailAsync` to the real transport) into `Incoming`; the bridge mints/honours
  a `CorrelationId`, holds the context in-flight, emits `FlowMessage<TRequest>` on a
  **reliable bounded** `Output`; the graph returns `FlowMessage<TResponse>` (same id, via
  `With`) to `Responses`; the bridge matches and replies. A `TimeProvider` sweep fails +
  evicts timed-out requests (no leak, no hung caller). Transport-agnostic.
- **`FluxFlow.Components.Http.AspNetCore`** (new): the ONLY ASP.NET-aware package.
  `MapFluxFlowTrigger` maps an endpoint to the bridge; `HttpRequestContext` writes the
  correlated reply (or 504/500/503) back. Held open until the graph answers.
- **`FluxFlow.Components.Mqtt.RequestReply`** (new): the SAME bridge driving MQTT, with
  no MQTT-library dependency — `MqttRequestContext` publishes the reply to the MQTT5
  response topic (echoing correlation data) via a host-supplied `IMqttResponsePublisher`.
  This is the transport-neutrality proof: bridge + `IRequestContext` + envelope reused
  verbatim across HTTP and MQTT.

## Triggers (the inbound shape)

A trigger is NOT a server. The host owns the server/broker (Kestrel, an MQTT client) and
feeds requests in via an `IRequestContext` whose `ReplyAsync` writes to the real transport.
The bridge implements request/reply over the dataflow realm by correlating on
`CorrelationId`. The reply travels back as a normal `FlowMessage` (the id is echoed by the
handler via `With`), never as a delegate threaded through the graph.

## Verification + state

Full solution green at 731 tests, 0 warnings. New packages registered in
`eng/packages.json` + CHANGELOG (kit/RequestReply/Http.AspNetCore/Mqtt.RequestReply at
`0.1.0`; Http rebuilt, version unflipped). Real-server end-to-end proven via ASP.NET
Core TestServer.

## Open / next

- The other ~27 components are still engine-coupled (old model). Rolling the kit +
  envelope across them is the large mechanical migration that makes the repo consistent;
  it is the point-of-no-return on this architecture and is gated on an explicit decision.
- Versioning/publishing deferred: this is a new major line for the reworked packages.
- The engine becomes an optional Layer-3 runtime; a generic "register a FlowNode into the
  engine" adapter (instead of per-component glue) is the future composition piece.

Builds on [[135-architecture-review-and-roadmap]] and the 2.0 line in
[[138-2.0-ga-remediation-and-cut]].

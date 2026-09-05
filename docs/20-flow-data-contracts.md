# Flow Data Contracts

These contracts ship in the `FluxFlow.Nodes` package and assembly. The
`FluxFlow.Data` namespace is retained so source code does not need gratuitous
renames; namespace ownership does not imply a separate package or assembly.
The former `FluxFlow.Data` package is retired without a forwarding package or
type forwarding.

FluxFlow uses `FlowValue` as the canonical structured value exchanged by
runtime-authored ordinary data ports. Direct code-first nodes remain generic
and keep the narrow CLR contracts they own. `FlowContent` represents exact
transport bytes inside values that require byte fidelity. `FlowMessage<T>` adds
workflow identity and carries either the declared value or `FlowError`.

## Representation Boundaries

| Need | Contract |
|------|----------|
| Runtime-authored normal-data link | `FlowValue` |
| Direct code-first node | A typed immutable CLR record or scalar |
| Module implementation | A module-owned request/result type materialized from or wrapped as `FlowValue` |
| Schema-less expression implementation | A detached `JsonElement` behind the runtime boundary |
| Exact transport body | `FlowContent` nested in the owning module contract |
| Processing failure | `FlowError` inside `FlowMessage<T>` |

`FlowValue` is deliberately small: it owns one detached JSON value and supports
serialization/deserialization mechanics. It is not a registry, dynamic proxy,
or God object that understands HTTP, MQTT, storage, or any other component.

## Module-Owned Materialization

Every runtime component that needs a typed input supplies an
`IFlowValueMaterializer<T>` from the module that owns `T`. A materializer checks
the structural input it accepts, normalizes module-specific conveniences, and
returns either the typed request or a stable module-owned `FlowError`.

For example, the HTTP module accepts an object with request fields and owns the
conversion to `HttpClientRequest`; MQTT independently owns conversion to
`MqttPublishMessage`. The framework never scans for conversions and never tries
to map arbitrary types to arbitrary types. Runtime mapper components transform
`FlowValue` to `FlowValue` explicitly in workflow topology.

## Discoverable Input Shapes

`IFlowValueMaterializer<T>.InputShape` describes accepted top-level JSON kinds and
required object property names. Declare that same immutable `FlowValueShape` with
`HasFlowValueInput(name, selector, materializer.InputShape)` when registering a
canonical node. Instance factories use `HasFlowValueInput(name, inputShape)`.
Both runtime `ComponentPortMetadata.InputShape` and designer
`PortDesignMetadata.InputShape` retain the declaration. These are destination
contracts; they do not change the canonical `FlowMessage` connection type.

Hosts can call `descriptor.ValidateInputShape("Input", sample)` before execution.
The result is `null` when no shape was declared, a failed result for a definite
structural mismatch, or success when the sample satisfies the declared top-level
checks. No factory, selector, materializer, or workflow is executed by this check.

Required property names are compared case-insensitively, matching the request
materializers' web JSON conventions. Presence alone is checked: nested fields,
property value types, null property values, defaults and domain rules remain the
destination's responsibility. A shape that accepts the JSON null kind accepts
`FlowValue.Null`; an absent shape is metadata uncertainty, not null message data.

Shapes round-trip through JSON. Equivalent declarations compare accepted kinds
and required properties as sets while retaining the description as part of
registration identity. Registering a changed shape for an existing component
type is a conflict.

Graph compilation cannot infer future values from an output's `FlowValue` type.
It therefore preserves dynamic links in both C# and JSON definitions. Sample
preflight is opt-in and does not prove that every future message will materialize.

## FlowMessage<T>

`FlowMessage<T>` is a closed, immutable two-case envelope. `IsError == false`
activates `Value`; `IsError == true` activates `Error`. A successful nullable
`T` may contain null because null is not the discriminator. Reading `Value` from
an error message throws, and constructors are private so contradictory states
cannot be created.

```csharp
var received = FlowMessage.Create(
    new OrderReceived("order-42", 125.50m),
    headers: new Dictionary<string, string>
    {
        ["source"] = "orders"
    });

FlowMessage<OrderAccepted> accepted = received.With(
    new OrderAccepted(received.Value.OrderId));

FlowMessage<OrderAccepted> rejected = received.WithError<OrderAccepted>(
    new FlowError(
        "order.invalid",
        "The order is invalid.",
        "validation"));

var text = rejected.Match(
    value => $"accepted:{value.OrderId}",
    error => $"error:{error.Code}");
```

`With` and `WithError` preserve `TraceId`, optional `CorrelationId`, and headers;
they create a new `MessageId`, set `CausationId` to the preceding `MessageId`,
and assign a new timestamp. The optional causation argument exists for an
explicitly known cause. `CorrelationId` is external/business metadata and is
not the workflow trace.

Headers are copied into an immutable ordinal string map. Identifiers and
timestamps remain first-class envelope fields; nested business documents and
transport-specific binary headers do not belong in the generic header map.

## Stable JSON Shape

The message converter writes a fixed expression-friendly shape:

```json
{
  "traceId": "...",
  "messageId": "...",
  "causationId": "...",
  "correlationId": null,
  "timestamp": "2026-07-26T12:00:00Z",
  "headers": { "source": "orders" },
  "isError": false,
  "value": { "orderId": "order-42" },
  "error": null
}
```

An error message has `isError: true`, a null `value`, and a populated `error`.
Deserialization rejects unknown or duplicate properties and contradictory
active cases. This projection is for persistence, diagnostics, and
JSON-oriented expressions; it does not make JSON the in-memory component type.

## FlowError

`FlowError` is transport-neutral workflow data with required `Code`, `Message`,
and `Category`, explicit `IsTransient`, and optional structured `Details`.
Details are cloned when accepted, so they remain valid after a caller-owned
`JsonDocument` is disposed. Raw exceptions do not cross workflow boundaries.

Expected business or protocol variants remain in the component's typed result.
For example, an acknowledged, rejected, not-found, or exhausted result may be a
valid domain outcome. Invalid input, conversion failure, unavailable resources,
or transport execution failure normally become `FlowError`.

Errors use the normal `Output` stream. Ordinary nodes propagate an incoming
error to their output type without running business logic. Routing, retry,
logging, mapping, and recovery nodes may intentionally inspect or transform it.
`Events` remains diagnostics, and `Completion` remains lifecycle state.

## FlowContent

`FlowContent` owns an `ImmutableArray<byte>` plus optional `ContentType` and
`Encoding`. `FromBytes` copies caller-owned memory. There are no codecs, cached
decoded values, alternate byte representations, or hidden conversion state.

```csharp
var content = FlowContent.FromBytes(
    Encoding.UTF8.GetBytes("{\"orderId\":\"order-42\"}"),
    "application/json",
    "utf-8");
```

Its built-in `System.Text.Json` converter uses one deterministic persisted
shape: format version 1, Base64 bytes, content type, and encoding. The converter
is strict about malformed or ambiguous input. Storage and Sessions write this
shared representation directly and retain read compatibility with their
earlier private envelopes.

Conversion is visible in workflow topology through Serialization components:
bytes/text/JSON/Base64 are separate operations. Decode once before fan-out when
several downstream nodes need the same decoded value. Branch before conversion
when both exact raw bytes and a decoded value are required.

## JSON and Dynamic CLR Values

`FlowValue` owns and clones its detached JSON representation. Module internals
may use `JsonElement` where JSON semantics are part of their implementation;
`JsonNode` is not the shared value because it is mutable. Known domain values
stay typed inside direct nodes and behind module boundaries.

A mapper may explicitly return a CLR record, dictionary, or `ExpandoObject`.
After publication, downstream nodes treat that value as owned and immutable by
convention. FluxFlow does not deep-clone arbitrary `T` during broadcast.
Expression adapters may create an internal read-only dynamic view during
evaluation, but that view is not a component, persistence, or core data type.

## Ownership Rules

- Envelope state, headers, `FlowError`, `FlowContent`, and detached JSON are
  immutable at publication.
- Immutable payloads can be shared across Dataflow broadcast branches.
- A component that accepts a mutable user type must document ownership and must
  not mutate shared input unexpectedly.
- Runtime-authored persistence and transport adapters serialize `FlowValue`;
  direct typed adapters serialize their declared contract.
- Component packages remain standalone. Composition describes configuration,
  ports, resources, and Designer hints without owning runtime resources.

Native language unions may replace the private discriminator after a stable
language/runtime feature is available across supported targets. The current
public contract does not depend on preview syntax or a third-party union type.

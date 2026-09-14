# Flow Data Contracts

These types ship in FluxFlow.Core under the FluxFlow.Data and FluxFlow.Nodes
namespaces. Canonical workflow ports carry FlowMessage with one FlowValue payload.

## FlowValue

FlowValue is an abstract class with one public property, `Json`, and one protected
abstract method, `CreateJson()`. JSON is built on first access and cached safely
for concurrent readers. The result is cloned into owned immutable storage.
There are no From, FromJson, Deserialize, Kind or materializer APIs on FlowValue.

A component-owned type can inherit FlowValue and expose its normal C# properties:

```csharp
public sealed class OrderValue(string orderId) : FlowValue
{
    public string OrderId { get; } = orderId;

    protected override JsonElement CreateJson()
        => JsonSerializer.SerializeToElement(this, OrderJsonContext.Default.OrderValue);
}

[JsonSerializable(typeof(OrderValue))]
internal sealed partial class OrderJsonContext : JsonSerializerContext;
```

Every field reachable from a typed value must remain immutable. Copy mutable
collections at construction. The base class cannot enforce deep immutability.
FlowValue is a class, so a record `with` copy cannot accidentally retain another
instance's lazy cache. The inherited Json property is excluded from serialization.

For dynamic JSON use `new JsonFlowValue(element)`. This concrete value clones its
input immediately, before the original JsonDocument can be disposed. Use
`JsonFlowValue.Null` for JSON null. The FlowValue converter also handles a
JsonFlowValue stored inside an object-typed collection and is usable from an
external generated JsonSerializerContext.

## Component-owned conversion

The receiving component selects its own type and serializer metadata:

```csharp
var request = value is OrderValue typed
    ? typed
    : value.Json.Deserialize(OrderJsonContext.Default.OrderValue)
      ?? throw new JsonException("An order is required.");
```

HTTP HttpClientRequest and MQTT MqttPublishMessage implement FlowValue. Their
nodes reuse typed requests directly and otherwise use local generated JSON
metadata and their own content converters. No materializer interface, result
wrapper, global conversion registry or inherited deserializer remains.
FlowContent.FromJson is a narrow shared converter for exact content, plain text
and JSON bytes. It does not select component request types.

Serialization can now fail on first Json access. FlowNode/FlowTransformNode
processing boundaries still catch failures, preserve the original exception for
diagnostics and emit error messages. Component-specific JSON rejection handling
retains its error codes and events. Host code that accesses Json outside those
boundaries owns its error handling. Lazy caches the first conversion exception.
When an Engine output has capture configured, it prepares JSON before capture.
A payload conversion failure is reported with the original exception and becomes
a portable error message, which is captured and delivered. Actual capture/store
failures still fail closed. Outputs without capture do not force JSON.

Typed forwarding does not require JSON. Persistence, expressions or transport
serialization may request it. After JSON roundtrip, an ordinary typed value is
restored as JsonFlowValue: components must accept that path too. No CLR type-name
registry or polymorphic rehydration mechanism is required.

## Canonical FlowMessage and errors

The non-generic FlowMessage stores one FlowValue. FlowError also derives from
FlowValue, so `failure.Value` and `failure.Error` refer to the same error object.
IsError remains the explicit failure discriminator; an ordinary message may
carry a FlowError as data without becoming a processing failure. TryGetValue
continues to report only the successful case; TryGetError reports failures.

```csharp
FlowValue request = new HttpClientRequest { Url = "https://example.test/" };
var message = FlowMessage.Create(request);
var failure = message.WithError(new FlowError("invalid", "Invalid request", "HTTP"));
```

Use a FlowValue-typed variable/cast with FlowMessage.Create for a canonical message;
the existing generic overload still creates FlowMessage<T> for a concrete T.
The existing persisted envelope format retains its value/error slots and IsError
flag; this change does not invalidate durable records. Original Exception objects
remain diagnostic data and are not attached to the portable FlowError.

The generic envelope remains for existing typed adapters and component internals:

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

# Flow Data Contracts

These types ship in FluxFlow.Core under the FluxFlow.Core.Messaging
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
Content conversion belongs to the component that consumes those bytes; the
minimum Core does not prescribe a transport content model.

Serialization can now fail on first Json access. FlowNode processing helpers
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
var failure = message.WithValue(new FlowError("invalid", "Invalid request", "HTTP"));
```

FlowMessage.Create returns a typed message for a concrete FlowValue subtype.
It inherits FlowMessage, so no cast or conversion method is needed to send it
through a canonical port. The original payload instance is retained.
The persisted envelope retains its `Value`/`Error` slots and `IsError` flag.
Records using the previous property names require migration as described below.
Original Exception objects remain diagnostic data and are not attached to the
portable FlowError.

The generic envelope remains for existing typed adapters and component internals:

## FlowMessage<T>

`FlowMessage<T> : FlowMessage where T : FlowValue` is a typed view of the shared
immutable envelope. T must implement the FlowValue contract; string, int and
arbitrary DTOs cannot be used directly. Success requires a non-null FlowValue;
use JsonFlowValue.Null for JSON null. Reading an error through an incompatible
typed Value (for example OrderAccepted) throws; FlowMessage<FlowError>.Value and
the common FlowMessage.Value expose the error. Identity, headers, error state
and wire serialization are shared.
In the example below, OrderReceived and OrderAccepted are component-owned
FlowValue subclasses with their own CreateJson implementations.

```csharp
var received = FlowMessage.Create(
    new OrderReceived("order-42", 125.50m),
    headers: new Dictionary<string, FlowValue>
    {
        ["source"] = "orders"
    });

FlowMessage<OrderAccepted> accepted = received.WithValue(
    new OrderAccepted(received.Value.OrderId));

FlowMessage<FlowError> rejected = received.WithValue(
    new FlowError(
        "order.invalid",
        "The order is invalid.",
        "validation"));

var text = rejected.Match(
    value => $"value:{value.Code}",
    error => $"error:{error.Code}");
```

`WithValue` preserves `TraceId`, optional `CorrelationId`, and headers;
it creates a new `MessageId`, sets `CausationId` to the preceding `MessageId`,
and assigns a new timestamp. The optional causation argument exists for an
explicitly known cause. `CorrelationId` is external/business metadata and is
not the workflow trace.

`FlowError` always marks a processing error, including when passed as FlowValue.
Use an ordinary payload such as JsonFlowValue(error.Json) when an error document
must be handled as business data rather than trigger error forwarding. This
replaces the old distinction between Create(error) and CreateError(error).
There are no public Restore/Convert factories: JSON deserialization and internal
runtime adapters preserve existing envelope identity without generating a result.

Headers use an immutable ordinal map of string keys to FlowValue values. Values
can be text, numbers, arrays, objects or component-owned immutable FlowValue
instances. Deriving a message shares unchanged values without materializing JSON.
CLR type identity is retained in process; JSON deserialization restores header
values as JsonFlowValue. Use JsonFlowValue.Null for an explicit JSON null.
The reserved traceparent/tracestate headers remain JSON strings.

## Stable JSON Shape

The message converter writes the declared C# property names:

```json
{
  "TraceId": "...",
  "MessageId": "...",
  "CausationId": "...",
  "CorrelationId": null,
  "Timestamp": "2026-07-26T12:00:00Z",
  "Headers": { "source": "orders" },
  "IsError": false,
  "Value": { "orderId": "order-42" },
  "Error": null
}
```

An error message has `IsError: true`, a null `Value`, and a populated `Error`.
Deserialization rejects unknown or duplicate properties and contradictory
active cases. This projection is for persistence, diagnostics, and
JSON-oriented expressions; it does not make JSON the in-memory component type.

Core does not apply a property naming policy. `FlowError` uses `Code`, `Message`,
`Category`, `IsTransient` and `Details`. `FlowEvent` uses `Name`, `Kind`, `Level`,
`Timestamp`, `Message`, `Dimensions`, `Measurements`, `Details` and `Attributes`;
the event name is no longer renamed to `type`. Nested JSON and dictionary keys,
including headers, attributes and measurements, keep their supplied spelling.
Event kind/level values and reserved `flow.event.*` header keys are unchanged.

Persisted envelopes and expressions that read the previous names must be updated
(for example, `traceId` to `TraceId`, and event `type` to `Name`). Envelope and event
readers use the declared names; no legacy aliases or automatic key rewriting are added.

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

## Binary content

The minimum Core no longer contains `FlowContent` or its versioned converter.
Components own exact bytes and any content type/encoding metadata in their own
immutable `FlowValue` payloads. There is no replacement Core content abstraction.
Existing users of the removed API must keep their content model and persisted
format handling in the consuming module; this is a breaking preview change.

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

- Envelope state, headers, `FlowError`, and detached JSON are
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

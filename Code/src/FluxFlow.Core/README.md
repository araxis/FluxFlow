# FluxFlow.Core

Core supplies standalone Dataflow component primitives, message/value contracts,
component lifetime, tracing and component-owned request/reply. It has no DI, Hosting,
configuration or application dependency. Application composition and typed addresses
live in the optional FluxFlow.Application package. Configuration, expression engines
and business components are separate packages.

## Native processing and publication

FlowNode is the only optional authoring base, containing emission helpers and
lifecycle forwarding. Each component constructs its native input, Output and Events
and declares them to one FlowLifetime using HasInput, HasOutput and HasEvents.
Observe seals those declarations and watches completion; producer execution starts
separately through StartAsync. Processing/tracing helpers wrap callbacks without
creating blocks. There are no block factories or abstract execution methods in the base.
Components expose their actual blocks and receive explicit dependencies; registration
stays outside their implementation. Implement IFlowNode/IFlowSource directly to compose
native blocks without using the helper base.

Ordinary outputs are native BroadcastBlock<FlowMessage> instances. Connections
use native LinkTo, optionally with a predicate. No custom output protocol,
router, dispatch queue, reservation adapter or application link capacity remains.

Broadcast publication does not guarantee delivery to every consumer. A full or
declining target does not hold the producer open or fault it. Intermediate values
can be missed; a late subscriber may receive the latest retained value. Emission
completion means admission to the output block, not downstream processing or
durable storage. OutputCapacity bounds native output admission, not downstream
retention. Consumers own buffering; stronger delivery requirements belong to an
explicit component selected for that scenario.

Matching broadcast links are offered the same message. A false predicate excludes
that link; no match causes no routing error. A thrown predicate faults the native
source. Earlier deliveries are not rolled back. Custom components
exposing other native source blocks retain those blocks' own semantics: BufferBlock
does not become a broadcaster, and unread TransformBlock results can hold completion
open. Core adds no hidden adapter or discard route.

See [node authoring](Nodes/README.md) for processing, controls and completion.

## Messages, coordination and diagnostics

FlowMessage owns one FlowValue, immutable headers and lineage identifiers.
Forwarding and fanout share the original envelope/payload. WithValue derives a
new message while retaining correlation and recording causation. WithHeaders replaces
headers with a snapshot while preserving the existing identity and payload. FlowValue.Json
is lazy; typed processing can avoid JSON conversion. JsonFlowValue owns dynamic
JSON, and FlowError is a value with failure semantics. Its optional Details accepts
FlowValue, preserving typed data without forcing JSON conversion. Use JsonFlowValue
for dynamic JSON details and Details.Json when a JSON representation is needed. See
[data contracts](../../../docs/20-flow-data-contracts.md).

Core provides standard System.Text.Json source-generated metadata for its envelopes
and known values. JSON uses declared C# property names such as `TraceId`, `Code`
and event `Name`. Payload JSON and dictionary keys retain their original spelling.
For example:

```csharp
string json = JsonSerializer.Serialize(message, FlowJsonContext.Default.FlowMessage);
FlowMessage restored = JsonSerializer.Deserialize(json, FlowJsonContext.Default.FlowMessage)!;
```

Custom FlowValue implementations own their JSON representation and can use their own
generated JsonSerializerContext. Hosts can combine their context with FlowJsonContext
through JsonTypeInfoResolver.Combine when serializing additional types. No Core type
registration is required for custom values carried by the base FlowMessage envelope.

For a typed envelope, a host can register the existing closed generic converter
directly, avoiding reflection-based converter construction:

```csharp
var options = new JsonSerializerOptions()
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(MyJsonContext.Default, FlowJsonContext.Default),
    Converters = { new FlowMessageJsonConverter<MyValue>() }
};
```

MyJsonContext must include MyValue and FlowMessage<MyValue>. MyValue owns its
canonical JSON representation as usual. The explicitly registered converter shares
the same envelope format and validation as the default converter factory.

The isolated Core package consumer runs on .NET 8 and 10 with
JsonSerializerIsReflectionEnabledByDefault=false. It covers values, headers, envelopes,
errors, event fields, processing failures and an explicitly registered typed converter.
Event attributes/dimensions containing
custom objects should be converted to JsonElement using the host's generated metadata,
or supplied as FlowValue. The existing arbitrary-object snapshot path still uses
reflection and falls back to invariant text if serialization fails. This check does
not certify Native AOT support: the default FlowMessage<T> converter factory still
uses runtime generic construction unless a closed converter is registered, and dynamic
expression engines remain separate.

FlowRequestReply belongs to components such as Retry and RateLimit. It matches
replies, enforces deadlines/capacity and settles cancellation. Application does not
send requests or implement functions. See [coordination](Coordination/README.md).

Native ActivitySource, Meter and processing exception diagnostics remain. Hosts
select listeners/exporters; Core does not depend on an OpenTelemetry SDK. Processing
scopes measure component work and local publication, not consumer processing.
Hosts may attach an opaque diagnostic string once with
`FlowDiagnostics.AttachComponentAddress(component, address)` before execution.
FluxFlow.Application does this automatically. Fully sampled processing/control spans
and runtime metrics then include `flow.component.address`; failure diagnostics expose
`ComponentAddress` even without tracing. Unattached components retain type-only telemetry.
Core neither parses the address nor depends on application types. The weak association
does not keep components alive or provide component lookup, construction or ownership.
See [trace context](../../../docs/processing-trace-context.md).

Application-level graph validation, workflow/resource addresses, registration and
host startup/shutdown belong to FluxFlow.Application. Configuration traverses native
IConfiguration sections in FluxFlow.Configuration. Core has no application model,
service registry or configuration parser.

This is a development preview. The native output and typed registration changes
break the prior public surface; rebuild dependent packages. They do not establish
durability, exactly-once delivery, whole-library AOT support or production throughput.

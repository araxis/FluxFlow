# FluxFlow.Components.Serialization

Standalone conversion nodes for canonical `FlowContent` and `FlowValue`.
The package makes JSON, text, and Base64 boundaries explicit without requiring
the Engine runtime or converting through mutable dictionaries and dynamic CLR
objects.

## Canonical Nodes

| Node | Input | Output |
|------|-------|--------|
| `JsonParseNode` | `FlowContent` | `FlowResult<FlowValue>` |
| `JsonStringifyNode` | `FlowValue` | `FlowResult<FlowContent>` |
| `TextEncodeNode` | `FlowValue.String` | `FlowResult<FlowContent>` |
| `TextDecodeNode` | `FlowContent` | `FlowResult<FlowValue>` |
| `Base64EncodeNode` | `FlowContent` | `FlowResult<FlowValue>` |
| `Base64DecodeNode` | `FlowValue.String` | `FlowResult<FlowContent>` |

Every node has one bounded `Input`, one broadcast `Output`, and `Events` for
diagnostics. Expected format, type, and size failures use `IsError == true` on
the normal output. The canonical nodes do not expose a universal error port.

```csharp
var content = FlowContent.FromBytes(
    Encoding.UTF8.GetBytes("""{"orderId":"order-42"}"""),
    contentType: "application/json");

await using var node = new JsonParseNode();
var results = new BufferBlock<FlowMessage<FlowResult<FlowValue>>>();
node.Output.LinkTo(results);

await node.Input.SendAsync(FlowMessage.Create(content));
var result = (await results.ReceiveAsync()).Payload;
```

Success and failure messages preserve correlation, trace, and headers while
creating the next message and causation identity through `FlowMessage.With(...)`.
Later inputs continue after expected conversion failures.

## Conversion Semantics

`json.parse` decodes a byte-backed `FlowContent` once and caches the resulting
`FlowValue` on that exact content object. It accepts JSON regardless of media
type because selecting this node is the explicit parse decision. JSON objects
become ordinal immutable objects, arrays retain order, and numeric values retain
integer, decimal, or floating-point kinds.

`json.stringify` writes ordinary JSON, not the tagged canonical `FlowValue`
storage format. Object properties are ordered ordinally for deterministic
output. Binary values become Base64 strings; date/time, duration, and GUID
values use invariant string forms.

`text.encode` accepts only a string `FlowValue` and creates `text/plain`
content. `text.decode` produces a string `FlowValue`, uses `FlowContent.Encoding`
or a content-type charset when present, skips the selected encoding preamble,
and otherwise uses `DefaultEncoding`. Invalid declared transport encodings fall
back to that validated default.

`base64.encode` uses the exact original content bytes. Value-backed content may
contain a binary or string `FlowValue`; other value kinds are rejected as normal
results. `base64.decode` accepts a string `FlowValue` and creates
`application/octet-stream` content.

## Results And Limits

`SerializationResultKinds` identifies each operation success or failure.
`SerializationErrorCodeNames` provides stable workflow-facing codes for format,
type, missing-input, input-limit, and output-limit failures. `FlowError.Details`
contains the node type, input kind, exception type, and available content
metadata.

`SerializationNodeOptions` applies bounded intake and conversion limits:

| Option | Default | Purpose |
|--------|---------|---------|
| `BoundedCapacity` | `128` | Maximum queued messages. |
| `DefaultEncoding` | `utf-8` | Encoding when content does not declare one. |
| `MaxInputBytes` | `1048576` | Maximum byte-backed input or encoded text size. |
| `MaxOutputBytes` | `1048576` | Maximum generated content or text size. |
| `WriteIndented` | `false` | Pretty-print JSON output. |
| `AllowTrailingCommas` | `false` | Permit trailing commas during JSON parsing. |
| `SkipComments` | `false` | Permit and skip JSON comments. |

Invalid static options fail node construction. Unexpected implementation faults
remain node faults so the runtime can isolate the component; only deliberate
conversion failures become result data.

## Migration To 5.x

The concise node names now own the canonical contracts shown above. Replace
the temporary `FlowContent*` and `FlowValue*` node names with their concise
equivalents when migrating from the local 4.x milestone. From published 3.x,
keep the concise operation name but replace request/result DTOs and `Errors`
links with `FlowContent` or `FlowValue` inputs and conditions over `FlowResult.Kind`,
`FlowResult.IsError`, and `FlowResult.Error.Code`.

Per-request encoding operations are explicit node composition in 5.x: encode
text before Base64 encoding and decode Base64 before text decoding. Encoding,
JSON formatting/parser behavior, queue capacity, and byte limits remain static
component options.

## Composition

Add `FluxFlow.Components.Serialization.Composition` to register the six fixed
canonical node types and their Designer metadata. That optional package binds
flat component settings and resolves an optional host-owned keyed
`TimeProvider`; this runtime package does not own resources, links,
configuration loading, or rendering.

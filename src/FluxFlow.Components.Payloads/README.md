# FluxFlow.Components.Payloads

Standalone payload inspection for canonical `FlowContent`. The primary node
preserves the exact content object and its lazy decoded `FlowValue`, creates
bounded previews, and returns expected content failures as normal workflow data.
No Engine runtime is required.

## Node

`PayloadInspectNode` consumes `FlowMessage<FlowContent>` and emits
`FlowMessage<FlowResult<PayloadInspectionResult>>` through one normal output.

```csharp
var content = FlowContent.FromBytes(
    Encoding.UTF8.GetBytes("""{"orderId":"order-42"}"""),
    contentType: "application/json");

await using var node = new PayloadInspectNode();
var results = new BufferBlock<
    FlowMessage<FlowResult<PayloadInspectionResult>>>();
node.Output.LinkTo(results);

await node.Input.SendAsync(FlowMessage.Create(content));
var result = (await results.ReceiveAsync()).Payload;
```

The result keeps `PayloadInspectionResult.Content` as the exact input instance.
`DecodedValue` is the value cached by `FlowContent.ReadAsFlowValue(...)`, so
later components can reuse it without deserializing the original bytes again.
The node does not convert content into mutable dictionaries or dynamic CLR
objects.

## Results

The canonical output uses one result family:

- `Inspected`: classification and previews succeeded.
- `InputTooLarge`: the exact content is preserved but not decoded.
- `DecodeFailed`: a selected content codec could not decode the bytes.
- `ParseFailed`: declared JSON or XML was invalid.
- `InspectFailed`: an unexpected per-message inspection operation failed.

Failure variants set `IsError`, expose a stable `PayloadErrorCodeNames` code,
retain the inspection value, and do not stop later messages. They are ordinary
workflow data and can be filtered or mapped like any other result. The
canonical node has no universal error port. Lifecycle and processing notes are
published through `Events`.

## Content Semantics

The package-owned default codec catalog recognizes:

- `application/json` and `+json` media types as JSON
- `application/xml`, `text/xml`, and `+xml` media types as XML text
- the `text/*` family as text
- all unknown or missing media types as binary

Inspection trusts declared media types. Bytes that happen to resemble JSON are
not sniffed as JSON when their media type is unknown. Missing, invalid, or
unsupported text encodings fall back to UTF-8 through the canonical content
codec behavior.

Hosts can supply a `FlowContentCodecCatalog` when they own additional media
conventions:

```csharp
await using var node = new PayloadInspectNode(
    options: PayloadInspectOptions.Default,
    codecs: hostCodecs,
    clock: TimeProvider.System);
```

`PayloadKind` distinguishes empty, JSON object/array/scalar, XML, base64 text,
text, binary, and already-decoded value content. Inspection can include byte
count, detected encoding, text and formatted previews, truncation flags, parse
details, and decoded base64 size.

## Options

```csharp
new PayloadInspectOptions
{
    MaxInputBytes = 1_048_576,
    MaxPreviewBytes = 1024,
    MaxFormattedChars = 4096,
    DetectBase64 = true,
    FormatJson = true,
    FormatXml = true,
    BoundedCapacity = 128
};
```

`Input` is bounded and applies backpressure. `Output` and `Events` are
broadcast sources, matching the standalone node-kit contract.

## Composition

Add `FluxFlow.Components.Payloads.Composition` when a Composition host should
register the canonical `payload.inspect` factory and Designer metadata:

```csharp
services
    .AddFluxFlowApplication(configuration)
    .UseRuntimeAssembler(runtime => runtime
        .RegisterNodes(registry => registry.RegisterPayloadInspect()));
```

The adapter can resolve optional host-owned keyed `FlowContentCodecCatalog` and
`TimeProvider` resources. This runtime package does not own resource lifetime,
configuration loading, links, or rendering.

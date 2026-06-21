# FluxFlow.Components.Serialization

Reusable serialization and encoding components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `json.parse` | `Input` -> `Output`, `Errors` | Parses text or bytes into a JSON result. |
| `json.stringify` | `Input` -> `Output`, `Errors` | Serializes a value into JSON text and bytes. |
| `text.encode` | `Input` -> `Output`, `Errors` | Encodes text into bytes. |
| `text.decode` | `Input` -> `Output`, `Errors` | Decodes bytes into text. |
| `base64.encode` | `Input` -> `Output`, `Errors` | Encodes bytes or text into base64 text. |
| `base64.decode` | `Input` -> `Output`, `Errors` | Decodes base64 text into bytes and optional text. |

## Configuration

Each node supports the same basic safety options:

```json
{
  "type": "json.parse",
  "name": "parse",
  "boundedCapacity": 128,
  "defaultEncoding": "utf-8",
  "maxInputBytes": 1048576,
  "maxOutputBytes": 1048576,
  "writeIndented": false,
  "allowTrailingCommas": false,
  "skipComments": false
}
```

Per-message failures emit `FlowError` values on the `Errors` port and the node
continues with later messages. Size limits are enforced before large inputs or
outputs are forwarded.

## Examples

```csharp
new JsonParseRequest
{
    Text = """{"ok":true}"""
};
```

```csharp
new Base64DecodeRequest
{
    Text = "aGVsbG8=",
    DecodeText = true
};
```

## Direct Use

```csharp
await using var node = new JsonParseNode(
    new SerializationNodeOptions { AllowTrailingCommas = true });
```

Each node is standalone: create it with `SerializationNodeOptions` and an
optional `TimeProvider`, post `FlowMessage<TRequest>` to `Input`, and link
`Output`, `Errors`, or `Events` to the next stage.

## Composition

The optional `FluxFlow.Components.Serialization.Composition` package registers
serialization factories for `FluxFlow.Composition`. It binds the existing
`SerializationNodeOptions` from node configuration and resolves an optional
keyed `TimeProvider` resource owned by the host.

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterJsonParse()
        .RegisterJsonStringify()
        .RegisterTextEncode()
        .RegisterTextDecode()
        .RegisterBase64Encode()
        .RegisterBase64Decode());
```

The request/result CLR types are fixed by the serialization contracts; no type
alias or string-to-type resolution is needed.

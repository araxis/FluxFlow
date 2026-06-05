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

## Registration

```csharp
registry.RegisterSerializationComponents();
```

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.

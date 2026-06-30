# FluxFlow.Components.Serialization.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone
serialization nodes from `FluxFlow.Components.Serialization`.

This package does not choose serializers, scan assemblies, resolve CLR types
from strings, or own encoding resources. Hosts register the fixed serialization
node factories explicitly and may provide an optional keyed `TimeProvider`.

## Registration

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

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `json.parse` | `JsonParseNode` | `Input`, `Output` |
| `json.stringify` | `JsonStringifyNode` | `Input`, `Output` |
| `text.encode` | `TextEncodeNode` | `Input`, `Output` |
| `text.decode` | `TextDecodeNode` | `Input`, `Output` |
| `base64.encode` | `Base64EncodeNode` | `Input`, `Output` |
| `base64.decode` | `Base64DecodeNode` | `Input`, `Output` |

All factories expose `Events` and `Errors`. `clock` is an optional keyed
`TimeProvider` resource for deterministic diagnostics. The request/result CLR
types are fixed to the contracts from `FluxFlow.Components.Serialization`.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "parse": {
              "type": "json.parse",
              "resources": {
                "clock": "fixed"
              },
              "configuration": {
                "defaultEncoding": "utf-8",
                "maxInputBytes": 1048576,
                "maxOutputBytes": 1048576,
                "allowTrailingCommas": true,
                "boundedCapacity": 128
              }
            }
          },
          "links": []
        }
      }
    }
  }
}
```

Each node binds the existing `SerializationNodeOptions` shape from composition
configuration.

## Design Metadata

`SerializationComponentDesignMetadataProvider` exposes neutral Designer metadata
for the six serialization composition nodes. The metadata describes the fixed
request/result ports, shared `SerializationNodeOptions` surface, option
grouping/editor hints, and optional `clock` resource picker hint for hosts that
build palettes, editors, validators, or documentation views.
The metadata is authored through the shared validated Designer metadata builder
while preserving the same public metadata contracts consumed by hosts.

The optional `clock` resource remains host-owned with a key-pattern hint and is
not represented as an editable node option.

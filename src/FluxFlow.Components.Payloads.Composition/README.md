# FluxFlow.Components.Payloads.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone
payload inspection node from `FluxFlow.Components.Payloads`.

This package does not scan assemblies, resolve CLR types from strings, own
payload adapters, or manage resources beyond resolving an optional keyed
`TimeProvider`. Hosts register the payload node factory explicitly.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterPayloadInspect());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `payload.inspect` | `PayloadInspectNode` | `Input`, `Output` |

The factory exposes `Events` and `Errors`. `clock` is an optional keyed
`TimeProvider` resource for deterministic result, event, and error timestamps.
The request/result CLR types are fixed to `PayloadInspectionRequest` and
`PayloadInspectionResult`.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "inspect": {
              "type": "payload.inspect",
              "resources": {
                "clock": "fixed"
              },
              "configuration": {
                "maxInputBytes": 1048576,
                "maxPreviewBytes": 1024,
                "maxFormattedChars": 4096,
                "detectBase64": true,
                "formatJson": true,
                "formatXml": true,
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

The node binds the existing `PayloadInspectOptions` shape from composition
configuration.

## Design Metadata

`PayloadsComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `payload.inspect` composition node. The metadata describes the fixed
request/result ports, `PayloadInspectOptions` surface, and optional `clock`
resource hint for hosts that build palettes, editors, validators, or
documentation views.

The optional `clock` resource remains host-owned and is not represented as an
editable node option.

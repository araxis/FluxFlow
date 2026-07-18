# FluxFlow.Components.Serialization.Composition

Composition registration and Designer metadata for explicit conversions among
canonical `FlowContent`, `FlowValue`, JSON, text, and Base64. The package binds
flat component settings and resolves an optional host-owned clock; it does not
own resources, scan assemblies, or require the Engine runtime.

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

| Type | Input | Output |
|------|-------|--------|
| `json.parse` | `FlowContent` | `FlowResult<FlowValue>` |
| `json.stringify` | `FlowValue` | `FlowResult<FlowContent>` |
| `text.encode` | `FlowValue` string | `FlowResult<FlowContent>` |
| `text.decode` | `FlowContent` | `FlowResult<FlowValue>` |
| `base64.encode` | `FlowContent` | `FlowResult<FlowValue>` |
| `base64.decode` | `FlowValue` string | `FlowResult<FlowContent>` |

Expected conversion failures remain on `Output` and later inputs continue.
The canonical registrations expose `Events` and no universal error port.

## Flat Definition

```json
{
  "Resources": {
    "ConversionClock": {
      "Type": "host.clock"
    }
  },
  "Workflows": {
    "Conversions": {
      "ParseInbound": {
        "Type": "json.parse",
        "clock": "Resources.ConversionClock",
        "allowTrailingCommas": false,
        "maxInputBytes": 1048576
      },
      "WriteOutbound": {
        "Type": "json.stringify",
        "writeIndented": false,
        "maxOutputBytes": 1048576
      }
    }
  }
}
```

The example leaves both component ports available for direct runtime access or
for type-compatible workflow links. A `FlowResult<T>` output is not implicitly
unwrapped into a `T` input; use an explicit result-aware component when chaining
a conversion result into another typed operation. Component settings and
resource references remain flat. Addresses are exact, ordinal, and
case-sensitive.

`clock` is an optional keyed `TimeProvider` using a `Resources.{name}` address.
The host owns registration, lifetime, and disposal. When omitted, the node uses
`TimeProvider.System`.

## Design Metadata

`SerializationComponentDesignMetadataProvider` describes the six fixed
canonical port pairs, all shared option section/importance/editor hints, and the
optional host-owned clock picker. The metadata is descriptive only; hosts own
palette and inspector rendering, validation display, persistence, activation,
and runtime mapping.

The Composition 2.x registrations intentionally select the canonical nodes.
Legacy request-based standalone nodes remain in the runtime package but are not
registered under these fixed node type names.

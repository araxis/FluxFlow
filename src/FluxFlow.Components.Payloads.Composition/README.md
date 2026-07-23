# FluxFlow.Components.Payloads.Composition

Composition registration and Designer metadata for canonical payload
inspection. The package binds flat node settings, resolves host-owned resources,
and creates the standalone `PayloadInspectNode` from
`FluxFlow.Components.Payloads`.

It does not scan assemblies, resolve CLR types from strings, own resource
lifetimes, deserialize transport payloads outside `FlowContent`, or require the
Engine runtime.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterPayloadInspect());
```

| Type | Input | Output | Diagnostics |
|------|-------|--------|-------------|
| `payload.inspect` | `FlowContent` | `FlowResult<PayloadInspectionResult>` | `Events` |

Expected size, decode, and parse failures remain on `Output` with `IsError ==
true`; there is no universal error port.

## Flat Definition

```json
{
  "Resources": {
    "PayloadCodecs": {
      "Type": "host.payload-codecs"
    },
    "InspectionClock": {
      "Type": "host.clock"
    }
  },
  "Workflows": {
    "Main": {
      "InspectPayload": {
        "Type": "payload.inspect",
        "codecs": "Resources.PayloadCodecs",
        "clock": "Resources.InspectionClock",
        "maxInputBytes": 1048576,
        "maxPreviewBytes": 1024,
        "maxFormattedChars": 4096,
        "detectBase64": true,
        "formatJson": true,
        "formatXml": true,
        "boundedCapacity": 128
      }
    }
  }
}
```

Settings and resource references are flat within the component object. Both
resources are optional:

- `codecs`: keyed `FlowContentCodecCatalog` for host-owned media conventions
- `clock`: keyed `TimeProvider` for deterministic result and diagnostic time

Resource addresses are exact, ordinal, and case-sensitive. The host registers,
owns, and disposes the keyed services; the factory falls back to the
package-owned codec catalog and `TimeProvider.System` when references are
absent.

## Design Metadata

`PayloadsComponentDesignMetadataProvider` describes:

- canonical `FlowContent` input and `FlowResult<PayloadInspectionResult>` output
- all `PayloadInspectOptions` fields with section, importance, and editor hints
- optional host-owned codec-catalog and clock pickers
- `Resources.{name}` key-pattern hints for both resources

The metadata is descriptive only. Hosts own palette/inspector rendering,
resource catalog binding, validation display, persistence, activation, and
runtime mapping.

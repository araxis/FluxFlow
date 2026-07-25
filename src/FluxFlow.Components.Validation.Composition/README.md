# FluxFlow.Components.Validation.Composition

Composition registration and Designer metadata for canonical JSON Schema
validation over immutable `FlowValue`. The package binds flat component
settings and resolves optional host-owned selector and clock resources; it does
not own resources, scan assemblies, watch schema files, or require Engine.

Existing definitions using `json.schema-validator` remain supported as a
hidden alias; new definitions and Designer palettes use `json.validate`.

## Registration

```csharp
services
    .AddFluxFlowApplication(configuration)
    .UseRuntimeAssembler(runtime => runtime
        .RegisterNodes(registry => registry.RegisterJsonSchemaValidator()));
```

| Type | Input | Output |
|------|-------|--------|
| `json.validate` | `FlowValue` | `FlowResult<JsonSchemaFlowValueValidationResult>` |

Valid schema matches use result kind `Valid`. Schema rejection uses `Invalid`
with issues and is not an error. Missing input, selector failure, and evaluation
failure use stable error variants on the same Output. The fixed registration
exposes Events and no Valid, Invalid, or universal Errors ports.

## Flat Definition

```json
{
  "Resources": {
    "Validation": {
      "OrderBody": {
        "Type": "host.validation-selector"
      }
    },
    "Clocks": {
      "Business": {
        "Type": "host.clock"
      }
    }
  },
  "Workflows": {
    "OrderProcessing": {
      "ValidateOrder": {
        "Type": "json.validate",
        "selector": "Resources.Validation.OrderBody",
        "clock": "Resources.Clocks.Business",
        "schema": {
          "type": "object",
          "required": [ "id", "total" ],
          "properties": {
            "id": { "type": "string" },
            "total": { "type": "number" }
          }
        },
        "schemaId": "orders",
        "valueSelector": "body"
      }
    }
  }
}
```

Components, settings, resource references, and port links remain flat. Resource
addresses are exact, ordinal, and case-sensitive. `schemaPath` is read only
during composition build; the message pump performs no file I/O or schema
compilation.

The host registers the referenced `IJsonSchemaFlowValueSelector` and
`TimeProvider` keyed services and owns their lifetime. Both Designer resource
pickers use `Resources.{name}` addresses.

## Result Boundary

The Output payload is a typed `FlowResult<JsonSchemaFlowValueValidationResult>`.
Links do not implicitly extract its Value. Conditions may route by result kind
or error state, and an explicit result-aware mapper or component can extract
the validation value when a downstream `FlowValue` input is required.
Convert CLR inputs explicitly at the application boundary. Existing
`payloadSelector` settings migrate directly to `valueSelector`; older Valid,
Invalid, and Errors links migrate to conditions over `Kind`, `IsError`, and
`Error.Code`.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`ValidationComponentDesignMetadataProvider` describes the fixed FlowValue Input
and single FlowResult Output, schema/selection/runtime option hints, and optional
host-owned selector and clock pickers. The metadata is descriptive only; hosts
own palette rendering, persistence, validation display, resource registration,
activation, and runtime mapping.

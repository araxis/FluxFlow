# FluxFlow.Components.Observability.Composition

Composition registration and Designer metadata for canonical FlowValue Counter,
Logger, and Metrics components. Each component emits one normal FlowResult
output plus Events.

This package does not scan assemblies, own expression engines/selectors/clocks,
create logging or metric sinks, or add renderer behavior.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterCounter()
        .RegisterLogger()
        .RegisterMetrics());
```

| Type | Input | Output |
|------|-------|--------|
| `flow.counter` | `FlowValue` | `FlowResult<FlowCounterSnapshot>` |
| `flow.logger` | `FlowValue` | `FlowResult<FlowValueLogEntry>` |
| `flow.metrics` | `FlowValue` | `FlowResult<FlowMetricSnapshot>` |

Descriptors expose Events and no universal Errors ports. Counter rejection is a
successful result. Logger attribute and Metrics size failures are partial error
results carrying usable output values.

## Flat Definition

```json
{
  "Resources": {
    "Expressions": {
      "Main": {
        "Type": "host.expression"
      }
    },
    "Selectors": {
      "Kind": {
        "Type": "host.flowValueSelector"
      },
      "Size": {
        "Type": "host.flowValueSelector"
      }
    },
    "System": {
      "Clock": {
        "Type": "host.clock"
      }
    }
  },
  "Workflows": {
    "Telemetry": {
      "CountAccepted": {
        "Type": "flow.counter",
        "engine": "Resources.Expressions.Main",
        "clock": "Resources.System.Clock",
        "name": "accepted-orders",
        "predicate": "input.status = 'accepted'",
        "boundedCapacity": 128,
        "Input": "OrderSource.Output"
      },
      "LogOrders": {
        "Type": "flow.logger",
        "clock": "Resources.System.Clock",
        "attributeSelectors": ["kind"],
        "attribute:kind": "Resources.Selectors.Kind",
        "level": "Information",
        "category": "orders",
        "messageTemplate": "Observed {kind} item #{sequence}.",
        "boundedCapacity": 128,
        "Input": "OrderSource.Output"
      },
      "MeasureOrders": {
        "Type": "flow.metrics",
        "sizeSelector": "Resources.Selectors.Size",
        "clock": "Resources.System.Clock",
        "name": "orders",
        "boundedCapacity": 128,
        "Input": "OrderSource.Output"
      }
    }
  }
}
```

Component settings, resource references, and port links are flat. Resource
addresses and component/port names are exact and case-sensitive. Logger uses a
string for one `attributeSelectors` entry or an array for multiple entries;
each entry resolves the matching `attribute:{name}` resource.

## Host-Owned Resources

- Counter: conditionally required `engine`, optional `contextFactory`, optional
  `clock`.
- Logger: optional `clock` and one required `attribute:{name}` selector for each
  configured attribute name.
- Metrics: optional `sizeSelector` and optional `clock`.

Every selector in the canonical contract implements
`IObservabilityFlowValueSelector` and returns FlowValue directly.

## Design Metadata

`ObservabilityComponentDesignMetadataProvider` describes canonical fixed ports,
option sections/editor hints, conditional expression resources, FlowValue
selector patterns, and exact host-owned resource addresses. The metadata is
descriptive only; hosts own palette and inspector rendering, resource selection,
validation UI, activation, persistence, and sink integration.

Explicit generic registration overloads retain the released direct-result
contract for code-authored compatibility. Composition `2.x` parameterless
registrations are canonical; install Composition `1.x` for existing legacy
definitions.

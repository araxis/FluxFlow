# FluxFlow.Components.Mapping.Composition

Optional `FluxFlow.Composition` registration helpers and Designer metadata for
mapping components. The canonical `data.map` contract consumes `FlowValue`
and emits `FlowResult<FlowValue>` on one normal output.

Existing definitions using `flow.mapper` remain supported as a hidden alias;
new definitions and Designer palettes use `data.map`.

The package does not choose an expression language, scan assemblies, resolve
CLR types from strings, or own expression-engine resources.

## Canonical Registration

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>(
    "Resources.Expressions.Primary",
    expressionEngine);

registry.RegisterMapper();
```

| Type | Node | Input | Output |
|------|------|-------|--------|
| `data.map` | `FlowValueMapperNode` | `FlowValue` | `FlowResult<FlowValue>` |

The output result distinguishes mapped values from expected expression failures
through `Kind`, `IsError`, and `Error`. The canonical contract has no `Failed`
port and no universal error output.

## Flat Definition

```json
{
  "Resources": {
    "Expressions": {
      "Primary": {
        "Type": "host.expression"
      }
    }
  },
  "Workflows": {
    "Main": {
      "MapOrder": {
        "Type": "data.map",
        "engine": "Resources.Expressions.Primary",
        "expression": "input",
        "expressionName": "normalize-order",
        "inputType": "order.input",
        "outputType": "order.normalized"
      }
    }
  }
}
```

Component settings and resource references are flat. Hosts register the
referenced expression engine as a keyed `IFlowExpressionEngine` using the exact,
case-sensitive resource address.

`contextFactory` is an optional keyed `IMappingContextFactory` reference and
`clock` is an optional keyed `TimeProvider` reference. Their addresses follow
the same `Resources.{name}` pattern. The host owns registration, lifetime, and
disposal of all three resources.

Invalid options, such as a missing `expression`, fail during node activation.

## 3.x Migration

Composition 3.x removes generic CLR mapper registration and the `Failed` port
constant. Register `data.map` with `RegisterMapper()`, convert CLR values to
`FlowValue` at the application boundary, and use conditional links over the
normal `FlowResult<FlowValue>` output for success and failure routing.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile.


`MappingComponentDesignMetadataProvider` describes the canonical node:

- `Input`: `FlowValue`
- `Output`: `FlowResult<FlowValue>`
- required `engine` resource, plus optional `contextFactory` and `clock`
- option section, importance, editor, syntax, and related-resource hints
- host-owned resource pickers using `Resources.{name}` key patterns

The metadata is descriptive only. Hosts own palette and inspector rendering,
resource selection, validation UI, activation, and persistence.

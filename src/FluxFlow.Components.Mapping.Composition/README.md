# FluxFlow.Components.Mapping.Composition

Optional `FluxFlow.Composition` registration helpers and Designer metadata for
mapping components. The canonical `flow.mapper` contract consumes `FlowValue`
and emits `FlowResult<FlowValue>` on one normal output.

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
| `flow.mapper` | `FlowValueMapperNode` | `FlowValue` | `FlowResult<FlowValue>` |

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
        "Type": "flow.mapper",
        "engine": "Resources.Expressions.Primary",
        "expression": "input",
        "expressionName": "normalize-order",
        "inputType": "order.input",
        "outputType": "order.normalized",
        "boundedCapacity": 128
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

Invalid options, such as a missing `expression` or non-positive
`boundedCapacity`, fail during node activation.

## Typed Compatibility Registration

Existing code-authored hosts can retain explicit CLR contracts:

```csharp
registry.RegisterMapper<InputMessage, OutputMessage>(
    "flow.mapper.typed");
```

That overload creates `FlowMapperNode<TInput,TOutput>` and preserves its
`Input`, `Output`, and `Failed` ports. Use a distinct node type when canonical
and typed registrations share one registry. `InputType`, `OutputType`, and
`targetType` remain diagnostic metadata; the closed generic arguments determine
the actual typed port contracts.

## Design Metadata

`MappingComponentDesignMetadataProvider` describes the canonical node:

- `Input`: `FlowValue`
- `Output`: `FlowResult<FlowValue>`
- required `engine` resource, plus optional `contextFactory` and `clock`
- option section, importance, editor, syntax, and related-resource hints
- host-owned resource pickers using `Resources.{name}` key patterns

The metadata is descriptive only. Hosts own palette and inspector rendering,
resource selection, validation UI, activation, and persistence.

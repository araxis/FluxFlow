# Expression Mapping

FluxFlow keeps expression evaluation outside the runtime core. Hosts provide
expression services, component packages decide where expressions are useful,
and composition adapters resolve those services through explicit keyed DI.
Links remain structural and never insert implicit mappings.
The default configuration-authored mapping component is `flow.mapper`.

## Core Contracts

`FluxFlow.Mapping` owns the expression and direct-mapper contracts:

```csharp
public interface IFlowExpressionEngine
{
    string Name { get; }

    object? Evaluate(string expression, FlowMapContext context, Type resultType);

    IFlowCompiledExpression<T> Compile<T>(string expression);
}
```

Expressions should compile during node construction or application activation.
Nodes evaluate the compiled form per message.

`FlowMapContext` carries named variables. Persisted expressions should use
stable variable names and data-shaped values; do not expose clients, mutable
services, or secrets through the context.

## Canonical FlowValue Mapper

`FluxFlow.Components.Mapping` provides `FlowValueMapperNode` for dynamic,
configuration-authored workflows:

```csharp
var options = new MapperOptions
{
    Expression = "input",
    ExpressionName = "normalize-order",
    InputType = "order.input",
    OutputType = "order.normalized",
    BoundedCapacity = 128
};

await using var node = new FlowValueMapperNode(options, expressionEngine);

var input = FlowValue.FromObject(new Dictionary<string, FlowValue>
{
    ["orderId"] = FlowValue.From("order-42"),
    ["total"] = FlowValue.From(125.50m)
});

await node.Input.SendAsync(FlowMessage.Create(input));
```

The node receives `FlowMessage<FlowValue>` and emits
`FlowMessage<FlowResult<FlowValue>>`. It passes the exact immutable input value
to the expression context, so mapping does not require a JSON, dynamic-object,
or CLR-object round trip.

The single output carries both expected outcomes:

| Result | `Kind` | `IsError` | `Value` |
|--------|--------|-----------|---------|
| mapped | `Mapped` | `false` | mapped `FlowValue` |
| expression failure | `MappingFailed` | `true` | original `FlowValue` |

Failure details use `FlowError` with code `mapping.mapper_failed`. The component
continues with later inputs. This is workflow data, so the canonical mapper has
no `Error` or `Failed` port. Engine/component lifecycle faults remain separate
from expected expression failures.

## Canonical Composition

Add `FluxFlow.Components.Mapping.Composition` and register the parameterless
factory:

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>(
    "Resources.Expressions.Primary",
    expressionEngine);

registry.RegisterMapper();
```

The default node type is `flow.mapper`:

| Port | Direction | Message payload |
|------|-----------|-----------------|
| `Input` | input | `FlowValue` |
| `Output` | output | `FlowResult<FlowValue>` |

The adapter resolves these host-owned references:

| Property | Required | Keyed service |
|----------|----------|---------------|
| `engine` | yes | `IFlowExpressionEngine` |
| `contextFactory` | no | `IMappingContextFactory` |
| `clock` | no | `TimeProvider` |

Example canonical application document:

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
    "Orders": {
      "Normalize": {
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

There are no `Composition`, `Nodes`, `Links`, `Configuration`, or per-component
`Resources` wrappers. Component options and resource references are flat.

## Context Factories

The default context exposes the `FlowValue` payload as both `input` and `value`.
Use `IMappingContextFactory` for additional variables:

```csharp
await using var node = new FlowValueMapperNode(
    options,
    expressionEngine,
    contextFactory: orderContextFactory,
    clock: TimeProvider.System);
```

The factory receives the same `FlowValue` instance that arrived in the message.
Keep additional variables immutable and transport-neutral where practical.

## Strongly Typed Compatibility

`FlowMapperNode<TInput,TOutput>` and
`RegisterMapper<TInput,TOutput>(nodeType)` remain available for hosts that
deliberately own CLR message contracts:

```csharp
registry.RegisterMapper<OrderInput, ReviewedOrder>("flow.mapper.order");
```

The typed node preserves its established `Output`, `Failed`, `Errors`, and
`Events` behavior. Closed generic arguments determine the actual port types;
`InputType`, `OutputType`, and `targetType` remain descriptive diagnostics.

Use a distinct node type when a registry contains both the canonical and typed
forms.

## Predicates And Routing

`ExpressionFlowPredicate<TInput>` adapts an expression engine to
`IFlowPredicate<TInput>` for component authors. Composition links may carry
compile-once conditions, but they never reshape payloads or insert mapper nodes.
Use an explicit mapper component whenever a payload shape changes.

## Direct Mapper Contract

`IFlowMapper<TInput,TOutput>` remains a small code-level transformation
contract:

```csharp
public interface IFlowMapper<in TInput, out TOutput>
{
    TOutput Map(TInput input, FlowMapContext context);
}
```

`DelegateFlowMapper<TInput,TOutput>` is suitable for small C# transformations.
Component packages decide explicitly when direct mapper contracts are part of
their behavior.

## Optional Engine Link Conditions

The older `FluxFlow.Engine` definition runtime still supports compile-once
conditions on links. Hosts using that compatibility path provide an expression
engine to `ApplicationRuntimeBuilder` or `FlowApplicationHost.Create(...)`.
These APIs can filter delivery but do not reshape payloads; use an explicit
mapper node whenever the value shape changes.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| activation reports missing `engine` | Register the exact referenced address as a keyed `IFlowExpressionEngine`; address matching is ordinal and case-sensitive. |
| mapper activation fails | Ensure `expression` is present and `boundedCapacity` is greater than zero. |
| result has `IsError == true` | Inspect `Error.Code`, `Error.Details`, and the preserved original `Value`. |
| expression cannot see data | Use `input` or `value`, or provide a keyed `contextFactory`. |
| typed output is incompatible | Verify the expression returns the closed `TOutput` selected by the typed registration. |
| a link needs to reshape data | Add an explicit mapper component; links do not map payloads. |

Next: [Package Versioning](11-package-versioning.md)

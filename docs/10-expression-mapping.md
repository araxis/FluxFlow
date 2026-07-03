# Expression Mapping

FluxFlow keeps expression evaluation outside the runtime core. Hosts provide
expression services, component packages decide where expressions are useful, and
composition adapters resolve those services as explicit keyed resources.
For configuration-first workflows, the default mapper composition node is
`flow.mapper`.

## Core Contracts

`FluxFlow.Mapping` owns the expression and mapping contracts used by components:

```csharp
public interface IFlowExpressionEngine
{
    string Name { get; }

    object? Evaluate(string expression, FlowMapContext context, Type resultType);

    IFlowCompiledExpression<T> Compile<T>(string expression);
}
```

Engines should compile expressions at build or node construction time when they
can. Nodes then evaluate the compiled expression per message.

`FlowMapContext` carries named variables:

```csharp
public sealed record FlowMapContext
{
    public IReadOnlyDictionary<string, object?> Variables { get; init; }
}
```

Use stable variable names once expressions are persisted in configuration or
workspace files. Keep variables small and data-shaped; do not put live services,
mutable state, clients, or secrets into expression context.

## Mapper Node

`FluxFlow.Components.Mapping` provides the standalone
`FlowMapperNode<TInput,TOutput>`:

```csharp
var options = new MapperOptions
{
    Expression = "input",
    ExpressionName = "copy",
    InputType = "app.input",
    OutputType = "app.output",
    BoundedCapacity = 128
};

await using var node = new FlowMapperNode<AppInput, AppOutput>(
    options,
    expressionEngine);
```

The node compiles `MapperOptions.Expression` during construction. Each incoming
`FlowMessage<TInput>` is evaluated and emitted as `FlowMessage<TOutput>` on
`Output` with the original correlation id.

Mapping failures emit a node error, fan the original input to `Failed`, and keep
processing later messages. `MapperOptions.Engine`, `InputType`, `OutputType`,
`targetType`, `ExpressionId`, and `ExpressionName` are diagnostic/configuration
metadata; they do not select CLR types or expression services.

## Composition Mapper

Add `FluxFlow.Components.Mapping.Composition` when a host wants mapper nodes from
fluent or `IConfiguration` composition definitions:

```csharp
services.AddFluxFlowExpressionEngine("default", expressionEngine);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry =>
        registry.RegisterMapper<AppInput, AppOutput>());
```

The default composition node type is `flow.mapper`. It exposes:

| Port | Direction | Message type |
|------|-----------|--------------|
| `Input` | input | `TInput` |
| `Output` | output | `TOutput` |
| `Failed` | output | `TInput` |

The adapter resolves these resources:

| Resource | Required | Service |
|----------|----------|---------|
| `engine` | yes | keyed `IFlowExpressionEngine` |
| `contextFactory` | no | keyed `IMappingContextFactory` |
| `clock` | no | keyed `TimeProvider` |

Example configuration:

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "map": {
              "type": "flow.mapper",
              "resources": {
                "engine": "default"
              },
              "configuration": {
                "expression": "input",
                "expressionName": "copy",
                "inputType": "app.input",
                "outputType": "app.output",
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

Use custom node type strings when one host needs several mapper type pairs:

```csharp
registry
    .RegisterMapper<HttpInput, StorageInput>("flow.mapper.http-to-storage")
    .RegisterMapper<StorageResult, ProjectionEvent>("flow.mapper.storage-result");
```

Closed generic registrations define the actual CLR message types. Option fields
such as `inputType`, `outputType`, and `targetType` remain portable metadata for
diagnostics and design tools.

## Context Factories

By default, expression helpers expose the payload as both `input` and `value`.
Use `IFlowMapContextFactory<TInput>` when a component needs additional variables:

```csharp
public sealed class OrderContextFactory : IFlowMapContextFactory<OrderInput>
{
    public FlowMapContext Create(OrderInput input)
        => new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input,
                ["threshold"] = 100m
            }
        };
}
```

`FlowMapperNode<TInput,TOutput>` accepts an `IMappingContextFactory`. The mapping
component package provides `TypedMappingContextFactory<TInput>` to adapt strongly
typed `IFlowMapContextFactory<TInput>` implementations when needed.

In composition, register that adapter as a keyed `contextFactory` resource and
reference it from the node's `resources` map.

For typed context factories used by Control, Assertions, Observability, or other
adapters that consume `IFlowMapContextFactory<TInput>` directly, register them
with:

```csharp
services.AddFluxFlowMapContextFactory<OrderInput>("order-context", contextFactory);
```

## Predicates And Control Nodes

`ExpressionFlowPredicate<TInput>` adapts an expression engine into an
`IFlowPredicate<TInput>`:

```csharp
var predicate = new ExpressionFlowPredicate<OrderInput>(
    "input.Priority == true",
    expressionEngine);
```

Composition does not evaluate expressions on links. Use standalone nodes for
conditional behavior:

- `flow.filter` to drop rejected messages
- `flow.when` to split true/false branches
- `flow.switch` to route by a host-owned selector
- `flow.mapper` to shape messages before routing

This keeps graph links structural and keeps expression services under host-owned
DI.

## Direct Mapper Contracts

`IFlowMapper<TInput,TOutput>` is a small transformation contract for component
authors:

```csharp
public interface IFlowMapper<in TInput, out TOutput>
{
    TOutput Map(TInput input, FlowMapContext context);
}
```

Use `DelegateFlowMapper<TInput,TOutput>` when a component needs a simple C#
mapper:

```csharp
var mapper = new DelegateFlowMapper<OrderInput, ReviewedOrder>(
    (order, context) =>
    {
        var threshold = (decimal)context.Variables["threshold"]!;
        return new ReviewedOrder(
            order.Id,
            order.Total,
            Priority: order.Total >= threshold);
    });
```

Composition links do not apply `IFlowMapper<TInput,TOutput>` automatically. Nodes
and adapters decide when mapper contracts are part of their behavior.

## App Pattern

For application-authored workflows:

1. Register expression engines as host-owned keyed resources.
2. Register closed generic component factories for each message shape.
3. Keep expression strings in node options, not in links.
4. Keep resource selection in node `resources` maps.
5. Validate app-specific expression presence before runtime build.
6. Let factory/build diagnostics report missing resources or invalid options.

## Optional Engine Link Conditions

`FluxFlow.Engine` still supports inline link `when` conditions for hosts that
intentionally use the older `ApplicationDefinition` runtime:

```json
{
  "Input": {
    "from": "review.Output",
    "when": "input.Priority == true"
  }
}
```

When an engine definition contains a non-empty resolved `when` condition,
`ApplicationRuntimeBuilder` or `FlowApplicationHost.Create(...)` must receive an
`IFlowExpressionEngine`. Otherwise the build fails with
`MissingExpressionEngine`.

Use this path only for engine-based hosts that need engine-specific conditional
links. Composition-first hosts should model routing with normal standalone nodes.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| composition build reports missing `engine` | Map the node's `engine` resource to a keyed `IFlowExpressionEngine`. |
| mapper build fails | Check `expression` is present and `boundedCapacity` is greater than zero. |
| mapped output has the wrong type | Verify the expression returns the closed `TOutput` type registered by the host. |
| expression cannot see data | Use `input` or `value`, or provide a keyed `contextFactory`. |
| rejected messages disappear | Link the mapper `Failed` port to an error path or dead-letter node. |
| routing expression is hard to maintain | Move business rules into C# node code and keep expressions small and data-shaped. |

Next: [Package Versioning](11-package-versioning.md)

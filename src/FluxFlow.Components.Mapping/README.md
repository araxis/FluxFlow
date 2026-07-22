# FluxFlow.Components.Mapping

Standalone mapping for FluxFlow. `FlowValueMapperNode` maps
transport-neutral `FlowValue` payloads without serializing them. The package
depends on the data, node, and expression contracts only; it does not require an
Engine runtime or choose an expression language.

## Nodes

| Node | Shape | Purpose |
|------|-------|---------|
| `FlowValueMapperNode` | `Input` -> `Output` | Maps `FlowValue` and emits `FlowResult<FlowValue>` on one normal output. |

The node compiles `MapperOptions.Expression` once during construction and uses
a host-provided `IFlowExpressionEngine` for evaluation.

## FlowValue Mapper

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
var results = new BufferBlock<FlowMessage<FlowResult<FlowValue>>>();
node.Output.LinkTo(results);

var input = FlowValue.FromObject(new Dictionary<string, FlowValue>
{
    ["orderId"] = FlowValue.From("order-42"),
    ["total"] = FlowValue.From(125.50m)
});

await node.Input.SendAsync(FlowMessage.Create(input));
var result = (await results.ReceiveAsync()).Payload;
```

The expression context receives the exact immutable `FlowValue` instance as
both `input` and `value`. The mapper does not convert through JSON, dictionaries
of `object`, or CLR dynamic objects.

`Output` carries one normal result family:

- Success: `Kind == MappingResultKinds.Mapped`, `IsError == false`, and `Value`
  contains the mapped `FlowValue`.
- Expected mapping failure: `Kind == MappingResultKinds.Failed`,
  `IsError == true`, `Error.Code == MappingErrorCodeNames.MapperFailed`, and
  `Value` retains the original input.

Failure results preserve normal `FlowMessage` correlation and trace identity,
create a new message identity through `With(...)`, and allow later messages to
continue. There is no universal error port or separate failed branch on this
canonical node.

## Mapping Context

Pass an `IMappingContextFactory` when expressions need additional data-shaped
variables. Do not place mutable services, clients, or secrets in expression
contexts.

```csharp
await using var node = new FlowValueMapperNode(
    options,
    expressionEngine,
    contextFactory: appContextFactory,
    clock: TimeProvider.System);
```

The context factory receives the original `FlowValue` plus immutable node
context containing the resolved options and canonical input/output types. No
conversion is performed before context creation.

## 5.x Migration

Mapping 5.x removes the generic CLR mapper, typed context adapter, numeric
error code, `Failed`/`Errors` branches, and the ignored `engine` and legacy
`targetType` options. Convert CLR values explicitly at the application boundary,
use `FlowValueMapperNode`, and route expected failures by inspecting
`FlowResult.Kind`, `IsError`, and `Error.Code` on `Output`.

## Validation And Diagnostics

`Expression` is required and `BoundedCapacity` must be greater than zero.
Invalid options fail during construction. Per-message
`flow.mapper.succeeded`/`flow.mapper.failed` diagnostics include the declared
input/output types, engine name, and optional expression id/name. Diagnostic
timestamps use the supplied `TimeProvider` or `TimeProvider.System`.

## Composition

Add `FluxFlow.Components.Mapping.Composition` for the canonical `data.map`
factory and Designer metadata:

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>(
    "Resources.Expressions.Primary",
    expressionEngine);

registry.RegisterMapper();
```

The optional composition package exposes one canonical `RegisterMapper()`
registration. Hosts own expression-engine, context-factory, and clock resources.

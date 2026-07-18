# FluxFlow.Components.Mapping

Standalone mapping nodes for FluxFlow. The primary `FlowValueMapperNode` maps
transport-neutral `FlowValue` payloads without serializing them. The package
depends on the data, node, and expression contracts only; it does not require an
Engine runtime or choose an expression language.

## Nodes

| Node | Shape | Purpose |
|------|-------|---------|
| `FlowValueMapperNode` | `Input` -> `Output` | Maps `FlowValue` and emits `FlowResult<FlowValue>` on one normal output. |
| `FlowMapperNode<TInput,TOutput>` | `Input` -> `Output`, `Failed` | Preserved strongly typed compatibility surface. |

Both nodes compile `MapperOptions.Expression` once during construction and use
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

The context factory still receives the original `FlowValue`; no conversion is
performed before context creation.

## Typed Compatibility

`FlowMapperNode<TInput,TOutput>` remains available for code-authored strongly
typed workflows. Its established `Output`, `Failed`, `Errors`, and `Events`
behavior is unchanged.

```csharp
await using var node = new FlowMapperNode<AppInput, AppOutput>(
    options,
    expressionEngine,
    contextFactory: new TypedMappingContextFactory<AppInput>(new AppInputContextFactory()));

node.Output.LinkTo(resultSink);
node.Failed.LinkTo(deadLetterSink);
await node.Input.SendAsync(FlowMessage.Create(appInput));
```

Use this surface when the host deliberately owns closed CLR message types. New
configuration-authored workflows should prefer the canonical `FlowValue`
contract.

## Validation And Diagnostics

`Expression` is required and `BoundedCapacity` must be greater than zero.
Invalid options fail during construction. Per-message
`flow.mapper.succeeded`/`flow.mapper.failed` diagnostics include the declared
input/output types, engine name, and optional expression id/name. Diagnostic
timestamps use the supplied `TimeProvider` or `TimeProvider.System`.

## Composition

Add `FluxFlow.Components.Mapping.Composition` for the canonical `flow.mapper`
factory and Designer metadata:

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>(
    "Resources.Expressions.Primary",
    expressionEngine);

registry.RegisterMapper();
```

The optional composition package also preserves
`RegisterMapper<TInput,TOutput>()` for explicit typed compatibility
registrations.

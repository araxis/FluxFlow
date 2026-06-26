# FluxFlow.Components.Control

Standalone expression-driven control nodes for FluxFlow. They depend only on
`FluxFlow.Nodes` and `FluxFlow.Mapping` — no engine, registry, or runtime. You
`new` a node and `LinkTo` the next one.

## Nodes

| Node | Shape | Purpose |
|------|-------|---------|
| `FilterNode<TInput>` | `Input` -> `Output` | Re-broadcasts messages whose payload matches an expression; drops the rest. |
| `WhenNode<TInput>` | `Input` -> `WhenTrue` / `WhenFalse` | Routes each message by expression result. |

Every message travels as a `FlowMessage<T>` envelope. `FilterNode` re-broadcasts
the surviving `FlowMessage<TInput>` on `Output`; `WhenNode` fans the original
message to `WhenTrue` (its primary `Output`) or `WhenFalse`. The routed message
carries the same correlation id as the input.

The package does not choose an expression language: each node takes an
`IFlowExpressionEngine` directly and compiles its predicate **once** at
construction, evaluating only the compiled form per message.

```csharp
var options = new ControlExpressionOptions
{
    Expression = "value > 10",
    ExpressionId = "route-v1",
    ExpressionName = "route-important",
    InputType = "int"
};

await using var when = new WhenNode<int>(options, appExpressionEngine);

when.WhenTrue.LinkTo(highSink, new DataflowLinkOptions { PropagateCompletion = false });
when.WhenFalse.LinkTo(lowSink, new DataflowLinkOptions { PropagateCompletion = false });

await when.Input.SendAsync(FlowMessage.Create(42));
```

`FilterNode` works the same way:

```csharp
await using var filter = new FilterNode<int>(
    new ControlExpressionOptions { Expression = "value % 2 == 0", InputType = "int" },
    appExpressionEngine);

filter.Output.LinkTo(evenSink, new DataflowLinkOptions { PropagateCompletion = false });
await filter.Input.SendAsync(FlowMessage.Create(2));
```

Expression-backed constructors require a non-empty `Expression`, non-empty
`InputType`, and `BoundedCapacity` greater than zero. Invalid options fail fast
during node construction before the input pipeline is created.

### Custom mapping context

By default the predicate sees the payload as the `input` and `value` variables.
Pass an `IFlowMapContextFactory<TInput>` to shape the variables an expression
engine evaluates against:

```csharp
var node = new FilterNode<AppMessage>(options, appExpressionEngine, new AppMessageContextFactory());
```

You can also supply an already-compiled `IFlowPredicate<TInput>` directly when
you do not want the node to own compilation:

```csharp
var node = new WhenNode<int>(options, myPredicate, engineName: "my-engine");
```

The compiled-predicate constructors do not require `Expression`, but they still
validate `InputType` and `BoundedCapacity` because those values drive
diagnostics and queue sizing.

## Behavior

Expression-evaluation failures emit a `FlowError` on `Errors` (carrying the
input's correlation id and a `Code` from `ControlErrorCodes`) and the node keeps
processing later messages. Per-message diagnostics — `flow.filter.passed` /
`flow.filter.rejected` / `flow.filter.failed` and `flow.when.routed` /
`flow.when.failed` (see `ControlDiagnosticNames`) — flow on the `Events` port
with input type, engine, expression id, expression name, route, and pass/fail
metadata where available.

## Runtime timing

Error and event timestamps use the node's clock (default `TimeProvider.System`).
Provide a deterministic clock for tests:

```csharp
new FilterNode<int>(options, engine, clock: new FakeTimeProvider(timestamp));
```

## Composition

Add `FluxFlow.Components.Control.Composition` when a host wants to instantiate
control nodes from `FluxFlow.Composition` fluent/config definitions. That
optional package registers closed generic `FilterNode<TInput>` and
`WhenNode<TInput>` factories.

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>("default", expressionEngine);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterFilter<AppMessage>()
        .RegisterWhen<AppMessage>());
```

Use custom node type names when one host needs several input shapes:

```csharp
registry
    .RegisterFilter<OrderMessage>("flow.filter.order")
    .RegisterWhen<HttpResponseOutput>("flow.when.http-response");
```

Composition resolves the expression engine from the keyed `engine` resource.
Optional keyed `contextFactory` and `clock` resources can provide custom
expression variables and deterministic diagnostics. The configured `InputType`
remains diagnostic metadata; CLR port types come from the closed generic
registration. Invalid `ControlExpressionOptions`, such as a missing expression,
blank `inputType`, or non-positive `boundedCapacity`, fail during composition
build and surface as factory diagnostics when build failures are configured as
diagnostics.

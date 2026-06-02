# Expression Mapping

FluxFlow has two related concepts:

| Concept | Purpose |
|---------|---------|
| link condition predicates | decide whether an output item is routed to an input |
| mapper contracts | help component authors transform values inside nodes |

The engine can use host-provided expressions for link `when` conditions.
Payload transformation still belongs in nodes and component packages.

## Link Conditions

A link condition is a predicate. If it returns `true`, the item is sent to that
input. If it returns `false`, that link skips the item.

```json
{
  "Input": {
    "from": "review.Output",
    "when": "input.Priority == true"
  }
}
```

When the host provides an expression engine, the default link condition context
exposes the current output item as:

- `input`
- `value`

Both names reference the same object.

If the resolved `when` value is missing, empty, or whitespace, the link
receives every item. Node defaults are applied before this check.

## Default Node Condition

`NodeDefinition.When` is a default condition for every link declared on that
node when the link does not provide its own condition.

```json
{
  "type": "sample.priority-sink",
  "when": "input.Priority == true",
  "Input": "review.Output"
}
```

Per-link conditions override the node default:

```json
{
  "type": "sample.sink",
  "when": "input.Priority == true",
  "Input": {
    "from": "review.Output",
    "when": "input.Total >= 250"
  }
}
```

If a link object sets `when` to `null`, the node default is used.

## Runtime Evaluation

Definition validation checks that `when` is a string or `null`. It does not
fully evaluate the expression.

Runtime build requires an expression engine when any resolved link condition is
non-empty. If no expression engine is supplied, build fails with
`MissingExpressionEngine`. When an expression engine is supplied, the runtime
evaluates the condition for each item as the output fanout pump delivers values.
If expression evaluation throws, that is a runtime failure, not a build
validation failure.

Keep link conditions:

- side-effect free
- fast
- deterministic
- focused on routing, not transformation
- limited to fields that exist on every expected message

## Host Expression Engine

`IFlowExpressionEngine` is the expression boundary:

```csharp
public interface IFlowExpressionEngine
{
    string Name { get; }
    object? Evaluate(string expression, FlowMapContext context, Type resultType);
}
```

Pass an engine to `ApplicationRuntimeBuilder` when an application uses link
conditions:

```csharp
var builder = new ApplicationRuntimeBuilder(
    registry,
    linkConditionExpressionEngine: new AppExpressionEngine());
```

For hosted applications, pass the same engine through the host factory:

```csharp
var host = FlowApplicationHost.Create(
    definition,
    registry,
    new AppExpressionEngine());
```

The runtime asks the engine for a `bool` result when evaluating link conditions.

## Expression Predicates

`ExpressionFlowPredicate<TInput>` adapts an expression engine into an
`IFlowPredicate<TInput>`.

```csharp
var predicate = new ExpressionFlowPredicate<ReviewedOrder>(
    "input.Priority == true",
    expressionEngine);

if (predicate.IsMatch(order))
{
    // route or process the order
}
```

The default predicate context contains `input` and `value`. Use a custom
`IFlowMapContextFactory<TInput>` when a component needs additional variables.

```csharp
public sealed class OrderContextFactory : IFlowMapContextFactory<ReviewedOrder>
{
    public FlowMapContext Create(ReviewedOrder input)
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

## Mapper Contracts

`IFlowMapper<TInput,TOutput>` is a small transformation contract:

```csharp
public interface IFlowMapper<in TInput, out TOutput>
{
    TOutput Map(TInput input, FlowMapContext context);
}
```

Use it inside nodes or component packages when a transform should be
replaceable or configurable.

```csharp
var mapper = new DelegateFlowMapper<SampleOrder, ReviewedOrder>(
    order => new ReviewedOrder(
        order.Id,
        order.Customer,
        order.Total,
        Priority: order.Total >= 100m));
```

When the mapper needs variables, use the overload that receives
`FlowMapContext`:

```csharp
var mapper = new DelegateFlowMapper<SampleOrder, ReviewedOrder>(
    (order, context) =>
    {
        var threshold = (decimal)context.Variables["threshold"]!;
        return new ReviewedOrder(
            order.Id,
            order.Customer,
            order.Total,
            Priority: order.Total >= threshold);
    });
```

The engine does not automatically apply `IFlowMapper<TInput,TOutput>` to graph
links. Component nodes decide when and how to use mappers.

## Context Variables

`FlowMapContext` carries named variables:

```csharp
public sealed record FlowMapContext
{
    public IReadOnlyDictionary<string, object?> Variables { get; init; }
}
```

Recommendations:

- Use `StringComparer.Ordinal` for variable dictionaries.
- Keep variable names stable once persisted in app/workspace files.
- Prefer simple values and DTOs for portable expressions.
- Keep large services and mutable state out of expression context.
- Validate app-specific expression strings before running important workflows.

## App Pattern

For application-authored workflows:

1. Validate that every configurable expression is present where required.
2. Build the runtime with the expression engine selected by the application.
3. Keep link conditions for routing only.
4. Put transformations in nodes or package-owned mapper objects.
5. Emit diagnostics from nodes when expression-driven routing or mapping affects
   operational behavior.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| item is not delivered | Verify the link condition returns `true` for that item. |
| build fails with `MissingExpressionEngine` | Pass an `IFlowExpressionEngine` to `ApplicationRuntimeBuilder` or `FlowApplicationHost.Create(...)`, or remove link `when` conditions. |
| build succeeds but runtime faults | Check condition syntax and message shape at runtime. |
| expression cannot see data | Use `input` or `value`, or provide a custom context factory. |
| mapper is not used | Register/use the mapper inside the node; graph links do not apply mappers automatically. |
| expression is hard to maintain | Move business rules into C# node code and keep the expression as a small routing condition. |

Next: [Package Versioning](11-package-versioning.md)

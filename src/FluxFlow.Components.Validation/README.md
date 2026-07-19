# FluxFlow.Components.Validation

Standalone JSON Schema validation for immutable workflow values. The package
depends on `FluxFlow.Data` and `FluxFlow.Nodes`, not Engine or JSON composition.

## Canonical Node

| Node | Input | Output |
|------|-------|--------|
| `FlowValueJsonSchemaValidatorNode` | `FlowMessage<FlowValue>` | `FlowMessage<FlowResult<JsonSchemaFlowValueValidationResult>>` |

The canonical node evaluates ordinary JSON semantics represented by
`FlowValue`. Objects, arrays, scalar numeric kinds, strings, booleans, null,
binary values, temporal values, durations, and GUIDs are converted
deterministically for JSON Schema evaluation without changing the input value.

```csharp
var options = new JsonSchemaValidatorOptions
{
    Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "id", "total" },
        properties = new
        {
            id = new { type = "string" },
            total = new { type = "number" }
        }
    }),
    SchemaId = "orders"
};

await using var node = new FlowValueJsonSchemaValidatorNode(
    options.LoadSchema(),
    schemaId: options.SchemaId,
    options: options);

node.Output.LinkTo(resultSink);
await node.Input.SendAsync(FlowMessage.Create(orderValue));
```

`Valid` and `Invalid` are both successful result kinds. The domain result keeps
the exact input and selected `FlowValue`, an `IsValid` flag, schema identity,
selector name, timestamp, and structured validation issues. Missing input,
selector failure, and schema evaluation failure use stable error result kinds
and `FlowError` codes on the same Output. Later inputs continue after expected
failures.

The canonical node has `Input`, `Output`, and `Events`. It does not expose
universal Errors or branch ports. Message correlation, trace, headers, and
causation are preserved through `FlowMessage.With(...)`.

## Value Selection

The default selector validates the complete input. Implement
`IJsonSchemaFlowValueSelector` when a component should validate a nested value:

```csharp
public sealed class BodySelector : IJsonSchemaFlowValueSelector
{
    public FlowValue Select(
        FlowValue input,
        JsonSchemaValidatorContext context)
        => input.GetObject()["body"];
}
```

The selected value remains a `FlowValue`; selectors do not introduce arbitrary
CLR object conversion. The configured `valueSelector` is descriptive context
for the selector and result. `payloadSelector` remains a compatibility alias.

## Schema And Timing

`JsonSchemaValidatorOptions.LoadSchema()` compiles inline `Schema` or
`SchemaPath` once before the node processes messages. Missing or malformed
schemas fail activation. Blank `InputType` and non-positive `BoundedCapacity`
also reject construction.

Results and Events use the supplied `TimeProvider`, defaulting to
`TimeProvider.System`. The package does not own schema files, selectors,
clocks, or their lifetimes.

## Compatibility Node

`JsonSchemaValidatorNode<TInput>` remains available unchanged. It emits
`JsonSchemaValidationResult<TInput>` on Output, fans the original message to
Valid or Invalid, and reports selection/conversion/evaluation failures through
its legacy Errors port. `IJsonSchemaValueSelector<TInput>` remains available
for that code-authored surface.

New composition definitions should use the canonical FlowValue node. The
generic node is retained for explicit migration and strongly typed hosts.

## Composition

`FluxFlow.Components.Validation.Composition` registers the canonical fixed
`json.schema-validator` type with parameterless `RegisterJsonSchemaValidator()`.
The adapter binds options, loads the schema during composition build, and
resolves optional host-owned `IJsonSchemaFlowValueSelector` and `TimeProvider`
resources.

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterJsonSchemaValidator());
```

The explicit generic overload remains available under a custom node type:

```csharp
registry.RegisterJsonSchemaValidator<OrderMessage>(
    "json.schema-validator.legacy-order");
```

`FlowResult<T>` is a real typed output. FluxFlow does not implicitly unwrap its
`Value` into a downstream `T` input; route it to a result-aware component or use
an explicitly registered mapper for that result type.

# FluxFlow.Components.Validation

Standalone JSON Schema validation for immutable workflow values. The package
depends on `FluxFlow.Data` and `FluxFlow.Nodes`, not Engine or JSON composition.

## Node Contract

| Node | Input | Output |
|------|-------|--------|
| `FlowValueJsonSchemaValidatorNode` | `FlowMessage<FlowValue>` | `FlowMessage<FlowResult<JsonSchemaFlowValueValidationResult>>` |

The node evaluates ordinary JSON semantics represented by
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

`Valid` and `Invalid` are successful result kinds. The domain result keeps
the exact input and selected `FlowValue`, an `IsValid` flag, schema identity,
selector name, timestamp, and structured validation issues. Missing input,
selector failure, and schema evaluation failure use stable error result kinds
and `FlowError` codes on the same Output. Later inputs continue after expected
failures.

The node has `Input`, `Output`, and `Events`. It does not expose
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
for the selector and result. Convert CLR values explicitly at the application
boundary before sending them to the node.

## Schema And Timing

`JsonSchemaValidatorOptions.LoadSchema()` compiles inline `Schema` or
`SchemaPath` once before the node processes messages. Missing or malformed
schemas fail activation. Blank `InputType` and non-positive `BoundedCapacity`
also reject construction.

Results and Events use the supplied `TimeProvider`, defaulting to
`TimeProvider.System`. The package does not own schema files, selectors,
clocks, or their lifetimes.

## Composition

`FluxFlow.Components.Validation.Composition` registers the canonical fixed
`json.validate` type with parameterless `RegisterJsonSchemaValidator()`.
The adapter binds options, loads the schema during composition build, and
resolves optional host-owned `IJsonSchemaFlowValueSelector` and `TimeProvider`
resources.

```csharp
services
    .AddFluxFlowApplication(configuration)
    .UseRuntimeAssembler(runtime => runtime
        .RegisterNodes(registry => registry.RegisterJsonSchemaValidator()));
```

`FlowResult<T>` is a real typed output. FluxFlow does not implicitly unwrap its
`Value` into a downstream `T` input; route it to a result-aware component or use
an explicitly registered mapper for that result type. Replace older Valid,
Invalid, and Errors branches with link conditions over `Kind`, `IsError`, and
`Error.Code`.

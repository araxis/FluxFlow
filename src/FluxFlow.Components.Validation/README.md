# FluxFlow.Components.Validation

A standalone JSON schema validator node for FluxFlow. It depends only on
`FluxFlow.Nodes` — no engine, registry, or runtime. You `new` the node and
`LinkTo` the next one.

## Node

| Node | Shape | Purpose |
|------|-------|---------|
| `JsonSchemaValidatorNode<TInput>` | `Input` -> `Output` (result), `Valid`, `Invalid` | Validates a selected value with a JSON schema. |

Every message travels as a `FlowMessage<T>` envelope. The validation result is
broadcast on `Output` as `FlowMessage<JsonSchemaValidationResult<TInput>>`; the
original input is fanned to `Valid` or `Invalid` (each `FlowMessage<TInput>`).
All three carry the same correlation id as the input.

```csharp
var schema = new JsonSchemaValidatorOptions
{
    Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "id" },
        properties = new { id = new { type = "string" } }
    }),
    SchemaId = "orders"
}.LoadSchema();

await using var node = new JsonSchemaValidatorNode<JsonElement>(schema, schemaId: "orders");

node.Output.LinkTo(resultSink, new DataflowLinkOptions { PropagateCompletion = false });
node.Valid.LinkTo(validSink, new DataflowLinkOptions { PropagateCompletion = false });
node.Invalid.LinkTo(invalidSink, new DataflowLinkOptions { PropagateCompletion = false });

await node.Input.SendAsync(FlowMessage.Create(order));
```

`JsonSchemaValidatorOptions.LoadSchema()` compiles the schema once (from inline
`Schema` or `SchemaPath`), so the node never performs File I/O or compilation in
its pump. A missing or malformed schema throws `InvalidOperationException` at
that point — configuration mistakes fail fast.

## Value selectors

By default the node validates the whole payload. Pass an
`IJsonSchemaValueSelector<TInput>` to select a value from the payload (e.g. an
inner property), plus a `valueSelector` name carried into the validation result
and selector context:

```csharp
var node = new JsonSchemaValidatorNode<AppMessage>(
    schema,
    selector: new PayloadSelector(),
    valueSelector: "payload");
```

The node accepts `JsonElement`, `JsonDocument`, `JsonNode`, `byte[]`, `string`
(parsed as JSON when possible, otherwise treated as a string value), and plain
objects (serialized) as selected values.

## Behavior

Invalid data is not an error: the node emits a result and routes the original
input to `Invalid`. Value-selection, value-conversion, and schema-evaluation
failures emit a `FlowError` on `Errors` (carrying the input's correlation id and
a `Code` from `ValidationErrorCodes`) and the node keeps processing later
messages. A one-time `json.schema-validator.loaded` event is emitted on
construction, and per-message `valid`/`invalid`/`failed` events flow on the
`Events` port.

## Runtime timing

Validation results use the node's clock for `Timestamp` (default
`TimeProvider.System`). Provide a deterministic clock for tests:

```csharp
new JsonSchemaValidatorNode<JsonElement>(schema, clock: new FakeTimeProvider(timestamp));
```

## Composition

The optional `FluxFlow.Components.Validation.Composition` package registers
closed generic `json.schema-validator` factories for `FluxFlow.Composition`.
The adapter binds `JsonSchemaValidatorOptions`, compiles inline `schema` or
`schemaPath` during composition build, and resolves optional keyed
`IJsonSchemaValueSelector<TInput>` and `TimeProvider` resources owned by the
host.

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry =>
        registry.RegisterJsonSchemaValidator<JsonElement>());
```

Use custom node type strings for multiple input shapes, for example
`json.schema-validator.order` and `json.schema-validator.http`. `InputType`
remains diagnostic metadata; the CLR port type comes from the closed generic
registration.

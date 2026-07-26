# Expression Mapping

FluxFlow keeps expression execution behind `FluxFlow.Mapping`. Component and
engine packages depend on the abstraction, while a host chooses and registers a
concrete expression engine. Payload representation is not selected to suit one
expression language.

## Typed Mapping

Use `FlowMapperNode<TInput,TOutput>` when input and output shapes are known.
The expression is compiled when the node is constructed and reused for every
message. The default context exposes the exact input as both `input` and
`value`.

```csharp
public sealed record Order(string OrderId, decimal Total);
public sealed record Invoice(string InvoiceId, decimal Amount);

var node = new FlowMapperNode<Order, Invoice>(
    new MapperOptions
    {
        Expression = "new Invoice(input.OrderId, input.Total)"
    },
    expressionEngine);

var results = new BufferBlock<FlowMessage<Invoice>>();
node.Output.LinkTo(results);

await node.Input.SendAsync(FlowMessage.Create(
    new Order("order-42", 125.50m)));
```

The selected expression engine receives normal CLR values. A C# engine can use
properties and methods directly. Delegate mappers and predicates also receive
typed values and do not require serialization.

## Value-or-error Output

Mapping success emits `FlowMessage<TOutput>`. Evaluation or output-conversion
failure emits the same output type with `IsError == true` and a
`FlowError` whose stable code is `mapping.failed`. An incoming error is
propagated without evaluating the expression.

Use normal conditional links to separate success and failure when topology
requires branches:

```json
{
  "Workflows": {
    "Orders": {
      "MapInvoice": {
        "Type": "data.map",
        "Expression": "...",
        "Output": [
          { "Port": "PersistInvoice.Input", "Condition": "isError == false" },
          { "Port": "RecordFailure.Input", "Condition": "isError == true" }
        ],
        "Engine": "Resources.Expressions.Default"
      }
    }
  }
}
```

There is no separate Failed or Errors port and no result wrapper.

## Canonical Runtime Link Conditions

`FluxFlow.Composition` compiles `data.map` output conditions against the stable
message JSON projection. The optional `ApplicationRuntimeAssembler` activates
those compiled links and their typed ports; it does not evaluate mapping
expressions or insert conversions. A condition can inspect `isError`,
`error.code`, headers, or the mapped value without requiring a parallel error
stream.

## Custom Context

Implement `IMappingContextFactory` to add immutable per-message variables:

```csharp
public sealed class TenantContextFactory : IMappingContextFactory
{
    public FlowMapContext Create(object? input, MappingNodeContext context)
        => new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input,
                ["tenant"] = "north"
            }
        };
}
```

The context copies its variable map. Do not place mutable workflow-wide state
in it. Engine-specific adaptation belongs in the engine implementation.

## Schema-less JSON

`JsonMapperNode` is the explicit `JsonElement` specialization used by the
configuration-driven `data.map` registration. It accepts and emits detached
JSON values. Use it only when JSON semantics are part of the workflow contract.

Known application types should stay typed. If a workflow intentionally needs a
dynamic CLR shape, make that conversion explicit in a mapper and emit the
chosen record, dictionary, or `ExpandoObject`. Such values are ordinary user
payloads, not FluxFlow foundation types.

## Composition Registration

`FluxFlow.Components.Mapping.Composition` registers the schema-less JSON node:

```csharp
var registry = new CompositionNodeRegistry()
    .RegisterMapper();
```

The host provides a keyed `IFlowExpressionEngine`; `IMappingContextFactory` and
`TimeProvider` are optional host-owned resources. Metadata exposes one
`JsonElement` Input, one `JsonElement` Output, and Events. `InputType` and
`OutputType` remain diagnostic/configuration hints and do not introduce runtime
reflection or automatic conversion.

## Expression Adapter Rules

- Compile expressions once during node construction or activation.
- Pass typed values directly to typed engines.
- Let JSON-oriented engines project or consume `JsonElement` explicitly.
- An engine may create an internal read-only dynamic view for evaluation, but
  that view must not become a public component or persistence contract.
- Do not add assembly scanning, reflection registration, implicit link
  conversion, or a universal dynamic object.

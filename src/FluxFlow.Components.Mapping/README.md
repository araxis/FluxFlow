# FluxFlow.Components.Mapping

Standalone typed expression mapping. No Engine or Composition dependency.

`FlowMapperNode<TInput,TOutput>` accepts `FlowMessage<TInput>` and emits
`FlowMessage<TOutput>`. The selected `IFlowExpressionEngine` compiles the
expression during construction. The default context exposes the exact input as
`input` and `value`; an `IMappingContextFactory` may add host variables.

```csharp
var node = new FlowMapperNode<Order, Invoice>(
    new MapperOptions { Expression = "new Invoice(input.Id, input.Total)" },
    engine);
```

An incoming error is propagated without evaluation. Evaluation/conversion
failure becomes `FlowError` on Output. `JsonMapperNode` is the explicit
`JsonElement` specialization for schema-less JSON. A mapper may intentionally
emit a CLR record, dictionary, or `ExpandoObject`; none is a universal runtime
contract.

## Composition

Install `FluxFlow.Components.Mapping.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.

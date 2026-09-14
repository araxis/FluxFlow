# FluxFlow.Components.Mapping

Standalone expression mapping over canonical immutable values. No Engine or Composition dependency.

`FlowMapperNode<TInput,TOutput>` accepts and emits `FlowMessage`. It materializes
the owned `FlowValue` into TInput locally and wraps the mapped TOutput as a new
structured value. The selected `IFlowExpressionEngine` compiles the expression
during construction. The default context exposes the same materialized input as
`input` and `value`; an `IMappingContextFactory` may add host variables.

```csharp
var node = new FlowMapperNode<Order, Invoice>(
    new MapperOptions { Expression = "new Invoice(input.Id, input.Total)" },
    engine);
```

Add the constructed instance directly to a workflow; type parameters describe
evaluation, not a separate transport or runtime.

An incoming error is forwarded as the same message without evaluation. Evaluation/conversion
failure becomes `FlowError` on Output. `JsonMapperNode` is the explicit
`JsonElement` specialization of the same implementation, without conversion
queues. A mapper may return a record, dictionary, or dynamic object internally;
the output boundary snapshots it into the canonical immutable value.

Version 8.0.0-alpha.1 is an unpublished development preview, not a release candidate.

## Composition

Install `FluxFlow.Components.Mapping.Composition` for optional component construction and registration. This runtime package remains free of Composition, Designer, and Engine dependencies.

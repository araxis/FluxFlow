# FluxFlow.Components.Mapping.Composition

Runtime registration has no Designer dependency. Visual consumers add
FluxFlow.Components.Mapping.Designer and call AddMappingDesign after AddMapping.
Direct constructors and runtime behavior are unchanged.


Optional structural configuration registration for `data.map`.

`AddMapping()` registers the configured `data.map` component with canonical
`FlowValue` Input and Output ports, plus Events. It materializes input to
`JsonElement` for evaluation. It is intentionally JSON-oriented because
configuration-driven documents are schema-less; code-authored workflows use
`FlowMapperNode<TInput,TOutput>` directly for known CLR contracts.

The host supplies a keyed `IFlowExpressionEngine`; context factory and clock
resources are optional. Mapping errors travel on Output and can be routed by
`isError` and `error.code`. There is no Failed or Errors port.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddMapping();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddMapper` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former MappingComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.

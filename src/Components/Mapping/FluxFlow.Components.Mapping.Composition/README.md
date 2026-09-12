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

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit MappingComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddMapping();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`MappingComponents.Mapper` is the typed contract used by both generic `AddComponent` and `AddMapper`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.

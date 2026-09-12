# FluxFlow.Components.State.Composition

Optional runtime registration and authoring for keyed JSON state reduction.

The JsonStateReducerNode receives canonical FlowMessage values and materializes
its reducer input locally. Output carries results or errors; Events is diagnostic.
Reducer/key expressions, initial state and key limits stay flat. The expression
engine is required and host-owned; the host-owned clock is optional.

Editor hints are separate: install `FluxFlow.Components.State.Designer` and call
`AddStateDesign()` after `AddState()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit StateComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddState();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`StateComponents.StateReducer` is the typed contract used by both generic `AddComponent` and `AddStateReducer`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.

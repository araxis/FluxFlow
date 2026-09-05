# FluxFlow.Components.Expectations.Composition

Optional registration and Designer metadata for `event.expect`.

Metadata declares `ProjectionEvent` Input, `EventExpectationResult` Output,
and Events. Rules, evaluation, result, diagnostic, and runtime options remain
flat. Optional evaluator and clock resources are host-owned and carry delegate
and clock picker hints. Errors share Output.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit ExpectationsComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddExpectations();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`ExpectationsComponents.EventExpectation` is the typed contract used by both generic `AddComponent` and `AddEventExpectation`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor. Normal
code-first hosting therefore calls only `AddFluxFlow(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.

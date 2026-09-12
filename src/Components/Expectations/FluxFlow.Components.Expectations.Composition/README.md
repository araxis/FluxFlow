# FluxFlow.Components.Expectations.Composition

Optional runtime registration and authoring for `event.expect`, independent of presentation.

Metadata declares `ProjectionEvent` Input, `EventExpectationResult` Output,
and Events. Rules, evaluation, result, diagnostic, and runtime options remain
flat. The optional clock resource is host-owned. Errors share Output.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit ExpectationsComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddExpectations();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

Visual hosts can reference `FluxFlow.Components.Expectations.Designer` and call
`AddExpectationsDesign()` after `AddExpectations()`. The opt-in metadata decorates
the existing descriptor without activation or resource resolution and retains
palette labels, defaults, expectation choices and clock picker hints.

## Code-first authoring

`ExpectationsComponents.EventExpectation` is the typed contract used by both generic `AddComponent` and `AddEventExpectation`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.

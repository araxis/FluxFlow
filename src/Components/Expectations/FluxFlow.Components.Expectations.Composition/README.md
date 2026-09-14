# FluxFlow.Components.Expectations.Composition

Optional runtime registration and definition for `event.expect`, independent of presentation.

Metadata declares `ProjectionEvent` Input, `EventExpectationResult` Output,
and Events. Rules, evaluation, result, diagnostic, and runtime options remain
flat. The optional clock resource is host-owned. Errors share Output.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddExpectations();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

Visual hosts can reference `FluxFlow.Components.Expectations.Designer` and call
`AddExpectationsDesign()` after `AddExpectations()`. The opt-in metadata decorates
the existing descriptor without activation or resource resolution and retains
palette labels, defaults, expectation choices and clock picker hints.

## Code-first definition

`AddEventExpectation` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former ExpectationsComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.

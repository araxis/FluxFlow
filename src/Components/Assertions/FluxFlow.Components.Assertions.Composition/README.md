# FluxFlow.Components.Assertions.Composition

Optional runtime registration and authoring for `data.assert`, independent of presentation.

`AssertionsComponentDefinition` is the package-owned source of canonical type,
option, resource and port names.

The registration uses `JsonAssertionNode`: one `JsonElement` Input, one
`AssertionResult<JsonElement>` Output, and Events. Passed/failed results and
in-band errors are routed with link conditions; there are no Passed, Failed, or
Errors ports.

The expression engine is a required host-owned keyed resource. Context factory
and clock resources are optional. Presentation hints live in the separate optional
`FluxFlow.Components.Assertions.Designer` package.

## DI Registration

This optional application-integration adapter uses one structural component
registration. Its `ComponentDescriptor` owns the canonical type, options,
resources, processing capabilities, and typed ports:

```csharp
services.AddFluxFlowComponents().AddAssertions();
```

The resulting `ComponentCatalog` is built once from DI registrations. No presentation
services are registered. Standalone runtime nodes remain usable without this
package, and referenced external resources remain host-owned.

Visual hosts can reference `FluxFlow.Components.Assertions.Designer` and call
`AddAssertionsDesign()` after `AddAssertions()`. This decorates the registered
descriptor without creating a component or resolving host resources. It retains
palette labels, expression-editor hints, defaults and required-resource information.

## Code-first authoring

`AssertionsComponents.Assertion` is the typed contract used by both generic `AddComponent` and `AddAssertion`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.

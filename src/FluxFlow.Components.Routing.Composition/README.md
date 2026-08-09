# FluxFlow.Components.Routing.Composition

Optional JSON registration and Designer metadata for `flow.window`,
`flow.correlate`, and `flow.join`.

The configured nodes use `JsonElement` specializations. Window has one Input;
Correlation has one Input and host-owned key/side selector delegates; Join has
Left and Right inputs with host-owned key selectors. Each exposes one typed
outcome Output and Events. Match, timeout, and error routing use normal link
conditions; there are no compatibility branch or Errors ports.

Selector resources use delegate picker hints and clocks use clock picker hints.
Composition does not own these resources.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit RoutingComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddRouting();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`RoutingComponents` exposes `Window`, `Correlation`, and `Join` typed contracts. The retained `AddX` methods use those same contracts; named handles include branch inputs, output, and `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.

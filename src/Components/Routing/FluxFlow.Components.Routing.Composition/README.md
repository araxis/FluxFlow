# FluxFlow.Components.Routing.Composition

Structural JSON registration and portable authoring for `flow.window`,
`flow.correlate`, and `flow.join`.

The configured nodes expose canonical `FlowValue` ports and materialize JSON locally. Window has one Input;
Correlation has one Input and host-owned key/side selector delegates; Join has
Left and Right inputs with host-owned key selectors. Each exposes one canonical
outcome Output and Events. Match, timeout, and error routing use normal link
conditions; there are no compatibility branch or Errors ports.

Composition does not own selector or clock resources and has no Designer dependency.
Visual consumers add FluxFlow.Components.Routing.Designer and call AddRoutingDesign
following AddRouting to retain delegate/clock pickers, editor hints and defaults.

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
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.

# FluxFlow.Components.Sessions.Composition

Optional runtime registration and authoring for session recording, replay and query.

Ports carry canonical FlowMessage values; content/query materialization remains
inside the component. Store, session, replay/query and timing options stay flat.
Errors share Output, with a separate diagnostic Events port. Borrowed stores and
clocks remain host-owned; activations retain cleanup for factory-owned stores.

Editor hints are separate: install `FluxFlow.Components.Sessions.Designer` and call
`AddSessionsDesign()` after `AddSessions()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit SessionsComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddSessions();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`SessionsComponents` exposes `SessionRecorder`, `SessionReplay`, and `SessionQuery` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.

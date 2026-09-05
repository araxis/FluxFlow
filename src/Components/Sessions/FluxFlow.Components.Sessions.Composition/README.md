# FluxFlow.Components.Sessions.Composition

Optional registrations and Designer metadata for session record, replay, and
query.

Metadata declares the runtime package's typed content/query contracts, Output,
and Events. Store, session name, replay/query, timing, and runtime options remain
flat. Store and clock references resolve host-owned keyed resources.

Errors share normal Output. There are no Sessions or Errors compatibility
ports, and Composition does not own the store.

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

`SessionsComponents` exposes `SessionRecorder`, `SessionReplay`, and `SessionQuery` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.

# FluxFlow.Components.Sessions.Composition

Optional runtime registration and definition for session recording, replay and query.

Ports carry canonical FlowMessage values; content/query materialization remains
inside the component. Store, session, replay/query and timing options stay flat.
Errors share Output, with a separate diagnostic Events port. Borrowed stores and
clocks remain host-owned; activations retain cleanup for factory-owned stores.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddSessions();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddSessionRecorder`, `AddSessionReplay`, `AddSessionQuery` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former SessionsComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.

Owned leases transfer disposal through context.SetLifetime; borrowed leases remain shared. Failed constructors release acquired leases before returning an error.

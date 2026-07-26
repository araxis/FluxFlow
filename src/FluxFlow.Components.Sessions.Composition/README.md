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
entries and exactly one SessionsComponentDesignMetadataProvider metadata provider through `IServiceCollection`:

```csharp
services.AddSessionsComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

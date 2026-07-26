# FluxFlow.Components.Serialization.Composition

Optional registrations and Designer metadata for JSON, text, and Base64
conversion nodes.

Descriptors expose the exact `FlowContent`, `JsonElement`, or string port types
listed by the runtime package, one Output, and Events. Encoding, JSON, size, and
runtime options remain flat. The optional clock is host-owned.

Errors share Output. No codec catalog, result wrapper, or universal Errors port
is registered.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and exactly one SerializationComponentDesignMetadataProvider metadata provider through `IServiceCollection`:

```csharp
services.AddSerializationComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

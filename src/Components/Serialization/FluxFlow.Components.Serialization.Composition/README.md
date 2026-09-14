# FluxFlow.Components.Serialization.Composition

Optional runtime registrations and definition for JSON, text, and Base64 conversion
nodes. This package does not reference Designer or register visual metadata.

Descriptors expose canonical `FlowValue` ports with explicit input shapes,
one Output, and Events. Encoding, JSON, size, and
runtime options remain flat. The optional clock is host-owned.

Errors share Output. No codec catalog, result wrapper, or universal Errors port
is registered.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddSerialization();
```

For editor metadata, reference `FluxFlow.Components.Serialization.Designer` and
explicitly call `.AddSerializationDesign()` after `.AddSerialization()`. Existing
labels, defaults, hints and clock pickers are retained in that optional package.

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddJsonParse`, `AddJsonStringify`, `AddTextEncode`, `AddTextDecode`, `AddBase64Encode`, `AddBase64Decode` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former SerializationComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.

# FluxFlow.Components.Payloads.Composition

Optional registration and Designer metadata for `payload.inspect`.

The descriptor declares `FlowContent` Input,
`PayloadInspectionResult` Output, and Events. Limit, preview, detection,
formatting, and runtime options remain flat. The optional clock is host-owned.

There is no codec-catalog resource, cached decoded value, result wrapper, or
Errors port. Parsing needed by downstream nodes is an explicit Serialization
step.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit PayloadsComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddPayloadsComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

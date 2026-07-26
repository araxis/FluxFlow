# FluxFlow.Components.Resilience.Composition

Optional `FluxFlow.Composition` registration and Designer metadata for
`flow.retry`.

The configured node is the schema-less JSON specialization. Metadata exposes
Input, Ack, Nak, Cancel, Output, and Events. Output carries typed retry signals
or `FlowError`; there is no Errors port or nested result wrapper.

Retry schedule, limits, timeout, and semantic processing options are flat.
`clock` and `jitter` references resolve host-owned keyed resources. Signal ports
remain explicit bounded feedback relations, so Ack/Nak/Cancel links do not make
ordinary data cycles valid.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and exactly one ResilienceComponentDesignMetadataProvider metadata provider through `IServiceCollection`:

```csharp
services.AddResilienceComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

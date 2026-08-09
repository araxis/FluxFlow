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
entries and explicit ResilienceComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddResilience();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`ResilienceComponents.FlowRetry` is the typed contract used by both generic `AddComponent` and `AddFlowRetry`. Its handle exposes `Input`, `Ack`, `Nak`, `Cancel`, `Output`, and `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor. Normal
code-first hosting therefore calls only `AddFluxFlow(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.

# FluxFlow.Components.Timers.Composition

Optional timer registrations and Designer metadata.

Interval and Schedule expose typed tick Outputs. Delay, Throttle, and Debounce
use the JSON specializations in configuration-driven workflows. Duration and
boolean options remain flat; timing, schedule, diagnostic, and runtime sections
provide Designer hints. Schedule keeps its explicit omitted time-zone option.

The optional clock is host-owned. Each descriptor exposes normal Output and
Events, with no result wrapper or Errors port.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit TimersComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddTimers();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`TimersComponents` exposes `IntervalTimer`, `ScheduleTimer`, `Delay`, `Throttle`, and `Debounce` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.

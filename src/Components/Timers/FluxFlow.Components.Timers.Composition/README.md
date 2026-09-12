# FluxFlow.Components.Timers.Composition

Optional runtime registration and authoring for interval/scheduled sources and delay/throttle/debounce transforms.

Ports carry canonical FlowMessage values. Sources emit ticks; the three transforms
work on FlowValue and forward the original message on success or upstream error.
This adapter constructs the same public nodes available to direct Core graphs;
it does not select a separate timer implementation. Duration and boolean
options stay flat. Schedule retains the documented typed time-zone limitation.
Clocks are host-owned. Each component exposes Output and diagnostic Events.

Editor hints are separate: install `FluxFlow.Components.Timers.Designer` and call
`AddTimersDesign()` after `AddTimers()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

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

`TimersComponents` exposes `IntervalTimer`, `ScheduleTimer`, `Delay`, `Throttle`, and `Debounce` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.

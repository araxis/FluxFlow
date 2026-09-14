# FluxFlow.Components.Timers.Composition

Optional runtime registration and definition for interval/scheduled sources and delay/throttle/debounce transforms.

Ports carry canonical FlowMessage values. Sources emit ticks; the three transforms
work on FlowValue and forward the original message on success or upstream error.
This adapter constructs the same public nodes available to direct Core graphs;
it does not select a separate timer implementation. Duration and boolean
options stay flat. Schedule retains the documented typed time-zone limitation.
Clocks are host-owned. Each component exposes Output and diagnostic Events.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddTimers();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddIntervalTimer`, `AddScheduleTimer`, `AddDelay`, `AddThrottle`, `AddDebounce` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former TimersComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.

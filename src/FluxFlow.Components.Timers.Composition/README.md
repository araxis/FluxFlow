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
entries and exactly one TimersComponentDesignMetadataProvider metadata provider through `IServiceCollection`:

```csharp
services.AddTimersComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

# FluxFlow.Components.Timers.Composition

Optional timer registrations and Designer metadata.

Interval and Schedule expose typed tick Outputs. Delay, Throttle, and Debounce
use the JSON specializations in configuration-driven workflows. Duration and
boolean options remain flat; timing, schedule, diagnostic, and runtime sections
provide Designer hints. Schedule keeps its explicit omitted time-zone option.

The optional clock is host-owned. Each descriptor exposes normal Output and
Events, with no result wrapper or Errors port.

## Registration And Design Metadata

Register components with `RegisterTimerDebounce`, `RegisterTimerDelay`, `RegisterTimerInterval`, `RegisterTimerSchedule`, `RegisterTimerThrottle`. `TimersComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.

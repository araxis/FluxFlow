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

## Registration And Design Metadata

Register components with `RegisterFlowRetry`. `ResilienceComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.

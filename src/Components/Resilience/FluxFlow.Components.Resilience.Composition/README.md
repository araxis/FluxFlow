# FluxFlow.Components.Resilience.Composition

Optional runtime registration and definition for `flow.retry`, independent of presentation.

The configured node carries immutable workflow values. Its structural contract exposes
Input, Ack, Nak, Cancel, Output, and Events. Output carries typed retry signals
or `FlowError`; there is no Errors port or nested result wrapper.

Retry schedule, limits, timeout, and semantic processing options are flat.
`Clock` and `Jitter` references resolve host-owned keyed resources. Activation
constructs the same public `FlowRetryNode` used directly. Ack/Nak/Cancel are
canonical message inputs, exposed as `InputPortHandle<FlowValue>`, not custom
signal-target ports or terminal controls. Native admission is distinct from
feedback settlement. Existing cycle validation still applies.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddResilience();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

Visual hosts can reference `FluxFlow.Components.Resilience.Designer` and call
`AddResilienceDesign()` after `AddResilience()`. The opt-in metadata decorates the
registered descriptor without activation or resource resolution. Retry strategy
choices, defaults, limits, timing hints and feedback-input descriptions stay available.

## Code-first definition

`AddFlowRetry` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former ResilienceComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.

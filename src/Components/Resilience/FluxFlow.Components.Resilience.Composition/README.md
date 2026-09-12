# FluxFlow.Components.Resilience.Composition

Optional runtime registration and authoring for `flow.retry`, independent of presentation.

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

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit ResilienceComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddResilience();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

Visual hosts can reference `FluxFlow.Components.Resilience.Designer` and call
`AddResilienceDesign()` after `AddResilience()`. The opt-in metadata decorates the
registered descriptor without activation or resource resolution. Retry strategy
choices, defaults, limits, timing hints and feedback-input descriptions stay available.

## Code-first authoring

`ResilienceComponents.FlowRetry` is the typed contract used by both generic `AddComponent` and `AddFlowRetry`. Its handle exposes `Input`, `Ack`, `Nak`, `Cancel`, `Output`, and `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.

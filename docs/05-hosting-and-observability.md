# Hosting And Observability

`FluxFlow.Engine` owns the canonical hosted path. `FluxFlowApplication` is the
single lifecycle owner, and `AddFluxFlow(...)` registers both the application
and the hosted-service adapter that operates on that same singleton.

## Registration

```csharp
services
    .AddFluxFlow(configuration, options =>
    {
        options.InitialRevisionId = "deployment-42";
        options.StartWithHost = true;
        options.StopWithHost = true;
    })
    .AddMappingComponents()
    .AddHttpComponents();

var application = provider.GetRequiredService<FluxFlowApplication>();
```

Configuration may be the canonical root or a named section. A direct
`ApplicationDefinition`, custom source instance, or
`AddFluxFlow<TDefinitionSource>()` is also supported. A custom source remains
replaceable through standard DI; no source registry or assembly scanning is
used.

`FluxFlowApplicationOptions` is host setup, not workflow JSON. Port capacities
and hosted start/stop policy stay outside `Resources` and `Workflows`.

## Lifecycle And Revisions

```csharp
var started = await application.StartAsync();
var reloaded = await application.ReloadAsync("deployment-43");
var applied = await application.ApplyAsync("deployment-44", definition);
await application.StopAsync();
```

Source-based start/reload and direct apply share `ApplicationUpdateResult`.
The status is `Applied`, `Unchanged`, or `Rejected`; diagnostics identify source,
validation/planning, resource/component preparation, activation,
swap, drain, disposal, or event-publication stages.

Expected revision failures are values, not host-terminating exceptions. A
failed candidate never becomes current and is disposed. If an older revision
is active, it remains available. A successful candidate is prepared and
activated before stable ports switch atomically; the old candidate then drains
and is disposed. Cancellation remains an exception and never performs a
partial swap.

`State`, `Current`, `CurrentDefinition`, and `LastUpdate` are owned by the
application. Concurrent lifecycle calls are serialized through one gate.

## Stable Application Ports

Use the stable `application.Ports` facade after the first successful activation:

```csharp
var send = await application.Ports.SendAsync(
    "Orders.Validate.Input",
    FlowMessage.Create(order));

var result = await application.Ports.ReceiveAsync<OrderResult>(
    "Orders.Final.Output",
    TimeSpan.FromSeconds(10));

await using var observation = await application.Ports.ObserveAsync<OrderResult>(
    "Orders.Final.Output",
    capacity: 64);
```

The facade resolves the active generation for each operation. Stable addresses
survive compatible revision replacement. A surface-changing revision publishes
its new generation atomically, so callers do not construct generations,
revisions, binders, or leases.

`SendAsync` returns normal intake status. `ReceiveAsync` and `ObserveAsync` are
broadcast taps. `SendAndReceiveAsync` installs its waiter before sending and
correlates the response by `TraceId`.

## Resource Ownership

Composition families implement `IApplicationResourceRegistrar` from
`FluxFlow.Composition`. Registrars add keyed services to a revision-owned
service collection in deterministic order. Engine builds isolated providers
and disposes services it owns exactly once. Explicitly bridged external
singletons remain host-owned. MQTT and other adapter packages keep concrete
client, reconnect, credential, and transport ownership.

## Diagnostics

Each canonical component exposes a traced `Workflow.Component.Events` output.
Engine also exposes:

- `System.Events.Output` and `application.Ports.SystemEvents` for reliable,
  ordered application and revision transitions.
- `System.Diagnostics.Output` and `application.Ports.Diagnostics` for bounded,
  best-effort operational diagnostics.
- `application.Ports.Rejections`, `Status`, and `Completion` for direct runtime
  observation.

Accepted system events are delivered in order with bounded backpressure.
Diagnostic overflow rejects immediately; accepted diagnostics remain ordered.
Observer or listener failure is isolated from workflow processing.

`FlowError` remains normal workflow data. It can be mapped, filtered, routed,
retried, logged, or returned; operational diagnostics do not replace it.

## Removed Hosting Surface

Legacy registration, source, lifecycle, and keyed-DI forwarding APIs are no
longer shipped. Register the canonical definition directly with
`services.AddFluxFlow(...)`, register adapter-owned resources in keyed DI, and
resolve the single `FluxFlowApplication` lifecycle facade.

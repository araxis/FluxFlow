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
        options.InputCapacity = 256;
        options.OutputCapacity = 256;
    })
    .AddMapping()
    .AddHttp();

var application = provider.GetRequiredService<FluxFlowApplication>();
```

Configuration may be the canonical root or a named section. A direct
`ApplicationDefinition`, custom source instance, or
`AddFluxFlow<TDefinitionSource>()` is also supported. A custom source remains
replaceable through standard DI; no source registry or assembly scanning is
used.

A direct definition built from complete code-first component and application
resource contracts carries those exact executable contracts into candidate
activation. `AddFluxFlow(definition)` is the complete FluxFlow registration for
that graph; only ordinary host dependencies remain in DI. A JSON/configuration
source carries no executable C# and therefore keeps explicit package-family
registration.

```csharp
services.AddFluxFlow(
    configuration,
    options => options.StartWithHost = false,
    sectionName: "CustomFluxFlow");
```

`FluxFlowApplicationOptions` is host setup, not workflow JSON. Engine
stable-port capacities and hosted start/stop policy stay outside `Resources`
and `Workflows`. Its surface is limited to `InitialRevisionId`,
`StartWithHost`, `StopWithHost`, `InputCapacity`, and `OutputCapacity`.
FileSystem/SQL storage paths, session
stores, MQTT transports, credentials, certificates, clocks, and all
component-instance settings belong to their backend, host resource, or
application-definition boundaries and must not be added here.

`InputCapacity` and `OutputCapacity` configure the Engine's stable addressable
application ports. They do not override component-instance `BoundedCapacity`,
custom `FlowNodeOptions`, or custom `FlowSourceOptions`.

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

## Optional Application Readiness

Add `FluxFlow.Engine.HealthChecks` only when the host wants a standard .NET
readiness result for the canonical application:

```csharp
using FluxFlow.Engine.HealthChecks;

services.AddFluxFlow(definition);
services.AddHealthChecks()
    .AddFluxFlowApplication();
```

This registers one idempotent check named `fluxflow.application` with the exact
tags `fluxflow` and `ready`. It reads the existing `FluxFlowApplication`
singleton when the health service executes:

- `Healthy` means a usable revision is active.
- `Degraded` means the latest update was rejected while the previous active
  revision remains usable.
- `Unhealthy` means FluxFlow is missing, no revision is active, or the
  application is stopping or stopped.

The result includes at most lifecycle state, active revision ID and sequence,
requested revision ID, update status, and the final diagnostic stage and code.
It excludes payloads, definitions, addresses, diagnostic text/details,
exceptions, paths, connections, and secrets. The adapter performs no polling,
I/O, resource probing, reflection, or background work. ASP.NET Core endpoint
wiring remains host-owned, for example `app.MapHealthChecks("/health/ready")`.

Readiness does not claim process liveness, durable-backlog health, or external
dependency availability. Keep those as separate host policies. See
[Application Health Readiness](42-application-health-readiness.md).

## Optional Durability Instrumentation

The provider-neutral durable-input and durable-output packages publish
standard BCL `ActivitySource` and `Meter` signals. A host can attach ordinary
.NET listeners or an OpenTelemetry-compatible bridge of its choice; FluxFlow
does not register an exporter, telemetry SDK, health check, polling service, or
dashboard. Signals exist only when an optional durability package executes its
configured durable operations.

| Package | `ActivitySource` and `Meter` name |
|---------|-----------------------------------|
| `FluxFlow.Engine.DurableInput` | `FluxFlow.Engine.DurableInput` |
| `FluxFlow.Engine.DurableOutput` | `FluxFlow.Engine.DurableOutput` |

Metric tags are deliberately low-cardinality outcomes, results, failure kinds,
and store operation names. Addresses, contracts, message ids, trace ids,
correlation/causation ids, lease identities, payloads, headers, connection
details, paths, owners, exception text, and credentials are never metric tags.
Activities may carry `flow.trace_id` and the delivery attempt so a host can
correlate sampled work without turning identity into a metric dimension.

Listener failure is isolated from capture and dispatch. Hosts that do not
enable these package-local instruments pay no exporter dependency or polling
cost, and ordinary non-durable Engine ports remain unchanged. Exact instruments
and semantic recording points are documented with
[durable inputs](25-durable-inputs.md),
[durable output capture](27-durable-output-capture.md), and
[durable output delivery](29-durable-output-delivery.md).

The runnable
[`FluxFlow.DurabilityOperationsSample`](../samples/FluxFlow.DurabilityOperationsSample/README.md)
shows a normal Generic Host constructing and disposing `MeterListener` and
`ActivityListener` directly. Its callbacks collect only static operation names
and bounded semantic results; they perform no I/O and omit payload and identity
data. The sample also demonstrates that event telemetry and persisted status
are separate: listeners observe live transitions, while the host requests each
status snapshot explicitly.

## Removed Hosting Surface

Legacy registration, source, lifecycle, and keyed-DI forwarding APIs are no
longer shipped. Register the canonical definition directly with
`services.AddFluxFlow(...)`, register adapter-owned resources in keyed DI, and
resolve the single `FluxFlowApplication` lifecycle facade.

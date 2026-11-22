# Hosting And Observability

FluxFlow.Application provides ordinary hosting through `AddFluxFlow()` in
`FluxFlow.Application`. Register configuration with
`services.AddFluxFlowApplication(configuration)` and the component setup
extensions before building the host provider. Host startup composes FlowApplication,
connects routes and starts sources. Shutdown stops and drains processing before the
workflow DI scopes dispose nodes and local resources; host disposal releases shared resources.

```csharp
services.AddFluxFlow();
```

The [console host sample](../Code/samples/Hosting/FluxFlow.Hosting.Sample/README.md) runs
two configured Scheduler/DataGenerator workflows, discovers registered addresses, and
observes output and events. It stops/restarts one workflow while the other continues,
reattaches probes to the new run, and verifies shutdown. Its disk location
matches `samples / Hosting / FluxFlow.Hosting.Sample` in the Visual Studio solution.

For manual lifetime control, resolve with `await provider.BuildApplicationAsync()`,
call `StartAsync`, then stop/dispose the application before disposing the provider.
Build returns the same registered singleton on repeated calls. Configuration changes
require rebuilding/restarting the host; this package has no hot reload or revision API.

## Workflow stop and restart

After application startup, an independent `FlowWorkflow` can stop and restart with
unchanged configuration. A successful `StopAsync` drains processing and disposes that
run's DI scope. `StartAsync` then creates fresh components and workflow-local resources,
reconnects existing links and starts sources. It does not reload configuration or rebuild
the host provider. Reacquire workflow services, components, ports and `Completion` after
restart, and attach output/event probes again. Other independent workflows keep running.

Wait for a successful stop before restarting. Starting during stop, or after failed
cleanup, is rejected. Workflows with incoming or outgoing cross-workflow links require
application-wide stop. `FlowApplication.StopAsync` is terminal; restarting an individual
workflow keeps the host running, but does not restart a stopped application. See the
[lifetime contract](../Code/src/FluxFlow.Application/README.md#workflow-stop-and-restart).

ASP.NET Core routes remain mapped across workflow restart and resolve the current run
for each request. Both shutdown modes reject new requests with 503 while the workflow
is stopping or stopped. Each HttpTrigger selects how accepted requests are handled:

- `Cancel` (the default) cancels pending reply waits with 503 where the connection remains available.
- `Drain` allows accepted requests to finish; existing reply timeouts and client
  cancellation still apply.

In either mode, acknowledgement waits only for publication, not downstream completion.

The [HTTP sample](../Code/samples/Hosting/FluxFlow.Hosting.Http.Sample/README.md#drain-stop-and-restart)
demonstrates 200, stopped 503, restart and 200 using the existing APIs. It stops an idle
workflow; the hosting tests cover in-flight draining and concurrent replies. See the
[HTTP hosting guide](../Code/src/Plugins/FluxFlow.Plugins.Http/Hosting/FluxFlow.Hosting.AspNetCore/README.md) for trigger settings.

Canceling the workflow stop token while stop is pending, including deadline expiry,
faults the run and rejects restart; cleanup continues and the application-wide fault
policy applies. For HttpTrigger, remaining waits are canceled and correlations released.
The Generic Host shutdown deadline also applies; neither deadline can
forcibly terminate component code that ignores cancellation. Continue disposing the host
after a failed stop; disposal can report the original failure.

## Host extension scope

Hosts discover registered components and ports through `GetComponentAddresses`,
`GetInputPortAddresses` and `GetOutputPortAddresses` on their existing service provider.
Discovery does not construct components or report runtime readiness. `ObserveOutput`
attaches to a declared output, including Events, and disposing the probe detaches it.
See the [Application guide](../Code/src/FluxFlow.Application/README.md) for usage and ownership.

Probes observe output offers, not confirmed delivery or processing. Their bounded
buffers drop samples when full; `DroppedCount` reports that loss. Retained-message
replay and repeated offers are possible. Serialization, remote transport and UI controls
belong to the host. Pausing a viewer can detach its probe and resuming can attach a new
one; this does not pause the application or guarantee replay of missed data.

Input/link observation and actual Windows Service verification remain pending.
Discovering input addresses does not imply support for observing inputs. No remote
observation endpoint or UI is included in these APIs.

## Normal hosts and Windows Services

Use the same Generic Host and `AddFluxFlow()` for both. A Windows Service executable
adds the standard `Microsoft.Extensions.Hosting.WindowsServices` package in its host
project; Core and Application do not depend on it.

```csharp
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddWindowsService(options => options.ServiceName = "MyWorkflowService");
// Register component/resource setup and the application's configuration here.
builder.Services.AddFluxFlow();
await builder.Build().RunAsync();
```

`AddWindowsService` activates WindowsServiceLifetime only when launched by Windows
Service Control Manager. Running that executable directly uses the ordinary console
lifetime; it does not prove SCM compatibility. Keep expression/data file base directories
explicit and consistent with the host content root, rather than relying on a service's
working directory. [Microsoft's hosting adapter](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.WindowsServices/src/WindowsServiceLifetimeHostBuilderExtensions.cs)
owns this selection; FluxFlow does not introduce another host lifetime.

Hosts resolve existing addresses and call `ObserveOutput` after startup as usual.
Probe attachment does not rebuild the graph, and a slow/disconnected reader does not
participate in application shutdown. A remote UI still needs a host-owned transport;
the smoke check exercises in-process observation, not remote access.

Run `eng/verify-hosting.ps1` to publish and exercise the same smoke host on .NET 8 and 10
in normal and Windows-adapter console modes. It checks automatic source startup with
links already attached, business delivery, live probes and event addresses, bounded
sample loss, shutdown with accepted work pending, and DI disposal exactly once.
The separate Generic Host tests also cover startup and runtime failures.

The [CI workflow](../.github/workflows/ci.yml) runs this existing script without
`-WindowsService` on its Ubuntu runner using PowerShell. Both modes use ConsoleLifetime;
the Windows-adapter mode checks console fallback only. The command requires no elevation,
creates no Windows services, and prints the temporary directory containing JSON evidence.
Local verification has passed both console modes on .NET 8 and 10; actual SCM
verification remains pending.

When that pending check is scheduled, run `eng/verify-hosting.ps1 -WindowsService`
from an elevated Windows PowerShell session
to additionally create a uniquely named temporary manual-start service for each target,
start it through SCM, attach/detach probes in the running service, and issue a real SCM
Stop command. The script attempts to stop/delete only the services it created in `finally`.
It retains published files and JSON evidence in a fresh temporary directory. Service mode
refuses to pass when executed as a console process. Evidence includes the actual lifetime,
SCM detection result, delivered values, dropped probe samples and component disposal counts.

SCM verification requires administrator access and is opt-in. Console fallback success
must be reported separately from actual Windows Service verification. Host/dependency logs
use their existing logging providers; verification status/failure files are separate.

## Ownership And Cancellation

The host owns one native DI provider. Resource registration adds factories before
provider construction. Components own their native block cleanup; DI owns components.
Application resources remain shared root singletons across workflow stop/restart and
are disposed with the host provider, not by workflow stop. Workflow-local resources
belong to the run's scope; externally supplied resource instances remain caller-owned.
The gateway tests verify that another workflow keeps using the same HTTP client while
one stops and restarts. Ownership alone does not guarantee operational isolation:
stopping an MQTT connection controller disconnects its shared client. Keep that controller
in a long-lived workflow or use a workflow-local resource when independent lifetimes are needed.

Cancellation can stop waiting or construction between factories. It is cooperative,
not a forced interruption of a constructor. Failed composition stops/drains partial
construction; the host must still dispose its provider.

## Diagnostics

Component Events carry address-stamped FlowMessage observations. Processing errors use
FlowError and preserve original exceptions through diagnostics. Application Completion
reports fatal graph/lifecycle failures. Automatic error collection and OpenTelemetry/SSE
integration remain separate work; no replacement revision event stream is introduced.

## Component identity

Application registration attaches each component's canonical address when its scoped instance
is created. No component changes or exporter SDK are required. Existing native .NET
diagnostics expose that identity as follows:

| Observation | Component identity |
| --- | --- |
| Live output probe | `probe.Address.ComponentAddress.Value` (the full port is `probe.Address`) |
| Component event | `message.Headers[FlowEventHeaders.Source]` |
| Processing, control and aggregate Activity | `flow.component.address` tag |
| Runtime processing and exception metrics | `flow.component.address` tag |
| Processing/lifecycle exception diagnostic | `ComponentAddress` property |

`ActivitySource` and `Meter` remain named `FluxFlow.Core`. Hosts continue using ordinary
`ActivityListener`, `MeterListener`, `DiagnosticListener` or their exporter integrations.
The tag name is available as `FlowDiagnostics.ComponentAddressTag`. Component type tags
remain. Component tags are available to `ActivityListener.Sample` through its creation
options, so hosts can select traces by address, and on fully sampled activities before
`ActivityStarted`. Propagation-only activities retain no component tags. Metrics and failure diagnostics
do not depend on tracing being enabled. Source startup has no processing span unless the
component explicitly starts one; attaching identity does not invent lifecycle activities.

Address identifies the processing instance, not the message origin. Message IDs, causation,
trace context and event headers keep their existing semantics. The association does not
add address headers to business messages or replace original exceptions/log records.
Request spans and downstream dependency spans retain their existing parent trace relationships;
the new tag is emitted by the component processing/control instrumentation.

Metric dimensions use the configured component address, never per-message IDs. Hosts with
many component addresses can aggregate or exclude that dimension in their telemetry setup.
Across separate application runs or processes, the host's resource attributes identify the
application/service instance; no run registry or automatically generated run ID is added.

Direct Core users may attach a diagnostic string once through
`FlowDiagnostics.AttachComponentAddress(component, address)` before execution. A second
attachment rejects the same instance. Core treats the value as opaque and stores it weakly;
unattached components retain type-only telemetry and null diagnostic `ComponentAddress`.

## Component events

`FlowEvent` is a `FlowValue`. Publishing and reading a typed event keep the same value
instance; `Json` is materialized once, on demand. Core does not depend on an exporter.

```csharp
var observation = new FlowEvent
{
    Name = "order.validated",
    Kind = FlowEventKind.Domain,
    Message = "Order validation completed.",
    Attributes = new Dictionary<string, object?> { ["orderId"] = "A-100" }
};
EmitEvent(observation.ToMessage(input.CorrelationId)); // inside a component
```

Correlation, trace and message IDs belong to the `FlowMessage` envelope. Event time
belongs to the value and defaults to its construction time; `ToMessage` uses that time
for the envelope too. `message.ToFlowEvent()` returns the existing typed value or reads
a JSON event received through serialization. Read correlation from `message.CorrelationId`.
Event JSON uses the declared names (`Name`, `Kind`, `Attributes`, and so on), while
attribute keys such as `orderId` keep their original spelling. See the
[JSON data contract](20-flow-data-contracts.md#stable-json-shape) when migrating stored events or expressions.

Attribute and dimension dictionaries are snapshotted during initialization. Immutable
scalars and `FlowValue` instances stay typed; arbitrary objects and collections become
owned `JsonElement` snapshots. Measurements are immutable finite numbers. This permits
lazy event JSON without retaining mutable caller data. Raw JSON projections expose
nested objects/arrays as `JsonElement`, not mutable dictionaries/arrays.

Attributes describe the event, dimensions supply grouping labels, measurements carry
numeric observations, and details hold a typed structured value. These remain separate
for existing consumers; none automatically creates a log or metric. Type/kind/level
headers remain routing conveniences. Application adds source/workflow/component headers
without replacing the value. A consumer can link Events to a logger, file writer,
dashboard or telemetry adapter; the component does not select that destination.

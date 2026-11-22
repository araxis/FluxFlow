# Getting started

The ordinary FluxFlow path uses native .NET dependency injection:

1. Add FluxFlow.Configuration and any expression-engine or custom-component packages
   you need. Configuration includes Application, Core and the bundled component dependencies.
2. Register component configuration readers, then call AddFluxFlowApplication.
3. Register services.AddFluxFlow() before building the host.
4. Build and run the host; normal host startup and shutdown manage the application.

Ordinary hosting composes from the host provider, links native blocks and starts sources.
Normal shutdown drains work and disposes workflow DI scopes; host disposal releases
shared resources. See [hosting](05-hosting-and-observability.md) for stop/restart and failure behavior.

## Serial text samples

- [SerialText-to-HTTP telemetry](../Code/samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/README.md#serial-text-variant):
  select `SerialTelemetry` to map received lines into HTTP requests.
- [HTTP-to-serial commands](../Code/samples/Hosting/FluxFlow.Hosting.Http.Sample/README.md#http-to-serial-commands):
  select `SerialCommands` to accept commands through HTTP. A 202 response confirms
  admission, not a completed serial write or device execution.

These are optional sections in the existing hosts. See [serial configuration](../Code/src/Plugins/FluxFlow.Plugins.Serial/Runtime/Configuration/README.md)
for port settings and [SerialText](../Code/src/Plugins/FluxFlow.Plugins.Serial/Runtime/README.md)
for framing, lifecycle and failure behavior. Automated checks use simulated I/O;
hardware and driver checks remain pending. Other examples are in the [sample index](README.md#start-here).

## Configuration example

The JSON root represents the application. Workflows contain flat component sections;
Resources is optional. This clock triggers the sample Source to emit its next value.

```json
{
  "workflows": {
    "main": {
      "clock": { "type": "scheduler", "interval": "00:00:01", "output": "samples.Input" },
      "samples": { "type": "source", "values": "[1,2,3]", "output": "sink.Input" },
      "sink": { "type": "my_sink" }
    }
  }
}
```

Register my_sink as shown below to process those values. Source advances one item per trigger and does not
repeat implicitly. Values preserves JSON types through IConfiguration.

```csharp
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;

services.AddScheduler().AddSource();
services.AddKeyedSingleton<ComponentRegistration>("my_sink", (registrations, section) =>
{
    var address = section.GetComponentAddress();
    registrations.AddComponentFactory(address, _ => new MySink())
        .AddInputPort<MySink>(address, "Input", node => node.Input);
});
services.AddFluxFlowApplication(configuration);
services.AddFluxFlow();
```

MySink is your component, exposing a native Input block and implementing IFlowNode.
See the [Configuration README](../Code/src/FluxFlow.Configuration/README.md) for complete,
current namespaces and configuration examples. For explicit lifecycle control instead
of hosted startup, build the provider, call `BuildApplicationAsync` and `StartAsync`,
then wait for your caller's shutdown signal before calling `StopAsync`. Dispose the
application before the provider. See the [existing hosting guide](05-hosting-and-observability.md).

Do not combine explicit startup with hosted startup. Component factories are synchronous
and lazy; Build resolves components in their workflow DI scopes. Repeated Build on the same
provider returns the same application. Failed Build stops constructed nodes; the host
still disposes the provider. DI owns disposal and follows standard stop-on-first-error behavior.

## Direct registration

```csharp
services.AddComponentFactory(ComponentAddress.Parse("main.worker"), provider => new MyWorker())
    .AddInputPort<MyWorker>(ComponentAddress.Parse("main.worker"), "Input", node => node.Input)
    .AddOutputPort<MyWorker>(ComponentAddress.Parse("main.worker"), "Output", node => node.Output);
services.AddSingleton<LinkFactory>(ports =>
    ports.Link(PortAddress.Parse("main.worker.Output"), PortAddress.Parse("other.sink.Input"), message => !message.IsError));
```

MyWorker and the sink are application-defined components; register both before Build.
Ports use native Dataflow blocks, addressed through keyed DI. They do not implement
IDisposable/IAsyncDisposable; the component owns cleanup. Components do not register
their own names or ports. There is no Workflow.Add or runtime GetInput/Send API.

Configuration changes require rebuilding/restarting the host; hot reload remains
deferred. See [runtime hosting](../Code/src/FluxFlow.Application/README.md).

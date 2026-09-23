> Historical API material: live revisions and the update host are removed from the minimum framework. See [current hosting](05-hosting-and-observability.md); old snippets below are not current setup instructions.

# Getting started

The ordinary FluxFlow path uses native .NET dependency injection:

1. Add FluxFlow.Core, FluxFlow.Configuration and the component packages you need.
2. Register component configuration readers, then call AddFluxFlowApplicationAsync.
3. Build the host/provider once.
4. Use services.AddFluxFlow() for normal host startup and shutdown, or explicitly Build/Start.

Ordinary hosting composes from the host provider, links native blocks and starts sources.
Shutdown drains work; the host provider owns disposal. No second provider is created.

## Configuration example

The JSON root represents the application. Workflows contain flat component definitions;
Resources is optional. This clock triggers the sample Source to emit its next value.

```json
{
  "workflows": {
    "main": {
      "clock": { "type": "scheduler", "interval": "00:00:01", "output": "samples.Input" },
      "samples": { "type": "source", "valuesJson": "[1,2,3]", "output": "sink.Input" },
      "sink": { "type": "my_sink" }
    }
  }
}
```

Register my_sink as shown below to process those values. Source advances one item per trigger and does not
repeat implicitly. ValuesJson preserves JSON types through IConfiguration.

```csharp
using FluxFlow.Configuration;
using FluxFlow.Configuration.Scheduling;
using FluxFlow.Configuration.Sources;
using FluxFlow.Core.Application;
using FluxFlow.Core.Runtime.Hosting;

services.AddScheduler().AddSource();
services.AddKeyedSingleton<ComponentRegistration>("my_sink", (registrations, section) =>
{
    var address = section.GetComponentAddress();
    registrations.AddComponentFactory(address, _ => new MySink())
        .AddInputPort<MySink>(address, "Input", node => node.Input);
});
await services.AddFluxFlowApplicationAsync(configuration);
services.AddFluxFlow();
```

MySink is your component, exposing a native Input block and implementing IFlowNode.
See the [Configuration README](../src/FluxFlow.Configuration/README.md) for complete,
current namespaces and configuration examples. For explicit lifecycle instead of hosting:

```csharp
await using var provider = services.BuildServiceProvider();
await using var application = await provider.BuildApplicationAsync();
await application.StartAsync();
// The host waits while sources and connected components process messages.
await application.StopAsync();
```

Do not combine explicit startup with hosted startup. Component factories are synchronous
and lazy; Build resolves one instance per keyed address. Repeated Build on the same
provider returns the same application. Failed Build stops constructed nodes; the host
still disposes the provider. DI owns disposal and follows standard stop-on-first-error behavior.

## Direct registration

```csharp
services.AddComponentFactory("main.worker", provider => new MyWorker())
    .AddInputPort<MyWorker>("main.worker", "Input", node => node.Input)
    .AddOutputPort<MyWorker>("main.worker", "Output", node => node.Output);
services.AddSingleton<LinkFactory>(ports =>
    ports.Link("main.worker.Output", "other.sink.Input", message => !message.IsError));
```

MyWorker and the sink are application-defined components; register both before Build.
Ports use native Dataflow blocks, addressed through keyed DI. They do not implement
IDisposable/IAsyncDisposable; the component owns cleanup. Components do not register
their own names or ports. There is no Workflow.Add or runtime GetInput/Send API.

Optional revision hosting uses AddFluxFlowUpdates and retains candidate isolation,
stable ingress/egress and rollback. It is a separate opt-in from ordinary composition.
See [runtime hosting](../src/FluxFlow.Core/Runtime/README.md).

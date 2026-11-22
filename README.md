# FluxFlow — minimum framework

This branch contains **FluxFlow.Core** and optional modules
**FluxFlow.Components.Routing** (Gate/Router), **FluxFlow.Components.Mapping** (Mapper),
**FluxFlow.Components.Source** (sample Source), **FluxFlow.Components.Scheduler** (scheduled signals), and
**FluxFlow.Components.Resilience** (request/reply Retry using Microsoft.Extensions.Resilience, backed by Polly).
**FluxFlow.Components.Reducer** maintains state and emits changes;
**FluxFlow.Components.RateLimit** holds native .NET permits across request/reply.
**FluxFlow.Components.Batch** collects messages by count or elapsed collection time.
**FluxFlow.Components.ChangeFilter** forwards changed values; **FluxFlow.Components.Deduplicate** suppresses previously seen keys within bounded retention.
Core has no dependency on modules.

**FluxFlow.Components.Logging** processes structured log requests, individually or in arrays,
using application logging or a dedicated OTLP destination. [Setup](Code/src/Plugins/FluxFlow.Plugins.Logging/Registration/README.md).

**FluxFlow.Components.Http** sends HTTP requests over shared native clients, retaining
response status, headers and body. [Configuration setup](Code/src/Plugins/FluxFlow.Plugins.Http/README.md)

**FluxFlow.Components.Mqtt** provides MQTT connection control, publishing and subscriptions
over a shared client resource. [Configuration setup](Code/src/Plugins/FluxFlow.Plugins.Mqtt/Runtime/Configuration/README.md)

**FluxFlow.Components.DataGeneration** combines JSONata expressions and Bogus functions
to generate one payload per input. [Configuration setup](Code/src/Plugins/FluxFlow.Plugins.Sources/Registration/DataGenerator.md)
is part of the Sources plugin alongside Scheduler.

**FluxFlow.Components.Ros2** provides typed ROS 2 publishers and subscribers using
Rcl.NET. The ROS 2 plugin supplies the node and component registrations. See the [ROS 2 setup guide](Code/src/Plugins/FluxFlow.Plugins.Ros2/README.md).

**FluxFlow.Application** owns application/workflow composition, typed addressing, native
DI registration, links and host startup/shutdown. Core and components do not depend on it.

**FluxFlow.Configuration** is the optional setup package connecting configuration
to component registrations supplied by installed plugins.
Register configuration with `services.AddFluxFlowApplication(configuration)` before building the provider;
`Workflows` and `Resources` belong at that root, without an `Application` wrapper.
A host may explicitly pass a selected section instead. Resource registrars describe
application/workflow resources through native DI factories. The host provider
constructs and owns those resources and components.

The configuration root represents the application. Each workflow owns its components
while native ports are registered in DI by unique address; input messages and source
components drive processing. `await provider.BuildApplicationAsync()` resolves the
registered singleton application, composes components and prepares link declarations.
Ports are registered before the provider is built. StartAsync connects prepared routes and starts sources.
ComponentFactory receives only IServiceProvider and returns IFlowNode. Workflows have no
Start/Stop lifecycle. Application stops and drains processing; DI disposes components. Each
output owns its links. There is no separate topology model, graph snapshot or
public executor. Use `await using var app = await provider.BuildApplicationAsync()`;
configuration registration and provider construction create no components. Repeated Build
returns the same application. Output capacity belongs to native component blocks.
Build requires a registered application; it never creates
an unowned fallback instance. Stop/dispose the application before disposing the provider.

Configuration traverses sections and registers component/resource factories, native
port selectors and link delegates in DI. Intermediate document models, serializers,
document compilers and the separate document construction path are removed.
Hot reload and revision replacement
are absent from this minimum framework; a future optional package may supply them.

Core contains message/value/error contracts, Dataflow processing, native broadcast
publication, component-owned request/reply and instrumentation. Application owns
workflow composition, addressable ports, component registration and host lifecycle.

Optional C# and JSONata expression packages live under `Code/src/Expressions`.
Ordinary hosting lives in Application; revision support is removed. Persistence providers and standalone
OpenTelemetry/ASP.NET Core adapters are not included in this minimum branch.

## Structure

Open [Code/FluxFlow.sln](Code/FluxFlow.sln) in Visual Studio. This is the single
solution for the runtime, components, hosts, Studio and tests.

```text
FluxFlow/
  Code/
    FluxFlow.sln
    src/
      FluxFlow.Core/
      FluxFlow.Application/
      FluxFlow.Configuration/
      Components/
      Expressions/
      Hosting/
      Authoring/
        FluxFlow.Authoring/
        FluxFlow.Conversations/
        FluxFlow.Conversations.Local/
      Studio/
        FluxFlow.Studio.UI/
        FluxFlow.DesignerApp/
        FluxFlow.StudioWebHost/
    tests/Unit/
    samples/Hosting/
    benchmarks/
    eng/
  docs/
  assets/
```

Visual Studio solution folders mirror the physical folders below `Code`.
Runtime packages remain independent of Studio; Studio uses local project
references. Working notes and experimental work are kept outside the repository.

See [Studio setup](Code/src/Studio/README.md) to run the designer.
See [configuration authoring](Code/src/Authoring/README.md) for reusable editing,
discovery and conversation APIs usable without Studio.

## Build and verify

```powershell
cd Code
dotnet build FluxFlow.sln -c Release
./eng/test.ps1 -Suite Unit -Configuration Release -NoBuild
./eng/verify-core-consumer.ps1
./eng/verify-routing-consumer.ps1
./eng/verify-mapping-consumer.ps1
./eng/verify-source-consumer.ps1
./eng/verify-scheduler-consumer.ps1
./eng/verify-reducer-consumer.ps1
./eng/verify-ratelimit-consumer.ps1
./eng/verify-batch-consumer.ps1
./eng/verify-changefilter-consumer.ps1
./eng/verify-deduplicate-consumer.ps1
```

The consumer verification packs Core, restores it into an isolated package-only
application and runs direct and JSON-defined message pipelines on .NET 8 and
.NET 10, including concurrent messages and continued processing after an error.
No database, broker or external telemetry service is required.

## Current refactoring checkpoint

ComponentActivationContext and ComponentInstance are removed. Ordinary and revision builds
share native DI factories/port selectors; workflows retain actual nodes. See the
[registration guide](docs/40-component-registration.md) for the breaking API migration.

## Earlier minimum-framework checkpoint

The first redesigned component is Gate. Review component implementation and its
boundary with application composition before adding further modules. Nothing else
is restored automatically. See the
[minimum-framework report](docs/reviews/2026-09-15-minimum-framework.md) and
[Core guide](Code/src/FluxFlow.Core/README.md).

Historical component/module guides describe earlier development checkpoints;
the current package inventory is `Code/eng/packages.json`.

Application-level RPC has been removed: no MakeFunction, CallFunction or application
request/reply API. Components such as Retry and RateLimit own their exchanges via
FlowRequestReply and explicit Request/Reply links. Configuration has no built-in
function component. Link conditions filter independently; messages matching no link,
including registered terminal outputs with no links, are consumed without routing faults.

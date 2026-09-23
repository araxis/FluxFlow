# FluxFlow — minimum framework

This branch contains **FluxFlow.Core** and optional modules
**FluxFlow.Components.Routing** (Gate/Router), **FluxFlow.Components.Mapping** (Mapper),
**FluxFlow.Components.Source** (sample Source), **FluxFlow.Components.Scheduler** (scheduled signals), and
**FluxFlow.Components.Resilience** (request/reply Retry using Polly).
**FluxFlow.Components.Reducer** maintains state and emits changes;
**FluxFlow.Components.RateLimit** holds native .NET permits across request/reply.
**FluxFlow.Components.Batch** collects messages by count or elapsed collection time.
**FluxFlow.Components.ChangeFilter** forwards changed values; **FluxFlow.Components.Deduplicate** suppresses previously seen keys within bounded retention.
Core has no dependency on modules.

**FluxFlow.Configuration** is the optional composition package connecting configuration
to components, with Scheduler, Gate, Router, Source, Mapper and Batch registrations. Components do not depend on it.
Register configuration with `await services.AddFluxFlowApplicationAsync(configuration)` before building the provider;
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
returns the same application and preserves its configured link capacity. To configure it,
register `services.AddSingleton(_ => new FlowApplication(linkCapacity: 256))` before
configuration registration. Build requires a registered application; it never creates
an unowned fallback instance. Stop/dispose the application before disposing the provider.

Configuration traverses sections and registers component/resource factories, native
port selectors and link delegates in DI. Intermediate document models, serializers,
document compilers and the separate document construction path are removed.
Hot reload and revision replacement
are absent from this minimum framework; a future optional package may supply them.

Core contains application/workflow graphs, addressable ports, message/value/error
contracts, Dataflow processing, bounded delivery, component-owned request/reply,
component registration and ordinary host startup/shutdown. Revision APIs are removed.

Optional C# and JSONata expression packages live under `src/Expressions`.
Ordinary hosting lives in Core; revision support is removed. Persistence providers and standalone
OpenTelemetry/ASP.NET Core adapters are not included in this minimum branch.

## Structure

```text
src/
  FluxFlow.Configuration/        # configuration DTOs and external component registrations
  Expressions/                  # optional C# and JSONata engines
  Components/
    FluxFlow.Components.Routing/  # Gate/Router; each module is its own project/package
    FluxFlow.Components.Mapping/  # Mapper; externally supplied mapping function
    FluxFlow.Components.Source/   # one sample value per trigger
    FluxFlow.Components.Scheduler/ # scheduled signals; owns Cronos dependency
    FluxFlow.Components.Resilience/ # Retry; Input/Request/Reply/Output blocks
    FluxFlow.Components.Reducer/   # immutable state reduction and change detection
    FluxFlow.Components.RateLimit/ # native .NET limiting across request/reply
    FluxFlow.Components.Batch/     # size/deadline batching preserving message envelopes
    FluxFlow.Components.ChangeFilter/ # compare with last forwarded key
    FluxFlow.Components.Deduplicate/  # bounded key history with expiry
  FluxFlow.Core/
    Application/       # application composition, workflows and execution lifetime
    Addressing/        # address interpretation
    Messaging/         # envelope, payload, errors and identifiers
    Nodes/             # processing building blocks
    Execution/         # routing and shared lifetime
    Coordination/      # pending request/reply exchanges
    Configuration/     # resource registration and setup errors
    Mapping/           # mapping/condition contracts
tests/
  Unit/                # Core, nodes, coordination and release checks
  Support/             # test fixtures
benchmarks/            # manual Core benchmarks
docs/                  # current scope and historical design records
eng/                   # build, package and consumer verification
```

Visual Studio mirrors `src`, `src/Components`, `tests/Unit`, `tests/Support` and
`benchmarks`. Each component module is one project beneath Components. There are
**30 solution projects**: fourteen production, fifteen unit-test and one
benchmark project.

## Build and verify

```powershell
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
[Core guide](src/FluxFlow.Core/README.md).

The previous full implementation is preserved on local `main` at `6a659218`.
The newer uncommitted nine-package state has a verified local recovery archive at
`artifacts/minimum-core-20260915/before-minimum.zip`. This ignored archive must be
copied separately for recovery on another machine. Historical component/module
guides describe capabilities absent from this baseline; the current package
inventory is `eng/packages.json`.

Application-level RPC has been removed: no MakeFunction, CallFunction or application
request/reply API. Components such as Retry and RateLimit own their exchanges via
FlowRequestReply and explicit Request/Reply links. Configuration has no built-in
function component. Terminal outputs require a sink, fallback or explicit discard.

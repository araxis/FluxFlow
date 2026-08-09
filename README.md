# FluxFlow

FluxFlow is a standalone-node-first workflow toolkit for .NET.

The default architecture is:

1. Build reusable nodes over `FluxFlow.Nodes`.
2. Declare complete typed component contracts with `FluxFlow.Composition`, or
   register those contracts explicitly for a JSON/dynamic host.
3. Either load the canonical JSON application document with exactly `Resources`
   and `Workflows`, or build a typed application directly in compiled C#.
4. Activate it through `FluxFlow.Engine` with one `AddFluxFlow(...)` registration
   when hosted lifecycle or addressable runtime ports are needed.
5. Let code-first resource declarations carry their package registrar while
   keeping concrete clients, stores, secrets, and protocol adapters explicitly
   owned by the revision or host.

`FluxFlow.Engine` remains optional for component packages. Canonical hosts use
`FluxFlowApplication` for revisions, compiled links, stable direct ports, and
system signals without moving external resource ownership into the engine.

## Main Packages

| Package | Purpose |
|---------|---------|
| `FluxFlow.Nodes` | Minimal standalone node kit plus the transport-neutral `FluxFlow.Data` namespace containing exact-byte `FlowContent` and `FlowError`. |
| `FluxFlow.Coordination` | Generic bounded pending exchanges with deterministic timeout, cancellation, and exact-once settlement. |
| `FluxFlow.Resilience` | Transport-neutral retry policy, schedules, state transitions, jitter, and direct-call execution. |
| `FluxFlow.Composition` | Canonical application definitions, addresses, links, component registrations, events, and processing profiles. |
| `FluxFlow.Engine` | Optional canonical application host with transactional revisions, stable direct ports, and system signals. |
| `FluxFlow.Engine.HealthChecks` | Optional standard .NET readiness check over the existing Engine lifecycle and active revision. |
| `FluxFlow.Engine.DurableInput` | Optional provider-neutral durable inbox with Engine-accepted or explicit workflow-completion acknowledgement, dead-letter operations, payload-free status, and explicit bounded terminal retention. |
| `FluxFlow.Engine.DurableInput.SqlFile` | Production SQLite single-file store with exact lease renewal, generation-protected dead-letter replay, read-only status, and transactional retention. |
| `FluxFlow.Engine.DurableInput.TSql` | Production opt-in networked T-SQL inbox with shared atomic leasing, exact renewal, dead-letter inspection/replay, read-only status, and transactional retention. |
| `FluxFlow.Engine.DurableOutput` | Optional provider-neutral capture, serial renewable leased at-least-once delivery, bounded attempts, dead-letter operations, payload-free status, and explicit bounded terminal retention. |
| `FluxFlow.Engine.DurableOutput.SqlFile` | Production SQLite single-file capture, exact lease renewal, delivery state, dead-letter operations, read-only status, and transactional retention for local hosts. |
| `FluxFlow.Engine.DurableOutput.TSql` | Production opt-in networked T-SQL capture, shared delivery state with exact renewal, dead-letter operations, read-only status, and transactional retention. |

Component packages should expose normal standalone nodes first. Composition
factory registration, design metadata, and host-specific DI helpers are optional
adapters around those nodes. Engine-specific integration is separate from the
normal component package shape.

## Standalone Node Example

```csharp
public sealed class UppercaseNode : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        Emit(message.With(message.Value.ToUpperInvariant()));
        return Task.CompletedTask;
    }
}
```

Nodes are plain Dataflow processors. Construct them, link their ports, send
`FlowMessage<T>` values, and await completion.

## Composition Example

`FluxFlow.Composition` adds strict definitions, complete typed component
contracts, validation, and link compilation around standalone nodes. A
contract declares runtime behavior and typed authoring once:

```csharp
public static ComponentContract<UppercaseHandle> Uppercase { get; } =
    ComponentContract.Create(
        "sample.uppercase",
        component =>
        {
            component
                .UseFactory(static _ => new UppercaseNode())
                .HasInput("Input", static node => node.Input)
                .HasOutput("Output", static node => node.Output)
                .HasEvents("Events", static node => node.Events);
        },
        static handle => new UppercaseHandle(handle));
```

Developers can instead author and host the graph directly in typed C#. This is
an independent compiled-code path, not a JSON generator:

```csharp
var definitionBuilder = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var main)
    .AddWorkflow("audit", out var audit);

main
    .AddComponent("source", OrderComponents.Source, out var source)
    .AddComponent("review", OrderComponents.Review, out var review)
    .AddComponent("priority", OrderComponents.Sink, out var priority);

audit.AddComponent("events", OrderComponents.EventCollector, out var events);

source.Output.ConnectTo(review.Input);
review.Output.ConnectTo(priority.Input, when: static order => order.Priority);
review.Events.ConnectTo(events.Input);

var definition = definitionBuilder.Build();
services.AddFluxFlow(definition);
```

The built definition carries the exact component descriptors and application
resource contracts it uses, so normal code-first hosting needs no second
component or resource-family registration. Component-specific
builders own settings, and named handles expose `Input`, `Output`, `Events`, and
other domain ports. Direct `ConnectTo` supports same-owner cross-workflow links
and returns the output handle for fan-out. Workflow `Connect` stays local;
application `Connect` is the explicit cross-workflow form. Typed predicates are
synchronous, revision-owned C# delegates and do not require an expression
engine. See [Typed Code-First Application Authoring](docs/39-typed-code-first-authoring.md)
and [Unified Component Contracts](docs/40-unified-component-contracts.md).

Those same handles continue across the runtime boundary:

```csharp
var result = application.Ports.ReceiveAsync(priority.Output);
await application.Ports.SendAsync(review.Input, FlowMessage.Create(order));
var received = await result;
```

Typed overloads are also available for observation, request/reply, durable
input enqueue, durable output capture, and explicit keyed resource binding.
They delegate to the same canonical-address implementation and keep
`FlowMessage<T>` metadata explicit.

Portable JSON remains independent and contains no executable delegates. A JSON
host registers the required contracts explicitly:

```csharp
var definition = ApplicationDefinitionJson.Deserialize(json);
services
    .AddFluxFlow(definition)
    .AddComponent(SampleComponents.Uppercase);
```

There is no reflection, assembly scanning, alias rewrite, or engine dependency
in this path. Component and resource type names must be canonical before load.
Repeated equivalent family registrations are idempotent. A different
registration for an existing component type fails immediately instead of
silently replacing the first registration.

`FluxFlow.Engine` can own the lifecycle around the same model:

```csharp
services
    .AddFluxFlow(configuration)
    .AddSources()
    .AddMapping();

var application = provider.GetRequiredService<FluxFlowApplication>();
await application.StartAsync();
```

Hosts that use standard .NET health checks can opt into one readiness signal
without changing Engine or starting another worker:

```csharp
using FluxFlow.Engine.HealthChecks;

services.AddHealthChecks()
    .AddFluxFlowApplication();
```

The check is named `fluxflow.application` and tagged `fluxflow` plus `ready`.
It is healthy when an active revision is available, degraded when a rejected
update leaves the previous revision serving, and unhealthy when FluxFlow is
missing, inactive, or stopped. See
[Application Health Readiness](docs/42-application-health-readiness.md).

Adapter packages still own concrete resources and register them in DI, usually
as named keyed services. A compiled-C# resource contract carries only its
portable type/options, typed handle, and explicit registrar. JSON cannot and
does not serialize that executable behavior, so configuration hosts continue
to register the relevant package family explicitly.

Expected failures remain ordinary value-or-error `FlowMessage<T>` values on
`Output`; component contracts do not expose a universal Errors port.
Every built-in component explicitly declares its traced
`Workflow.Component.Events` port. Custom components choose the event-port name
with `HasEvents(...)`, and components that omit it have no implicit event port;
unrecoverable faults remain on `Completion`. Canonical JSON does not expose
Dataflow capacities or parallelism settings. An optional `processing.profile`
resource provides semantic `Mode`, `Order`, and `Buffer` settings when defaults
are not enough.

`FluxFlow.Components.Resilience` provides a standalone retry-controlled
operation node, and its optional Composition adapter registers `flow.retry`.
Ack, Nak, and Cancel are payload-independent signal inputs. Retry attempts keep
one workflow `TraceId`; an internal attempt key prevents late feedback from an
older attempt completing a newer one.

## Samples

Run the canonical application composition sample:

```sh
dotnet run --project samples/FluxFlow.CompositionSample/FluxFlow.CompositionSample.csproj
```

Run the MQTT composition sample with in-memory adapter resources:

```sh
dotnet run --project samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj
```

Run the HTTP trigger sample:

```sh
dotnet run --project samples/FluxFlow.HttpTriggerSample/FluxFlow.HttpTriggerSample.csproj
```

Run the advanced canonical host sample for workspace projection, conditional
links, and component Events fan-in:

```sh
dotnet run --project samples/FluxFlow.SampleApp/FluxFlow.SampleApp.csproj
```

Use `samples/FluxFlow.ComponentPackageTemplate` as the copyable shape for new
component packages.

## Building

```sh
dotnet build FluxFlow.sln --configuration Release
dotnet test FluxFlow.sln --configuration Release --no-build
```

Build and test with the same configuration. The Release verification suite runs
sample applications from those prebuilt outputs so a missing artifact fails
visibly instead of triggering an implicit restore or build.

For allocation and throughput investigation, use the permanent non-packable
benchmark suite. Start with a dry run; timing remains manual evidence and is
not a flaky CI threshold:

```sh
dotnet run --project benchmarks/FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj --configuration Release -- --filter "*" --job Dry --noOverwrite
```

See [Performance, Concurrency, And Lifetime Baseline](docs/43-performance-concurrency-lifetime-baseline.md)
for the measured cases, current machine-local baseline, and full-run commands.

## License

FluxFlow is licensed under the MIT License.

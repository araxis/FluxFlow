# FluxFlow.Engine

Canonical hosted runtime for complete FluxFlow applications. The package owns
definition loading, transactional revision replacement, stable addressable
ports, lifecycle state, application diagnostics, and system events.
`FluxFlow.Composition` owns the application model, component descriptors,
addresses, and link contracts consumed by this runtime.

Component packages remain Engine-independent. They expose standalone nodes and
optional Composition adapters; an application host chooses Engine when it needs
configuration-driven activation or direct port access.

## Registration

Register the application and component families explicitly in one service
collection. There is no assembly scanning or secondary runtime registration.

```csharp
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Engine;

services
    .AddFluxFlow(configuration)
    .AddMapping();

var application = provider.GetRequiredService<FluxFlowApplication>();
```

`AddFluxFlow(...)` supports an `IConfiguration` root or named section, a direct
`ApplicationDefinition`, a source instance, or a source type:

```csharp
services
    .AddFluxFlow(configuration, sectionName: "CustomFluxFlow")
    .AddMapping();
```

```csharp
services.AddFluxFlow<MyDefinitionSource>(options =>
{
    options.InitialRevisionId = "deployment-42";
    options.StartWithHost = true;
    options.StopWithHost = true;
    options.InputCapacity = 256;
    options.OutputCapacity = 512;
});
```

A compiled C# definition built from complete `ComponentContract` values carries
its own executable descriptors. Register it directly; do not repeat those
components in the service-registration chain:

```csharp
var definition = applicationBuilder.Build();

services.AddSingleton(orderStore);
services.AddFluxFlow(definition);
```

JSON and low-level string definitions intentionally carry no executable C#.
Register their required family extensions or individual complete contracts on
the host. Dynamic/plugin descriptors use the visibly advanced
`AddFluxFlowComponents().Advanced.AddDynamicComponent(...)` escape hatch.

The registered hosted service resolves the same singleton
`FluxFlowApplication` that direct callers resolve. Set `StartWithHost` to
`false` when the application will be started explicitly.

`InputCapacity` and `OutputCapacity` belong to the Engine's stable addressable
ports. Component DSL `BoundedCapacity` values and standalone node options remain
component-owned; registration does not overwrite them.

## Application Lifecycle

`FluxFlowApplication` is the sole owner of lifecycle state and revision
mutation:

```csharp
var started = await application.StartAsync();
var reloaded = await application.ReloadAsync("deployment-43");
var applied = await application.ApplyAsync(
    "deployment-44",
    nextDefinition);
await application.StopAsync();
```

`StartAsync` and `ReloadAsync` load through `IApplicationDefinitionSource`;
`ApplyAsync` accepts an already loaded complete definition. All three return
`ApplicationUpdateResult` with `Applied`, `Unchanged`, or `Rejected` status,
the requested revision ID, active and previous snapshots, and staged
diagnostics. Expected source, validation, preparation, or activation failures
are rejected results. Cancellation remains `OperationCanceledException`.

Lifecycle mutations share one synchronization boundary. A candidate is fully
prepared before it can replace the active revision. Failed candidates are
disposed and cannot damage the previous revision. A successful replacement
switches stable ports atomically, then drains and disposes the old revision.
Revision-owned providers and candidates are disposed exactly once.

`State`, `Current`, `CurrentDefinition`, and `LastUpdate` expose the current
host view. A rejected reload keeps the application `Running` while the previous
revision continues serving work and records a rejected `LastUpdate`; a later
successful update replaces that result. A failed initial start with no active
revision is `Degraded`. The result returned by a successful replacement reports its
`PreviousRevision`; the retained `LastUpdate` copy omits that retired snapshot
so application state does not keep executable definitions and captured
predicates alive. A stopped application cannot be restarted.

## Stable Ports

`ApplicationPorts` is a stable facade over the active runtime generation. A
compiled-C# caller should retain component handles and use their typed ports;
canonical strings and `ApplicationAddress` remain available for JSON,
operations, and dynamic selection:

```csharp
var receive = application.Ports.ReceiveAsync(
    finalResult.Output,
    TimeSpan.FromSeconds(10));

var send = await application.Ports.SendAsync(
    validateOrder.Input,
    FlowMessage.Create(order));

var reply = await application.Ports.SendAndReceiveAsync(
    validateOrder.Input,
    finalResult.Output,
    FlowMessage.Create(order),
    TimeSpan.FromSeconds(10));
```

`SendAsync` reports normal intake states such as accepted, full, unavailable,
or completed. `ReceiveAsync` is a broadcast tap and does not steal workflow
delivery. `ObserveAsync` uses a caller-selected bounded buffer.
`SendAndReceiveAsync` registers its waiter before sending and matches by
`TraceId`.

The `ApplicationPorts` object remains stable across revisions and resolves the
current runtime generation for each operation. `Metadata`, `CurrentRevision`,
`Status`, `Rejections`, `SystemEvents`, `Diagnostics`, and `Completion` expose
the current generation. Access before the first successful activation is
rejected.

Canonical system outputs remain:

- `System.Events.Output` with `FlowMessage<ApplicationSystemEvent>`.
- `System.Diagnostics.Output` with `FlowMessage<ApplicationDiagnostic>`.

System-event delivery is bounded and reliable for accepted events. Diagnostic
delivery is bounded and best effort; overflow rejects immediately while
accepted diagnostics remain ordered. Component `FlowError` values remain
ordinary workflow data and are not replaced by operational diagnostics.

Normal application ports intentionally remain in-process. Hosts that require
crash recovery before Engine accepts an input can add the separate
`FluxFlow.Engine.DurableInput` package and a host-owned `IDurableInputStore`.
That adapter preserves `MessageId` and provides leased at-least-once delivery;
it does not change Engine revisions, port capacities, or normal send semantics.
Local hosts can add `FluxFlow.Engine.DurableInput.SqlFile` for a production
SQLite provider without adding a dependency from Engine itself. Shared hosts
can instead add `FluxFlow.Engine.DurableInput.TSql` for a production networked
relational provider with atomic multi-host leasing. Capable providers may also
expose `IDurableInputDeadLetterStore` for bounded inspection and explicit
compare-and-set replay without changing Engine configuration.

Hosts that need selected application outputs persisted before live Engine
dispatch can add `FluxFlow.Engine.DurableOutput`. Engine resolves one optional
typed capture operation per output port and otherwise keeps the current fast
path. The adapter uses explicit output addresses and `JsonTypeInfo<T>` metadata;
it adds no reflection discovery, provider setting, or transport dependency.
`ReceiveAsync` and `ObserveAsync` remain live taps rather than persistence
contracts. Hosts may independently enable the adapter's one-at-a-time leased
delivery dispatcher with one `IDurableOutputDeliveryStore` and one host-owned
`IDurableOutputDeliveryHandler`. This is fixed-retry at-least-once delivery;
handlers own destination idempotency. Local hosts can add
`FluxFlow.Engine.DurableOutput.SqlFile` for atomic idempotent SQLite capture and
independently initialized delivery state, or
`FluxFlow.Engine.DurableOutput.TSql` for shared networked capture, leases,
dead-letter operations, and replay. Other backends implement capture and,
optionally, the narrow delivery capability without changing Engine or workflow
definitions.

## Resources And Ownership

Composition adapters implement `IApplicationResourceRegistrar` from
`FluxFlow.Composition`. Registrars receive a revision-owned service collection
and register keyed resources in deterministic order. For compiled C#, the
resource contract that authored the definition carries that registrar into the
candidate revision; JSON/configuration definitions intentionally carry none and
still require explicit package registration. Engine merges exact host and
definition registrar identities idempotently, builds isolated resource and
workflow providers, resolves one effective catalog from host and
definition-owned descriptors, and owns only revision-scoped services it
creates. During activation, revision-owned services and keyed resources take
precedence; ordinary host services remain available as an explicit fallback and
keep host ownership.

Runtime generations, provider snapshots, revision candidates, binders, leases,
and port builders are implementation details. Normal consumers construct and
control only `FluxFlowApplication` through DI.

## Optional Readiness Health Check

Hosts that use standard .NET health checks can reference the separate
`FluxFlow.Engine.HealthChecks` package and register one application readiness
check:

```csharp
using FluxFlow.Engine.HealthChecks;

services.AddHealthChecks()
    .AddFluxFlowApplication();
```

The adapter observes existing in-memory application state only. It adds no
Engine dependency in the reverse direction, worker, polling, storage access,
or endpoint. See `docs/42-application-health-readiness.md` for the exact status
and bounded-data contract.

## Public Surface

The primary host-level contracts are:

- `FluxFlowApplication` and `FluxFlowApplicationOptions`.
- `ApplicationState`, `ApplicationSnapshot`, and `ApplicationUpdateResult`.
- `ApplicationPorts` plus result and metadata contracts in
  `FluxFlow.Engine.Ports`.
- the optional `IApplicationOutputCaptureResolver` and
  `IApplicationOutputCapture<T>` extension seam used by durable-output adapters.
- `IApplicationDefinitionSource`,
  `ConfigurationApplicationDefinitionSource`, and
  `StaticApplicationDefinitionSource`.
- operational contracts in `FluxFlow.Engine.Signals`.

Retired Engine and Composition document shapes are rejected. Convert stored
documents outside the runtime before loading them, then persist the canonical
`Resources` / `Workflows` shape and canonical component type names.

See `docs/05-hosting-and-observability.md` for lifecycle details and
`docs/15-engine-compatibility.md` for the current boundary policy.

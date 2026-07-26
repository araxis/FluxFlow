# Hosting And Observability

The hosted coordination path is `FluxFlow.Composition.Hosting` over the
canonical flat `ApplicationDefinition`. It loads exactly `Resources` and
`Workflows`, delegates concrete candidate construction to an explicitly
registered factory, and serializes initial activation and later
complete-definition revisions. `FluxFlow.Engine.Hosting` supplies the standard
runtime assembler when the host wants executable nodes, compiled links, and
stable directly addressable ports.
`IApplicationRevisionHost` is the primary lifecycle, status, reload, and direct
complete-definition update surface.

## Register The Application

```csharp
services
    .AddFluxFlowApplication(configuration)
    .AddFluxFlowEngine()
    .AddMappingComponents()
    .AddHttpComponents()
    .AddApplicationResourceRegistrar<ApplicationResourceRegistrar>()
    .ConfigureFluxFlowApplication(
        options => options.InitialRevisionId = "deployment-42");
```

The configuration object may be the exact application root. Pass a section
name when the canonical document is nested under host settings:

```csharp
services
    .AddFluxFlowApplication(configuration, "FluxFlowApplication")
    .AddFluxFlowEngine()
    .AddMappingComponents()
    .AddHttpComponents()
    .AddApplicationResourceRegistrar<ApplicationResourceRegistrar>();
```

There is no assembly scanning. Family extensions register explicit
`ComponentDescriptor` instances; DI materializes one immutable
`ComponentCatalog`. An `IApplicationResourceRegistrar` may read the complete
canonical definition and add provider-owned or explicitly external services to
the candidate resource collection. The assembler prepares one resource snapshot,
one snapshot per workflow, component instances, compiled links, and one stable
port revision. Adapter packages still own concrete clients, stores, retry
behavior, credentials, and protocol lifetimes.

Hosts with a different activation model can register their
`IApplicationRevisionCandidateFactory` through
`AddApplicationRevisionCandidateFactory<TFactory>()` instead of
`AddFluxFlowEngine()`.

## Hosted Lifecycle

The registered hosted service applies the initial definition with the .NET host
and drains the active candidate at host stop. Resolve
`IApplicationRevisionHost` for status, reload, or direct complete-definition
updates:

```csharp
var host = services.GetRequiredService<IApplicationRevisionHost>();

var reload = await host.ReloadAsync("deployment-43");
if (reload.Error is not null)
    logger.LogError("{Code}: {Message}", reload.Error.Code, reload.Error.Message);

if (reload.Update?.Status == ApplicationRevisionUpdateStatus.Rejected)
{
    foreach (var failure in reload.Update.Failures)
        logger.LogWarning("{Code}: {Message}", failure.Error.Code, failure.Error.Message);
}
```

`ReloadAsync` loads another complete definition from the configured source.
`ApplyAsync` accepts an already loaded complete definition. Partial patches,
file watching, and remote configuration transport belong to the source layer.

## Direct Port Access

After the first revision activates, resolve `IApplicationRuntimeAccess` to send
to an input or receive, observe, or perform request/reply against an output by
canonical address:

```csharp
var access = provider.GetRequiredService<IApplicationRuntimeAccess>();
var ports = access.GetRequiredPorts();
var input = ApplicationAddress.WorkflowPort("Orders", "Validate", "Input");
var output = ApplicationAddress.WorkflowPort("Orders", "Validate", "Output");

var request = FlowMessage.Create(order);
var result = await ports.SendAndReceiveAsync<Order, ValidationResult>(
    input,
    output,
    request,
    TimeSpan.FromSeconds(10));
```

Direct output observation is broadcast and does not steal workflow delivery.
The first active definition fixes the external address, direction, kind, and
payload-type surface for that assembler instance. Later complete-definition
revisions replace resources, nodes, links, and port attachments atomically, but
a surface-changing revision is rejected and leaves the current revision active.

## Failure Isolation

A source-load failure returns `ApplicationRevisionLoadResult.Error` with stable
code `revision.source.load_failed`. The application host enters `Degraded`, but
the surrounding .NET host continues running. Caller cancellation still throws
`OperationCanceledException`.

Planning, preparation, and activation failures return a rejected revision
result. If an older revision is active, it remains active and the host remains
`Running`. A successful activation publishes one immutable current snapshot
before the old candidate drains. Drain and disposal failures are reported after
all cleanup is attempted and do not roll back the committed revision.

## Provider Snapshots

Compose service descriptors before building immutable snapshots:

```csharp
var registrations = new ServiceCollection();
registrations.AddFluxFlowResource<IMessageClient>(
    ApplicationAddress.Resource("Messaging", "Client1"),
    services => new MessageClient(
        services.GetRequiredService<ClientOptions>()));

await using var resources = new CompositionServiceProviderSnapshotBuilder()
    .AddServices(registrations)
    .Build(CompositionProviderBoundary.ResourceRevision, "resources-43");
```

Canonical address strings are keyed-service identities. Factory registrations
are provider-owned. `AddExternal...`, `BridgeExternal...`, and
`CreateExternalHost(...)` are explicit non-owning boundaries. Snapshots never
scan, merge, mutate, or fall back to another provider.

## System Streams

Engine-backed candidates connect lifecycle and revision events to
`System.Events.Output` and best-effort activity to
`System.Diagnostics.Output`. Both are ordinary stable output addresses and can
be observed directly or linked into workflows. Every canonical component also
exposes `Workflow.Component.Events` with traced
`ComponentEvent` data. Component events are not copied into
`System.Events.Output`, so hosts may observe both without duplicates.

## Legacy Application Conversion

The former `CompositionDefinition` host is removed in version 3. Convert its
configuration once, then use the canonical host:

```csharp
var definition = new LegacyCompositionDefinitionMigrator()
    .Migrate(legacyConfiguration);

services
    .AddFluxFlowApplication(definition)
    .AddFluxFlowEngine()
    .AddMyComponents();
```

Persist the canonical result so subsequent startup does not repeat migration.
Application load, validation, revision results, component Events, and runtime
ports then use one model and one hosting lifecycle.

Next: [Workspace Projection](06-workspace-projection.md).

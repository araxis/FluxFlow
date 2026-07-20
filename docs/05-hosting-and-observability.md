# Hosting And Observability

The default hosted path is `FluxFlow.Composition.Hosting` over the canonical
flat `ApplicationDefinition`. It loads exactly `Resources` and `Workflows`,
delegates concrete candidate construction to an explicitly registered factory,
and serializes initial activation and later complete-definition revisions.
`IApplicationRevisionHost` is the primary lifecycle, status, reload, and direct
complete-definition update surface.

## Register The Application

```csharp
services
    .AddFluxFlowApplication(configuration)
    .UseCandidateFactory<ApplicationCandidateFactory>()
    .UseRevisionEventSink<ApplicationRevisionEventSink>()
    .Configure(options => options.InitialRevisionId = "deployment-42");
```

The configuration object may be the exact application root. Pass a section
name when the canonical document is nested under host settings:

```csharp
services
    .AddFluxFlowApplication(configuration, "FluxFlowApplication")
    .UseCandidateFactory<ApplicationCandidateFactory>();
```

There is no assembly scanning. The candidate factory is a normal DI service and
owns preparation of resource and workflow provider snapshots, components,
stable-port attachments, and compiled links. Adapter packages still own
concrete clients, stores, retry behavior, credentials, and protocol lifetimes.

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
be observed directly or linked into workflows. Component packages remain free
of an Engine dependency and may expose explicit domain event outputs when
workflow logic needs those events.

## Legacy Composition Host

`AddFluxFlowComposition(...)` and `ICompositionRuntimeHost` remain available
for the released standalone `CompositionDefinition` runtime:

```csharp
services
    .AddFluxFlowComposition(legacyConfiguration)
    .RegisterNodes(registry => registry.RegisterMyNodes());

var legacyHost = services.GetRequiredService<ICompositionRuntimeHost>();
var build = await legacyHost.BuildAsync();
if (build.Succeeded)
    await legacyHost.StartRuntimeAsync();
```

That host exposes `CompositionRuntime.Events`, `Errors`, `Completion`, and build
diagnostics. New canonical applications should use `AddFluxFlowApplication`;
do not register both hosting models as competing owners of the same graph.

Next: [Workspace Projection](06-workspace-projection.md).

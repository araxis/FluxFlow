# FluxFlow.Composition.Hosting

Canonical application hosting and immutable provider-snapshot bridge for
`FluxFlow.Composition`.

Use this package when a .NET host wants DI/configuration to own composition
startup while keeping concrete resources in adapter packages.

## Boundary

This package owns:

- building immutable host, resource-revision, and workflow-revision service
  provider snapshots
- explicit keyed registration of resources, components, typed ports, and
  payload-independent signal targets by canonical application address
- loading the canonical flat `ApplicationDefinition` from an object, an exact
  `IConfiguration` root, or a named configuration section
- registering one canonical application revision host with
  `IServiceCollection` and `IHostedService`
- exposing normal source-load and revision-update results without terminating
  the .NET host for an ordinary rejected activation
- serializing complete-definition revision preparation, activation, commit,
  drain, and disposal through host-supplied candidates

Named component resources resolve through the `ComponentActivationContext`
instance methods in `FluxFlow.Composition`; this package's role is registering
the canonical application runtime against the host's keyed services.

It does not own resource creation policies. Adapter packages still own concrete
clients, stores, reconnect behavior, secrets, hosted client lifetime, and
adapter-specific options.

Provider snapshots do not merge service providers and do not fall back to an
arbitrary parent provider. Compose `IServiceCollection` instances before
building a snapshot, or bridge an exact external instance explicitly.

## Canonical Application Hosting

When the immutable `ComponentCatalog` and its normalizer are registered, the
host normalizes component and resource aliases immediately after load and
before revision planning. `ApplicationRevisionUpdateResult` exposes structured
normalization diagnostics. Alias-only updates compare equal to the active
canonical definition and do not prepare or activate another candidate.

Register the complete flat application document and an explicit candidate
factory. The factory prepares resource providers, workflow providers,
components, stable-port attachments, and compiled routing for one complete
candidate. Hosting coordinates its lifecycle but does not discover factories,
scan assemblies, or depend on Engine.

```csharp
using FluxFlow.Composition.Hosting;

services
    .AddFluxFlowApplication(configuration)
    .AddApplicationRevisionCandidateFactory<ApplicationCandidateFactory>()
    .AddApplicationRevisionEventSink<ApplicationRevisionEventSink>()
    .ConfigureFluxFlowApplication(
        options => options.InitialRevisionId = "deployment-42");
```

`configuration` must contain exactly `Resources` and `Workflows` at its root.
Pass a section name when those two properties live under a host-specific
configuration section. `StaticApplicationDefinitionSource` and
`ConfigurationApplicationDefinitionSource` can also be registered directly.

The hosted service loads and applies the initial definition. A source-load
failure becomes `ApplicationRevisionLoadResult.Error` with stable code
`revision.source.load_failed`; the host enters `Degraded` and the surrounding
.NET host continues running. A candidate rejection is returned through
`ApplicationRevisionUpdateResult` and never replaces an already active
revision.

```csharp
var host = services.GetRequiredService<IApplicationRevisionHost>();
var reload = await host.ReloadAsync("deployment-43");

if (!reload.Succeeded)
{
    var sourceError = reload.Error;
    var revisionFailures = reload.Update?.Failures ?? [];
}
```

`ApplyAsync(...)` accepts an already loaded complete definition. `ReloadAsync`
loads another complete definition from the configured source. Partial patches,
file watching, and remote configuration transport are source-layer concerns.
Stop drains and disposes the active candidate exactly once. Cleanup failures
are reported after all candidate cleanup has been attempted.

## Provider Snapshots

`CompositionServiceProviderSnapshotBuilder` copies service descriptors when
they are added. Later changes to the source collection do not affect a built
snapshot. `Build(...)` creates a normal Microsoft DI provider with scope and
build validation enabled by default.

```csharp
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Snapshots;
using Microsoft.Extensions.DependencyInjection;

var registrations = new ServiceCollection();
var clientAddress = ApplicationAddress.Resource("Messaging", "Client1");

registrations.AddFluxFlowResource<IMessageClient>(
    clientAddress,
    services => new MessageClient(
        services.GetRequiredService<ClientOptions>()));

await using var resources = new CompositionServiceProviderSnapshotBuilder()
    .AddServices(registrations)
    .Build(CompositionProviderBoundary.ResourceRevision, "resources-7");

var client = resources.GetRequiredKeyedService<IMessageClient>(
    "Resources.Messaging.Client1");
```

Canonical `ApplicationAddress.Value` strings are the DI keys. Resource keys use
`Resources.Group.Resource`, components use `Workflow.Component`, and typed
ports use `Workflow.Component.Port`. System outputs may also be registered as
typed output ports. The same resource string stored in canonical JSON therefore
resolves directly through keyed DI.

Factory registrations are owned and disposed by the built provider. Methods
whose names contain `View` create non-owning aliases of another provider-owned
service. Methods whose names start with `AddExternal` and builder methods whose
names start with `BridgeExternal` retain external ownership. Component and port
aliases use non-owning forwarding views so one underlying block is never
disposed twice.

`CompositionProviderBoundary` distinguishes `Host`, `ResourceRevision`, and
`WorkflowRevision` snapshots. `CompositionProviderSnapshotInfo` is a stable
transport record for revision events. Scopes remain opt-in through
`snapshot.Services.CreateScope()`; message processing does not create scopes
implicitly. Prefer `DisposeAsync()` when registrations may be async-disposable.

`CompositionServiceProviderSnapshot.CreateExternalHost(...)` wraps an existing
host provider without taking disposal ownership. This is an explicit bridge,
not a provider fallback mechanism.

## Transactional Revisions

`ApplicationRevisionCoordinator` accepts the next complete canonical
`ApplicationDefinition`; partial document patches are a source-layer concern.
It plans against the latest committed definition, asks an explicit
`IApplicationRevisionCandidateFactory` to prepare replacements away from live
routing, and serializes concurrent updates.

```csharp
await using var revisions = new ApplicationRevisionCoordinator(
    currentDefinition,
    candidateFactory,
    revisionEventSink,
    currentCandidate);

var result = await revisions.ApplyAsync("orders-8", nextDefinition);
if (!result.IsActivated)
{
    foreach (var failure in result.Failures)
        Console.Error.WriteLine(failure.Error.Code);
}
```

Candidate activation is the commit boundary. After it succeeds, the coordinator
publishes one immutable `Current` snapshot before draining the previous
candidate. Drain and disposal are both attempted; failures are returned with
the activated result and do not roll back the new revision. Failure or caller
cancellation before activation disposes the prepared candidate and preserves
the old definition.

Candidate factories own package-specific construction and must clean up any
partial work if `PrepareAsync` throws. Candidates expose provider snapshot
metadata and must make a failed `ActivateAsync` safe to dispose. The coordinator
does not depend on Engine, merge service providers, discover registrations, or
define resource creation policy.

## Component Registration

`FluxFlow.Engine.Hosting` supplies the standard canonical candidate factory.
Register each component family once through `IServiceCollection`:

```csharp
services
    .AddFluxFlowApplication(configuration)
    .AddFluxFlowEngine()
    .AddMappingComponents()
    .AddHttpComponents();
```

Family registration is explicit and idempotent. Neither Hosting nor Engine
scans assemblies or discovers component factories implicitly. Component
factories resolve flat canonical resource properties through
`ComponentActivationContext`; the host and adapters own keyed external services
and their lifetimes. Families that translate application resource definitions
register one focused `IApplicationResourceRegistrar` through
`AddApplicationResourceRegistrar<TRegistrar>()`.

The removed `CompositionDefinition` host is not a parallel runtime option.
Convert old documents with
`LegacyCompositionDefinitionMigrator`, persist the canonical result, and use
`AddFluxFlowApplication(...)`.

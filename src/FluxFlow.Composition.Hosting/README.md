# FluxFlow.Composition.Hosting

Optional hosting and immutable provider-snapshot bridge for
`FluxFlow.Composition`.

Use this package when a .NET host wants DI/configuration to own composition
startup while keeping concrete resources in adapter packages.

## Boundary

This package owns:

- building immutable host, resource-revision, and workflow-revision service
  provider snapshots
- explicit keyed registration of resources, components, typed ports, and
  payload-independent signal targets by canonical application address
- registering a single composition runtime with `IServiceCollection`
- loading a `CompositionDefinition` from an object or `IConfiguration`
- building the runtime through `CompositionRuntimeBuilder`
- starting and stopping the runtime through `IHostedService`
- exposing build diagnostics through `ICompositionRuntimeHost`

Named node resources resolve through the `CompositionNodeFactoryContext`
instance methods in `FluxFlow.Composition`; this package's role is registering
the composition runtime against the host's keyed services. The older context
extension methods in this package remain as obsolete delegating wrappers.

It does not own resource creation policies. Adapter packages still own concrete
clients, stores, reconnect behavior, secrets, hosted client lifetime, and
adapter-specific options.

Provider snapshots do not merge service providers and do not fall back to an
arbitrary parent provider. Compose `IServiceCollection` instances before
building a snapshot, or bridge an exact external instance explicitly.

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
transport record for later revision events. Scopes remain opt-in through
`snapshot.Services.CreateScope()`; message processing does not create scopes
implicitly. Prefer `DisposeAsync()` when registrations may be async-disposable.

`CompositionServiceProviderSnapshot.CreateExternalHost(...)` wraps an existing
host provider without taking disposal ownership. This is an explicit bridge,
not a provider fallback mechanism.

## Hosted Runtime Registration

```csharp
services.AddKeyedSingleton<IMessageStore>("primary", new InMemoryMessageStore());

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.Register(
        "sample.sink",
        context =>
        {
            var store = context.GetRequiredResource<IMessageStore>("store");
            var node = new StoreSinkNode(store);
            return ValueTask.FromResult(ComposedNode.Create(
                node,
                inputs: [CompositionPorts.Input<string>("Input", node.Input)]));
        },
        inputs: [CompositionPorts.Metadata<string>("Input")]));
```

Reusable packages or hosts can also register explicit contributor classes or
instances:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodeContributor<AppCompositionNodes>();
```

Contributor registration is explicit and duplicate-safe by implementation type.
The hosting package does not scan assemblies or discover node factories
implicitly.

The established hosted runtime configuration records the resource reference by
name:

```json
{
  "workflows": {
    "main": {
      "nodes": {
        "sink": {
          "type": "sample.sink",
          "resources": {
            "store": "primary"
          }
        }
      }
    }
  }
}
```

The node factory asks for the local resource slot (`store`), and hosting
resolves the keyed service named `primary`.
Resource slot names passed to the factory helpers and configured keyed service
references are trimmed before lookup, so incidental surrounding whitespace does
not change which host-owned service is resolved.

## Runtime Access

```csharp
var host = services.GetRequiredService<ICompositionRuntimeHost>();

foreach (var diagnostic in host.Diagnostics)
{
    Console.Error.WriteLine(diagnostic.Message);
}
```

By default the hosted service builds and starts the runtime with the host and
throws `CompositionHostingException` if the composition cannot be built.
`CompositionHostingException.Diagnostics` is a construction-time snapshot, so
callers can safely keep the exception as stable build-failure evidence.
Hosted and manual start/stop calls are idempotent at the hosting boundary: a
runtime that is already started is not started again, and a runtime that has
already been stopped is not completed or started again.

If you already have the exact section, call `AddFluxFlowCompositionSection(...)`.
The hosting registration APIs reject null service collections, definitions,
configuration roots, definition sources, node registration delegates, and options
configuration delegates. A null section name is rejected explicitly; pass an
empty string only when the supplied configuration object is already the exact
composition section.

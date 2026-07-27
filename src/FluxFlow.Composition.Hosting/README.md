# FluxFlow.Composition.Hosting

Obsolete compatibility package for applications migrating to
`FluxFlow.Engine` 6.x.

New applications should reference `FluxFlow.Engine`, call `AddFluxFlow(...)`,
and resolve `FluxFlowApplication`. This package contains no revision
coordinator, provider-snapshot builder, runtime assembler, port runtime, or
lifecycle state implementation.

## Preferred API

```csharp
using FluxFlow.Engine;

services.AddFluxFlow(configuration);

var application = provider.GetRequiredService<FluxFlowApplication>();
await application.StartAsync();
await application.ReloadAsync("deployment-43");
await application.StopAsync();
```

The Engine hosted-service adapter and direct DI resolution use the same
singleton application instance.

## Compatibility Surface

The package retains small obsolete adapters where practical:

- `AddFluxFlowApplication(...)` forwards to `AddFluxFlow(...)`.
- `AddFluxFlowEngine()` is a no-op registration bridge because Engine setup is
  included by `AddFluxFlow(...)`.
- legacy definition-source types adapt to Engine definition sources.
- `ApplicationRevisionHost` delegates lifecycle calls to the registered
  `FluxFlowApplication`.
- legacy keyed DI extension methods delegate to
  `FluxFlow.Composition.DependencyInjection`.

These APIs exist only to support staged migration. They do not maintain a
second lifecycle, synchronization gate, current snapshot, port generation, or
resource provider. `FluxFlow.Composition.Hosting` is planned for removal in the
next major release.

## Migration

Replace:

```csharp
services
    .AddFluxFlowApplication(configuration)
    .AddFluxFlowEngine();

var host = provider.GetRequiredService<IApplicationRevisionHost>();
await host.StartApplicationAsync();
```

with:

```csharp
services.AddFluxFlow(configuration);

var application = provider.GetRequiredService<FluxFlowApplication>();
await application.StartAsync();
```

Use `application.Ports` instead of resolving a separate runtime-access service.
Use `ApplicationUpdateResult.Diagnostics` instead of separate load errors and
revision failure collections. Resource registrars and keyed component/port
registration helpers now live in `FluxFlow.Composition`.

No package in the normal Engine or component path should depend on this
compatibility package.

# Application Health Readiness

`FluxFlow.Engine.HealthChecks` is an optional, lightweight bridge from the
canonical `FluxFlowApplication` lifecycle to the standard .NET health-check
abstraction. It answers one question: can this host currently serve work
through an active FluxFlow application revision?

The adapter is separate from `FluxFlow.Engine`. Applications that do not use
health checks take no dependency on it and pay no registration or runtime
cost. The Engine does not reference the adapter.

## Registration

Reference `FluxFlow.Engine.HealthChecks`, then add the application and readiness
check through normal dependency injection:

```sh
dotnet add package FluxFlow.Engine.HealthChecks
```

```csharp
using FluxFlow.Engine;
using FluxFlow.Engine.HealthChecks;

services.AddFluxFlow(definition);

services.AddHealthChecks()
    .AddFluxFlowApplication();
```

`AddFluxFlowApplication()` returns the same `IHealthChecksBuilder`. Repeating
the call is idempotent and preserves unrelated health-check registrations. It
adds exactly one registration with:

- name: `fluxflow.application`
- tags: `fluxflow`, `ready`
- configured failure status: `Unhealthy`

Calling `AddFluxFlow(...)` alone does not register a health check.

ASP.NET Core hosts may expose the standard result using host-owned endpoint
wiring:

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = static registration => registration.Tags.Contains("ready")
    });
```

The package itself has no ASP.NET Core dependency and does not create an
endpoint. The predicate is ordinary host policy; the fixed `ready` tag lets the
host keep readiness separate from liveness or dependency checks.

## Status Contract

| Standard status | FluxFlow condition |
|-----------------|--------------------|
| `Healthy` | `Running` or `Reloading`, an active revision exists, and the latest update was not rejected. |
| `Degraded` | `Running` or `Reloading`, an active revision exists, and the latest update was rejected. The previous revision remains ready. |
| `Unhealthy` | FluxFlow is not registered, no active revision exists, lifecycle state is not ready, or the application is stopping or stopped. |

A rejected hot reload is deliberately `Degraded`, not `Unhealthy`, because
transactional activation preserves the previous usable revision. A rejected
initial start has no active revision and is therefore `Unhealthy`. A later
successful update returns the result to `Healthy`.

The check fails closed if it cannot observe a stable, ready lifecycle snapshot.
Cancellation is propagated immediately with the caller's token.

## Bounded Result Data

The result may contain only these seven keys:

- `applicationState`
- `activeRevisionId`
- `activeSequence`
- `requestedRevisionId`
- `lastUpdateStatus`
- `diagnosticStage`
- `diagnosticCode`

Only applicable values are emitted. When FluxFlow is not registered,
`applicationState` is `Unavailable`. When diagnostics exist, only the final
diagnostic stage and stable error code are included.

The result never includes workflow payloads, application definitions, component
or resource options, port addresses, diagnostic messages or details, exception
objects or text, connection strings, file paths, credentials, secrets, or
arbitrary tags. This keeps the health response bounded and safe to expose
through a host-selected readiness endpoint.

## Runtime And Ownership Boundary

Each probe performs a short, observational read of existing in-memory
`FluxFlowApplication` properties. It does not mutate lifecycle state, reload a
definition, activate a revision, resolve workflow resources, query durable
stores, contact dependencies, scan assemblies, or use reflection.

The package adds no hosted service, worker, timer, polling loop, cache, logger,
database access, or dependency-health aggregation. Readiness evaluation occurs
only when the host invokes the standard health-check service.

## Deliberate Non-Goals

This check does not report:

- process liveness;
- durable input or output backlog thresholds;
- database, broker, HTTP endpoint, or other external dependency health;
- workflow business outcomes or payload errors;
- deployment traffic policy beyond the application-readiness status above.

Hosts may register separate standard checks for those concerns and select them
with their own endpoint predicates. Keeping them separate preserves FluxFlow's
lightweight in-process core and makes operational policy explicit.

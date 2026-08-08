# FluxFlow.Engine.HealthChecks

Optional standard .NET readiness integration for a canonical
`FluxFlowApplication`.

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

The idempotent registration adds one check named `fluxflow.application` with
the tags `fluxflow` and `ready`.

- `Healthy`: a revision is active and usable.
- `Degraded`: the latest update was rejected, but rollback preserved the active
  revision.
- `Unhealthy`: FluxFlow is missing, has no active revision, or is stopped.

The check reads existing in-memory application state only. It adds no worker,
polling, timer, storage query, ASP.NET Core middleware, or endpoint. Result data
is bounded to lifecycle state, revision identity/sequence, update status, and
the final diagnostic stage/code. It never includes payloads, definitions,
addresses, diagnostic messages/details, exceptions, paths, connections, or
secrets.

ASP.NET Core hosts may expose the standard result using host-owned endpoint
wiring:

```csharp
app.MapHealthChecks("/health/ready");
```

This package reports application readiness only. Process liveness, durable
backlog thresholds, and external dependency health remain separate operational
decisions.

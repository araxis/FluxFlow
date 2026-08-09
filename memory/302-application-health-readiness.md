# Application Health Readiness

Date: 2026-08-08

## Decision

FluxFlow now offers one optional standard .NET readiness integration in the
separate `FluxFlow.Engine.HealthChecks` 1.0.0 package. The package is a leaf
adapter over `FluxFlowApplication`; `FluxFlow.Engine` does not reference it.

The public surface intentionally contains one extension:

```csharp
services.AddHealthChecks()
    .AddFluxFlowApplication();
```

It registers exactly one health check named `fluxflow.application` with the
exact tags `fluxflow` and `ready`. Repeated calls are idempotent and do not
replace unrelated standard health checks. `AddFluxFlow(...)` alone remains
unchanged and registers no health check.

## Readiness Semantics

- Healthy: lifecycle state is `Running` or `Reloading`, an active snapshot
  exists, and the latest update was not rejected.
- Degraded: lifecycle state is `Running` or `Reloading`, an active snapshot
  exists, and the latest update was rejected. Transactional rollback keeps the
  previous revision usable.
- Unhealthy: FluxFlow is missing, no active snapshot exists, lifecycle state is
  not ready, or the application is stopping or stopped.

A rejected initial start is unhealthy because there is no active revision. A
successful update after a rejected reload restores healthy status. The adapter
derives readiness from `Current` and `LastUpdate`; it does not equate the
Engine's lifecycle enum with the standard health status.

## Data And Privacy Boundary

Results may contain only:

- `applicationState`
- `activeRevisionId`
- `activeSequence`
- `requestedRevisionId`
- `lastUpdateStatus`
- `diagnosticStage`
- `diagnosticCode`

Only applicable keys are present. The check never returns payloads,
definitions, component/resource configuration, addresses, diagnostic messages
or details, exceptions, connection strings, file paths, credentials, secrets,
or arbitrary high-cardinality values.

## Runtime Boundary

The check performs a bounded observational read of existing in-memory
application state when the host invokes `HealthCheckService`. It adds no hosted
service, background worker, polling loop, timer, cache, storage query, resource
activation, network request, reflection, assembly scanning, logging pipeline,
ASP.NET Core dependency, or endpoint.

Liveness, durable queue thresholds, and external dependency health remain
separate host policies. ASP.NET Core hosts may expose the check through their
own `MapHealthChecks` endpoint and selection predicate.

## Packaging And Verification Boundary

The package targets .NET 8 and .NET 10, references only Engine plus the
standard health-check abstractions, is listed in the package manifest as the
initial release, and participates in public API governance. The isolated
package-only acceptance consumer references the packed candidate and proves a
healthy code-first application through the standard `HealthCheckService` path
with the exact marker `PACKAGE_ACCEPTANCE_HEALTH_OK=True`.

## Verification Evidence

- Focused health project: 32/32 passed, no failures, skips, or warnings.
- Focused Release health/package governance: 21/21 passed, no failures, skips,
  or warnings.
- Public API baseline: explicitly accepted and then verified normally, 2/2
  both times with no warnings. The appended entry is index 59; prior indices
  did not move.
- Pack-script rehearsal: 1/1 passed. The controlled process harness proved ten
  pack commands, fifteen total invocations, exact markers, and cleanup.
- Direct package-only acceptance: exit 0 in 33.9 seconds. Ten real package and
  symbol archives were created, restored and hash-verified from the isolated
  candidate source, the .NET 8 consumer built with zero warnings/errors, and
  all Engine/code-first/resource/health/Fluent/durability/restart markers were
  emitted. Both runner-owned directories were removed.
- Final Release restore/build: 136 projects, zero errors and warnings.
- Full solution: 2,665/2,665 tests across 67 projects, zero failures and
  warnings.
- Dedicated Release suite: 185/185 tests, zero warnings.
- Full formatting verification and `git diff --check`: passed.
- Direct and transitive dependency audit: no vulnerable packages.

The held-reload test uses a causal preparation gate, not a sleep: while the
candidate is blocked and the application reports `Reloading`, readiness is
healthy and the old typed route still returns `still-serving`. Release then
allows the candidate to apply. Rejected reload, failed initial start, recovery,
stopping/stopped/disposed, missing registration, cancellation, privacy,
idempotency, and non-mutation paths are all independently asserted.

No package was published and no branch, commit, pull request, tag, or release
was created.

The direct run initially identified a missing host logging registration in the
bare console fixture. Adding standard `services.AddLogging()` fixed the host;
the FluxFlow health adapter gained no logging dependency or behavior.

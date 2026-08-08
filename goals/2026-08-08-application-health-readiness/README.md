# Goal: Add a standard optional FluxFlow application readiness check

- Date: 2026-08-08
- State: complete
- Scope: one optional Engine health-check adapter package, its tests, package/release governance, package-only acceptance, documentation, and memory
- Compatibility: additive public package; no Engine behavior or existing package API change

## Objective

Add a small, explicit integration with the standard .NET health-check system so
hosts can expose FluxFlow application readiness without inventing their own
interpretation of `FluxFlowApplication.State`, `Current`, and `LastUpdate`.

The normal registration must be familiar and flat:

```csharp
services.AddFluxFlow(definition);

services.AddHealthChecks()
    .AddFluxFlowApplication();
```

ASP.NET Core hosts may then expose the standard endpoint through their own web
host configuration:

```csharp
app.MapHealthChecks("/health/ready");
```

The adapter must remain optional. Installing or using `FluxFlow.Engine` alone
must not add health-check dependencies, checks, workers, timers, polling,
database access, or endpoint behavior.

## Architectural principles

- KISS: one package, one public extension method, one internal stateless check.
- SRP: Engine owns application lifecycle; the adapter only translates the
  existing observable lifecycle into the standard `HealthCheckResult` model.
- IOC: the standard health-check registration factory receives the host service
  provider and resolves the optional `FluxFlowApplication` dependency.
- No service locator inside the check: the check receives the resolved
  application reference, or `null` when FluxFlow was not registered.
- No parallel status model: do not add a second mutable application state,
  cache, monitor, event projection, or health-specific state machine.
- No hidden work: checks are evaluated only when the host asks for them.
- Privacy by construction: expose only bounded operational identifiers and
  enum/code values; never expose payloads, component/resource properties,
  exception objects, diagnostic messages/details, paths, connections, or
  secrets.
- Preserve current behavior: no changes to Engine start, reload, rollback,
  ports, revisions, disposal, durability, JSON, Fluent, MQTT, or hosting.

## Required package boundary

Create a new packable project:

```text
src/FluxFlow.Engine.HealthChecks/FluxFlow.Engine.HealthChecks.csproj
```

Required project metadata:

- package/assembly/root namespace: `FluxFlow.Engine.HealthChecks`;
- version: `1.0.0`;
- target frameworks: `net8.0;net10.0`;
- direct project reference only to `FluxFlow.Engine`;
- one direct package reference to the centrally versioned
  `Microsoft.Extensions.Diagnostics.HealthChecks` package;
- package-local `README.md` packed at the package root;
- no durability, ASP.NET Core endpoint, database/provider, Fluent, MQTT,
  Designer, reflection, or scanning dependency.

Add `Microsoft.Extensions.Diagnostics.HealthChecks` to central package version
management using the repository's current `Microsoft.Extensions.*` version
line. Do not move this dependency into `FluxFlow.Engine`.

Add the new package to `eng/packages.json` as an initial release with a null
binary compatibility baseline. Append it to preserve all existing manifest
indices and public API baseline identities. Add a non-empty
`FluxFlow.Engine.HealthChecks 1.0.0` changelog section.

## Required public API

Expose exactly one normal public declaration surface in the new package:

```csharp
namespace FluxFlow.Engine.HealthChecks;

public static class FluxFlowHealthChecksBuilderExtensions
{
    public static IHealthChecksBuilder AddFluxFlowApplication(
        this IHealthChecksBuilder builder);
}
```

Rules:

- validate `builder` with `ArgumentNullException.ThrowIfNull`;
- return the same builder instance;
- register one health check named `fluxflow.application`;
- assign exactly the tags `fluxflow` and `ready`;
- use `HealthStatus.Unhealthy` as the standard registration failure status;
- repeated calls on the same service collection are idempotent and keep one
  FluxFlow registration/check;
- do not add overloads, callbacks, custom names, custom status mapping,
  thresholds, timeout settings, or options in this round;
- do not expose the check implementation, registration marker, metadata keys,
  or a new application-status DTO publicly;
- do not add forwarding APIs to Engine or Fluent.

Idempotency may use one private registration-marker service in the adapter
package. It must not be hosted, disposable, mutable, or resolved during normal
application execution.

## Required check behavior

Implement one internal stateless `IHealthCheck`. Its constructor receives a
nullable `FluxFlowApplication`. The health registration factory resolves the
application with ordinary optional DI resolution, so a missing FluxFlow
registration produces a controlled unhealthy result rather than a resolution
exception.

At evaluation time:

1. honor the supplied cancellation token immediately;
2. read the current application state, active snapshot, and last update only;
3. perform no asynchronous I/O, waiting, polling, locking, database access,
   service resolution, event subscription, logging, or runtime mutation;
4. return a completed task containing one deterministic result.

### Health mapping

| FluxFlow condition | Standard health status | Meaning |
|---|---|---|
| `FluxFlowApplication` is not registered | `Unhealthy` | Host registration is incomplete |
| `Running` with an active revision and no rejected latest update | `Healthy` | The active revision is usable |
| `Reloading` with an active revision and no rejected latest update | `Healthy` | The previous active revision remains usable while the candidate is prepared |
| `Running` or `Reloading` with an active revision and the latest update rejected | `Degraded` | Rollback preserved a usable active revision, but operator attention is appropriate |
| No active revision in `Empty`, `Starting`, or `Degraded` | `Unhealthy` | No usable application revision exists |
| `Stopping` or `Stopped` | `Unhealthy` | The application is not ready to serve workflow traffic |
| Any impossible/inconsistent combination, including a ready state without an active revision | `Unhealthy` | Fail closed without throwing |

An applied or unchanged update following a rejection must restore `Healthy`.
Stopping and disposal must both produce the same deterministic stopped outcome.

### Descriptions

Use short stable descriptions that explain the category but do not repeat
diagnostic messages or exception text:

- healthy: an active FluxFlow revision is available;
- degraded: the active revision remains available after the latest update was
  rejected;
- unhealthy: FluxFlow is not registered, has no active revision, or is stopped.

Do not attach an exception to any `HealthCheckResult` created by the adapter.

### Bounded result data

The result data dictionary must contain at most these seven keys:

- `applicationState`;
- `activeRevisionId`;
- `activeSequence`;
- `requestedRevisionId`;
- `lastUpdateStatus`;
- `diagnosticStage`;
- `diagnosticCode`.

Rules:

- always include `applicationState`; use `Unavailable` when FluxFlow is not
  registered;
- include active revision fields only when an active snapshot exists;
- include requested revision and update status only when a last update exists;
- include only the final diagnostic's stage and stable `FlowError.Code` when a
  diagnostic exists;
- values are only strings or the active sequence number;
- never include `FlowError.Message`, `FlowError.Details`, exception data,
  application definitions, resources, components, port addresses, message
  identities, payloads, paths, connection information, or configuration.

## Concurrency and lifecycle interpretation

The adapter must not take Engine's internal lifecycle gate or introduce a new
lock. `FluxFlowApplication` already publishes state, active snapshot, and last
update through its established thread-safe read surface.

The check must classify transitions conservatively:

- a visible active revision is required for `Healthy` or `Degraded`;
- a rejected update never makes the still-active previous revision
  `Unhealthy`;
- starting without an active revision and stopping are never ready;
- impossible/torn observations fail closed as `Unhealthy` and never throw.

Tests must exercise the check while a candidate reload is deliberately held in
preparation, without sleeps or timing guesses, to prove the active revision
remains healthy during a normal reload. They must also prove rejected reload
rollback becomes degraded while the previous route remains operational.

## Package-only acceptance

Extend the existing package-only consumer rather than creating a separate
acceptance harness:

- add `FluxFlow.Engine.HealthChecks` as a top-level package reference controlled
  by `FluxFlowEngineHealthChecksVersion`;
- add `engine-healthchecks` to the exact candidate closure and pack list;
- keep the independent public package source for Microsoft dependencies;
- register the check through `AddHealthChecks().AddFluxFlowApplication()`;
- after the code-first application is active, resolve the standard
  `HealthCheckService` and require `fluxflow.application` to be `Healthy` with
  the exact `fluxflow`/`ready` tags and safe active-revision metadata;
- emit `PACKAGE_ACCEPTANCE_HEALTH_OK=True` exactly once;
- preserve all existing JSON, code-first resource, Fluent, durability, restart,
  candidate-hash, isolated restore/build/run, and cleanup behavior;
- update exact pack and process invocation counts for the additional package.

## Documentation and memory

Create `docs/42-application-health-readiness.md` covering:

- package installation and the two-line registration;
- ASP.NET Core `MapHealthChecks` example as host-owned endpoint wiring;
- exact status mapping;
- exact metadata and privacy boundary;
- fixed name/tags and standard tag filtering;
- the absence of polling, workers, storage queries, and durable backlog
  thresholds;
- the distinction between application readiness, process liveness, durable
  backlog status, and business-level dependency health.

Update:

- root `README.md` package/features and concise usage;
- `src/FluxFlow.Engine/README.md` to point to the optional adapter;
- `docs/05-hosting-and-observability.md`;
- `docs/14-public-api-overview.md` package/API table;
- `docs/README.md` index;
- `docs/38-release-validation.md` package marker/count contract;
- `memory/00-index.md`;
- `memory/01-current-state.md`;
- new `memory/302-application-health-readiness.md` with the final decision and
  evidence.

Do not add an ASP.NET Core dependency or endpoint implementation to the package.
The endpoint mapping in documentation belongs to the consuming host.

## Tests and governance

Use the repository's xUnit and Shouldly conventions. Create
`tests/FluxFlow.Engine.HealthChecks.Tests` and cover the real standard
`HealthCheckService` plus focused direct internal behavior where cancellation
or precise mapping requires it.

Required behavioral evidence:

1. default registration uses the exact name, tags, failure status, and same
   builder return value;
2. duplicate registration is idempotent and yields one report entry;
3. a missing FluxFlow registration is unhealthy without an exception;
4. an unstarted application is unhealthy with bounded `Empty` metadata;
5. successful start is healthy with exact revision metadata;
6. a deliberately held reload remains healthy and reports the still-active
   revision;
7. rejected initial activation is unhealthy;
8. rejected hot reload is degraded, reports only safe final diagnostic
   stage/code, and preserves the previous active revision/route;
9. a later successful or unchanged update restores healthy status;
10. stop and dispose are deterministically unhealthy/stopped;
11. cancellation throws `OperationCanceledException` with the exact token;
12. malformed/inconsistent inputs to the internal classifier fail closed;
13. result data never exceeds the seven allowed keys and contains no message,
    details, exception, definition, resource, component, address, payload,
    connection, or secret content.

Add Release tests asserting:

- the new project is optional and Engine does not reference it or the health
  package;
- only the approved one-method public surface is present;
- source contains no reflection, scanning, global registry, hosted service,
  worker, timer, polling, delay, database/provider, durability, ASP.NET endpoint,
  logging, or mutable static state;
- project targets and dependencies are exact;
- README/docs contain the required normal usage and boundaries;
- manifest, changelog, API baseline, and package-only closure include the new
  package and marker.

Update the public API baseline only through the repository's explicit baseline
acceptance workflow after the new project and manifest are final.

## Explicit non-goals

- Durable input/output backlog health checks or threshold options.
- Broker, database, filesystem, HTTP, MQTT, or other dependency health checks.
- Process liveness, watchdogs, heartbeats, startup probes, or shutdown probes.
- Custom health-check names, tags, statuses, callbacks, options, or endpoints.
- A custom JSON health-response writer.
- ASP.NET Core middleware or a web framework dependency.
- Polling, scheduled observation, caches, hosted services, workers, channels,
  timers, or event subscriptions.
- Health-specific Engine state, a new public status DTO, or Engine lifecycle
  changes.
- Exposing diagnostic messages/details or exceptions.
- Moving health settings into `FluxFlowApplicationOptions`.
- Publishing packages, committing, pushing, creating a branch/PR, or releasing.

## Implementation phases

### Phase 1: package and minimal registration

1. Add the central Microsoft health-check package version.
2. Create the packable adapter project and package README.
3. Implement the one-method extension, fixed registration, private idempotency
   marker, and nullable application factory.
4. Add the production project to the solution and package manifest.

### Phase 2: readiness classification

1. Implement the internal stateless check and conservative state mapping.
2. Build the bounded metadata dictionary.
3. Honor cancellation and attach no exceptions.
4. Add no Engine source dependency in the reverse direction.

### Phase 3: focused tests

1. Create the net10 test project and internal test access.
2. Add standard registration/report integration tests.
3. Add lifecycle, reload, rollback, recovery, cancellation, privacy, and
   inconsistent-observation tests.
4. Run only the new test project until green.

### Phase 4: package acceptance and release governance

1. Add the health package to the candidate closure/version arguments.
2. Exercise the check and exact marker in the package-only fixture.
3. Update Release source, manifest, docs, public-surface, pack-count, command,
   marker, and cleanup assertions.
4. Accept and then verify the public API baseline.

### Phase 5: documentation and memory

1. Add the dedicated documentation page and package README.
2. Update hosting, public API, release, root, docs index, and Engine references.
3. Record the decision in memory and update current-state/index files.

### Phase 6: verification and closure

Run sequentially with no overlapping build lanes:

1. new focused health-check tests;
2. focused Release/public/package/documentation tests;
3. real isolated package-consumer `-PackPackages` gate;
4. Release solution build with zero warnings/errors;
5. full solution tests with zero failures, skips, or warnings;
6. dedicated Release tests;
7. public API baseline verification without acceptance mode;
8. `dotnet format --verify-no-changes`;
9. `git diff --check`;
10. direct/transitive vulnerable-package audit;
11. source scans for the forbidden mechanisms and stale counts/markers.

Replace the completion section below with exact files, named tests, commands,
counts, warnings, and outcomes before marking the goal complete.

## Acceptance criteria

The goal is complete only when:

1. a normal host can register FluxFlow readiness through the standard two-line
   DI shape;
2. healthy/degraded/unhealthy mapping matches the table exactly;
3. failed hot reload remains usable and is reported degraded rather than down;
4. missing, unstarted, failed initial, stopping, stopped, and disposed states
   fail closed without exceptions or sensitive data;
5. registration is idempotent and the public API remains one method;
6. Engine and all existing packages remain independent of the optional adapter;
7. no worker, polling, reflection, storage I/O, ASP.NET endpoint, or new runtime
   state is introduced;
8. package-only consumption proves the packed adapter works on net8;
9. docs, package manifest, changelog, public API baseline, release governance,
   and memory are current;
10. every focused and repository-wide gate is green.

## Completion evidence

Completed 2026-08-08 without publishing, committing, pushing, or creating a
branch, pull request, tag, or release.

### Delivered surface

- Added the optional `FluxFlow.Engine.HealthChecks` 1.0.0 package targeting
  .NET 8 and .NET 10.
- Added the single public
  `IHealthChecksBuilder.AddFluxFlowApplication()` extension with fixed name,
  tags, unhealthy failure status, same-builder return, and idempotent
  registration.
- Added the internal stateless readiness check with the exact
  healthy/degraded/unhealthy mapping, forward/reverse stable observation,
  immediate cancellation, and the seven-key privacy boundary.
- Added the package manifest entry, changelog, accepted public API baseline,
  package README, dedicated documentation, root/Engine/hosting/public/release
  guidance, and memory records.
- Extended the existing package-only consumer to exercise the standard
  `HealthCheckService` path and emit
  `PACKAGE_ACCEPTANCE_HEALTH_OK=True` exactly once.

### Focused behavioral and governance evidence

- `FluxFlow.Engine.HealthChecks.Tests`: 32 passed, 0 failed, 0 skipped,
  0 warnings.
- Focused `HealthReadinessConventionTests` plus package acceptance assertions:
  21 passed, 0 failed, 0 skipped, 0 warnings.
- Public API baseline acceptance: 2 passed, 0 warnings; immediate normal-mode
  verification: 2 passed, 0 warnings. The new appended baseline is entry 59
  with three public declarations; all prior indices remain unchanged.
- Pack-mode script rehearsal fact
  `Acceptance_script_pack_mode_cleans_owned_source_and_workdir_after_success`:
  1 passed, 0 warnings. It proved ten candidate pack operations, fifteen total
  subprocesses, every default/seed/recovery marker exactly once including the
  health marker, and cleanup of both owned directories.
- Direct `eng/package-consumer-acceptance.ps1 -PackPackages`: exited 0 in
  33.9 seconds. It created all ten real package and symbol archives, restored
  them into an isolated .NET 8 package-only consumer, verified every restored
  FluxFlow package identity/version/archive, built with zero warnings/errors,
  executed Engine/code-first/resource/health/Fluent/durability plus separate
  seed/recovery processes, emitted every required marker exactly once, and
  removed both runner-owned directories.
- Real held-reload fact
  `Active_revision_stays_healthy_and_operational_while_candidate_reload_is_held_in_preparation`
  causally blocked candidate preparation without sleeping, observed
  `Reloading`, returned healthy readiness, and routed `still-serving` through
  the previous active revision before releasing the candidate.

### Repository-wide evidence

- Restore: 136 projects, 0 errors, 0 warnings.
- Final Release build: 136 projects, 0 errors, 0 warnings.
- Full solution tests: 2,665 passed across 67 test projects, 0 failures,
  0 warnings.
- Dedicated `FluxFlow.Release.Tests`: 185 passed, 0 warnings.
- `dotnet format FluxFlow.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.
- Direct/transitive vulnerable-package audit: no vulnerable packages in any
  project, including the new production and test projects.

The first full-suite run exposed a missing repository-required `dataflow`
package tag and one existing resource-registrar closure test whose async helper
could retain a JIT stack root. The package tag was added. The test now performs
setup/replacement on a joined dedicated thread and retains its exact alive
before / collected after assertions with one forced full-GC sequence and no
sleeps, polling, retries, or timeouts. Both exact facts and the repeated full
suite are green.

The first direct package-only execution additionally exposed that its bare
console `ServiceCollection` had not registered the standard logging service
required by `DefaultHealthCheckService`. The fixture now calls the ordinary
host-owned `services.AddLogging()`; the focused fixture assertion and repeated
direct package-only execution are green. No logging dependency or behavior was
added to the FluxFlow health package.

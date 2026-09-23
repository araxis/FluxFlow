# Consolidated framework: local closeout

The framework project consolidation is complete locally. The framework-only
baseline had 19 production projects and 45 solution projects; it now has nine
production projects and 27 solution projects, under the shallow layout established
earlier. UI and the old component packages remain outside this framework branch.
The full former implementation remains on local main; no remote publication,
push or new commit was performed in this step.

## Final production boundaries

| Package | Responsibility |
| --- | --- |
| FluxFlow.Core | Graphs, messages, values, execution, addressing, JSON, registration, mapping/retry policies and transactional revisions |
| FluxFlow.Hosting | .NET host integration, diagnostic logging, live event stream and standard readiness checks |
| FluxFlow.Observability.OpenTelemetry | OpenTelemetry integration |
| FluxFlow.Observability.AspNetCore | ASP.NET/SSE integration |
| FluxFlow.Expressions.CSharp | C# expressions |
| FluxFlow.Expressions.Jsonata | JSONata expressions |
| FluxFlow.Persistence | Durable input/output contracts and workers |
| FluxFlow.Persistence.Sqlite | SQLite input/output stores |
| FluxFlow.Persistence.SqlServer | SQL Server input/output stores |

Tests retain focused behavioral suites, including runtime, nodes and health, even
where production assemblies are consolidated. The two licensed SQL Server
integration suites remain separately runnable. Production package count is not
used as a reason to delete behavioral tests.

## Execution and development simplification

Core owns the shared FlowOutput delivery implementation, FlowRouteSelection,
FlowRequestReply, FlowLifetime and FlowDependencyOrder. Direct graph execution and
transactional revisions use those mechanisms; revision-specific port generations,
activation barriers and rollback remain explicit rather than being discarded.
The former Engine production project/assembly and separate HealthChecks package
are removed, with no forwarding projects. Runtime code lives in Core/Runtime;
readiness lives in Hosting/HealthChecks.

Earlier steps removed the component activator class and unified condition-context
creation across four paths. This closeout also removes ApplicationRuntimePlanFactory
and its temporary ApplicationRuntimePlan record. Preparation invokes registered
factories, compiles links using the shared compiler, describes ports and constructs
the revision directly inside the existing cleanup boundary. No new manager,
builder interface or global component-type switch was introduced.

Component development continues to use AddComponent with a factory and direct
context.HasInput/HasOutput/HasEvents registration. Moving packages did not introduce
a new component contract. JSON retains its existing flat structure and addressing.

Two public application entry points remain intentional: a directly owned graph
and an update-managed application. Their shared execution is not a claim that
their ownership policies are identical. Direct Stop drains and Dispose releases
ownership; update-host Stop also retires the active revision. Both behaviors are
covered by the same workflow comparison test. No public lifecycle API was silently
removed to obtain a single class name.

## Consumer migration

Replace FluxFlow.Engine package references with FluxFlow.Core and
FluxFlow.Engine.HealthChecks with FluxFlow.Hosting, then rebuild. Existing
FluxFlow.Engine and FluxFlow.Engine.HealthChecks namespace imports remain valid;
their types now reside in Core and Hosting assemblies respectively. The source
declaration baseline was reviewed and updated for assembly ownership changes.
The development versions remain Core 1.0.0-alpha.1 and Hosting 7.0.0-alpha.1;
these are unpublished local candidates, not a new published release.

Core now carries the former runtime's Microsoft.Extensions dependencies. Database
drivers, telemetry SDKs and ASP.NET framework dependencies remain outside Core.
DiagnosticSource may consequently be newer than the target .NET framework; the
propagation-only sampling test checks the actual loaded library version instead
of assuming the target framework determines it.

## Qualification

- Release build: zero warnings/errors.
- Full selected local regression: 3,436 passed, zero failed, 19 nonempty reports.
  This includes rollback, revisions, concurrent delivery, cancellation, routing,
  request/reply, resource/component cleanup, configuration and persistence suites.
- Nine packages packed; release tooling and package-version/dependency checks
  use the new manifest. Published-baseline tooling retains synthetic fixture
  coverage rather than inventing a published baseline for unpublished Core.
- An isolated package-only net8 consumer passed direct and runtime scenarios,
  health, durable delivery and SQLite recovery across process restart.
- An isolated observability consumer passed tracing, metrics and original-exception
  logging on net8/net10, including net10 SSE registration/startup.
- Both consumers resolved package libraries only; every FluxFlow dependency hash
  matched the fresh candidate feed. No retired Engine/HealthChecks package resolved.
- Git whitespace checks passed. No compatibility shim or old production project
  remains in the solution.

Evidence: artifacts/final-core-20260915/verification.json, qualification/,
build-final.log, consumer.log, observability.log and feed/. Initial failed attempts
remain separate from the final qualification reports.

Live SQL Server services and external telemetry collectors were not exercised;
SQL Server provider unit tests and local telemetry exporters were. Source/package
validation does not establish external consumer adoption or a production SLO.

This closes the agreed framework consolidation before redesigning components.
There is no pending internal migration from the removed projects. Component
redesign, external application adoption and publication remain separate work.

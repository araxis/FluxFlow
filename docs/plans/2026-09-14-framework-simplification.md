> Superseded target (2026-09-15): the user now requires only FluxFlow.Core.
> Optional modules are removed and will be added individually. See the
> [minimum-framework report](../reviews/2026-09-15-minimum-framework.md).

# Simplify the framework before rebuilding components

## Completed local consolidation

The final checkpoint has nine production packages and 27 solution projects.
Engine is incorporated in Core, HealthChecks in Hosting, and no forwarding
projects remain. Shared execution/lifecycle mechanisms and revision-specific
ownership preserve the existing features. Component activation and plan-factory
indirections are removed. Release build, 3,436 local tests, nine packages and
isolated runtime/persistence/observability consumers passed. See
[final closeout](../reviews/2026-09-15-consolidated-framework.md) and
artifacts/final-core-20260915/verification.json. Earlier proposed counts and next
steps below are chronological history, not pending work. Component redesign and
external adoption/publication remain outside this completed framework phase.

Latest checkpoint: [application lifetime comparison](../reviews/2026-09-15-application-lifetime-parity.md)
adds one shared graph scenario for Core and Engine. Both drain work and fan out
errors without losing later messages, but Engine Stop disposes revision components
while Core retains them until Dispose. Preserve this distinction during execution
convergence. All 536 Engine tests pass on net10.0; this test-only step changes no
production code or project counts (11 production, 29 solution).

The user has moved framework simplification ahead of the first replacement
component. The component-free checkpoint is `6b85b45f`; the complete former
implementation remains on local `main` at `6a659218`.

## Observed baseline

The solution contains 45 projects, of which 19 are production/package projects.
Counts below include C# source lines, excluding bin/obj and generated build output.

| Current boundary | Projects | C# lines |
| --- | ---: | ---: |
| Core | 1 | 6,007 |
| JSON definitions and composition | 2 | 6,177 |
| Mapping and resilience | 2 | 554 |
| Engine | 1 | 7,864 |
| Durable input/output and SQLite/SQL Server implementations | 6 | 15,260 |
| Hosting, logging, health, OpenTelemetry and ASP.NET Core | 5 | 584 |
| JSONata and C# expressions | 2 | 187 |

Before the structural step, the source tree repeated conceptual categories and
package names, for example `src/Engine/Runtime/FluxFlow.Engine` and
`src/Core/Application/FluxFlow.Composition`. Visual Studio repeated those layers.
That extra navigation does not itself establish a useful dependency boundary.

There are also substantive architectural questions:

- `FluxFlow.Core.FlowApplication` owns the directly constructed graph.
- `Composition/Definition/ApplicationConstructionExtensions.cs` constructs that
  Core graph from definitions and factories.
- `Engine/Hosting/ApplicationRuntimeAssembler.cs` uses its own preparation,
  activation and port runtime pipeline. It additionally supports transactional
  revisions and stable ports. These differences must be characterized before
  consolidating execution; moving their files alone would preserve complexity.
- Durable input and output use the same database families but have different
  delivery semantics. Package consolidation need not merge those state machines.
- Mapping has 201 lines and resilience 353 without external package dependencies.
  Their independent distribution is a candidate for removal, not a requirement
  inferred from their size alone.
- ASP.NET Core integration currently targets net10.0 and has an ASP.NET framework
  reference. Database, telemetry and expression integrations bring external
  dependencies. Their isolation has a concrete purpose.

## Proposed destination to evaluate

One public application model and one main framework package, with optional
integrations separated by actual external dependencies. A working target is
nine production projects rather than nineteen; this is not a numeric quota.

```text
src/
  FluxFlow.Core/                 application, workflows, messaging, execution,
                                definitions, registration, JSON, shared policies
  FluxFlow.Hosting/              host lifecycle, logging, health
  FluxFlow.OpenTelemetry/        OpenTelemetry SDK integration
  FluxFlow.AspNetCore/           HTTP/SSE integration
  FluxFlow.Expressions.Jsonata/
  FluxFlow.Expressions.CSharp/
  FluxFlow.Persistence/          durable input and output protocols/workers
  FluxFlow.Persistence.Sqlite/   SQLite implementation for both directions
  FluxFlow.Persistence.SqlServer/ SQL Server implementation for both directions
tests/
  ...                           shallow projects aligned to behavior/packages
benchmarks/
  FluxFlow.Benchmarks/
```

These are proposed final names, not implemented package/API renames. The main
framework consolidation would bring the existing Microsoft.Extensions dependencies
of Composition/Engine into Core. That is a real tradeoff to assess; keeping SDKs,
database drivers and ASP.NET outside Core remains important.

Within Core, organize around what developers use: Application, Workflows,
Messaging, Ports, Execution, Configuration, Diagnostics. Do not reproduce the
old project hierarchy as deeply nested internal folders. A folder does not need
its own interface, builder, service, namespace layer or assembly.

## Incremental implementation order

1. Flatten physical project paths and the solution hierarchy together. Update
   project references, release scripts, package inventory and tests; no runtime
   behavior or namespace changes in this mechanical step.
2. Consolidate the small dependency-free Mapping/Resilience boundaries into Core,
   then review JSON/Composition as configuration and registration responsibilities.
   Remove obsolete projects and package entries, not add forwarding packages.
3. Characterize the direct and Engine execution paths for the same workflow:
   bounded links, errors, request/reply, cancellation, disposal and diagnostics.
   Converge their shared execution responsibilities while retaining transactional
   update, stable-address and durability behavior. Only then settle the main
   public application API and remove redundant runtime layers.
4. Combine durable input/output distribution by provider while preserving their
   distinct delivery guarantees. Consolidate host/logging/health distribution;
   keep SDK and provider dependencies optional.
5. Review names and public API together, update docs and package-only consumers,
   and remove obsolete vocabulary and wrappers. Flatten the test solution as well;
   production package consolidation must not silently remove behavioral coverage.
6. Resume component design only after this framework pass has a verified baseline.

Each step must report structural/API changes separately from code actually
removed. A smaller project count is not evidence of a simpler execution design.
Preserve JSON readability and round trips, error-as-data with original exceptions
available to diagnostics, lazy values, tracing/logging/metrics, request/reply,
fanout/backpressure and existing persistence/update semantics.

## Structural step completed

43 project directories moved to the shallow layout. The solution retains all 45
projects but uses six folders rather than 38: src, tests, Unit, Integration,
Support and benchmarks. Project identities, namespaces, assembly names, versions,
target frameworks and all production C# source are unchanged. Internal source
folders were not redesigned in this mechanical project-layout step.

Project references and linked test fixtures, package inventory, build/release
scripts, CI paths and documentation links were updated. The public API baseline
is unchanged. A layout convention now requires production projects immediately
under src and test projects immediately under their kind.

Release build passed with zero warnings/errors; all 3,070 existing local regression
tests passed. 20 targeted checks also passed after adding the layout guard. All
19 packages packed, with no missing project references. External/licensed database
integration was not run. Evidence is under `artifacts/flat-layout-20260914/`.

The package-count reduction and runtime consolidation above remain future work.

## Mapping and Resilience consolidated into Core

The first package consolidation is implemented. The 17 production C# files
(554 lines) from Mapping and Resilience now live under Core/Mapping and
Core/Resilience, with unchanged namespaces and implementation. No new runtime
dependency, interface, wrapper or forwarding assembly was added.

The two old production projects and their two test projects were removed. All
helper tests moved to Core.Tests and now run on net8.0 as well as net10.0.
There are 17 production projects and 41 solution projects. Downstream references,
release planning and package acceptance now resolve these contracts through Core.

This is a package/assembly migration: consumers remove FluxFlow.Mapping and
FluxFlow.Resilience package references, reference Core and recompile. Namespace
compatibility does not imply compatibility with already compiled consumers.
The declaration baseline was reviewed for the type relocation into Core.

This step reduces distribution and project maintenance, not runtime code lines:
the mapping and retry behavior remains intact. The next bounded candidate is the
JSON/Composition configuration boundary; runtime convergence remains separate.

Verification: Release build passed without warnings/errors. The final local
regression evidence totals 3,095 passed tests (use regression-final reports with
the Release report replaced by release-final). All 17 packages packed. An isolated
package-only consumer executed mapping and retry on both net8.0 and net10.0 using
Core, with no project references or old helper packages. Evidence and counters:
`artifacts/core-helpers-20260914/verification.json`. Nothing was published.

## JSON and Composition consolidated into Core

The next package step moves JSON definitions/binding into Core/Configuration/Json
and composition, registration and definition construction into Core/Configuration.
The former namespaces remain. Both production projects and both test projects
are removed; their tests now run within Core.Tests on net8.0 and net10.0.
The solution contains 37 projects, including 15 production packages.

Core now references Microsoft.Extensions.DependencyInjection and its abstractions,
as Composition previously did. The redundant friend-assembly declarations for
Composition are removed. SDKs, expression implementations, ASP.NET and database
drivers remain outside Core. Existing consumers replace Json/Composition package
references with Core and recompile; no forwarding packages are provided.

Validation: Release build passed with zero warnings/errors; all 3,430 local tests
passed. All 15 packages packed. An isolated consumer referencing only Core built
and ran the same echo workflow through JSON binding and C# registration, including
addressed request/reply, on net8.0 and net10.0. The consumer resolved no project
references or old Json/Composition packages. Evidence:
`artifacts/core-configuration-20260914/verification.json`.

This is consolidation of distribution and ownership, not removal of all parallel
construction paths. Those APIs and runtime differences still need code review.
Remaining package target: hosting/logging/health 3 to 1 (15 to 13), durability
6 to 3 (13 to 10), then Core/Engine consolidation if behavior can be preserved
(10 to 9). Preserve the staged review of the execution model before that last step.

## Hosting and base Observability consolidated

Hosting now owns diagnostic logging and the caller-owned live event stream under
Diagnostics. The two implementation files are unchanged; the namespace remains
FluxFlow.Observability, but the types now ship in the Hosting assembly. Consumers
replace the old base Observability package with Hosting and rebuild. OpenTelemetry
and ASP.NET adapters reference Hosting. The separate Observability project and
its test project are removed; tests now run in Hosting.Tests on both frameworks.
There are 14 production packages and 35 solution projects.

Dependency review changed the order: HealthChecks currently references Engine.
Merging it now would add Engine transitively to standalone Hosting and telemetry.
Keep that boundary until Core/Engine convergence; no forwarding facade was added.
The remaining count target is persistence 6 to 3 (14 to 11), Core/Engine if viable
(11 to 10), then health into hosting once that dependency is no longer added (10
to 9). The target is subordinate to behavior and meaningful dependency boundaries.

Release build compiled successfully. NU1900 warnings recorded an unavailable
online NuGet vulnerability service; no compile errors occurred. All 3,432 local
tests passed, including a new assembly/dependency boundary guard on both target
frameworks. All 14 packages packed. An isolated package consumer verified logging,
tracing and metrics on net8.0/net10.0 and SSE on net10.0, resolving Hosting without
Engine or the retired Observability package. Candidate package hashes matched
the fresh feed. Evidence: `artifacts/hosting-consolidation-20260914/verification.json`.

## Persistence consolidated by provider

Completed: six durable input/output projects became Persistence,
Persistence.Sqlite and Persistence.SqlServer, with Input/Output folders and
unchanged namespaces. Six unit projects became three; external SQL Server
integration suites remain separate. There are now 11 production projects and
29 solution projects. All 62 production C# files are unchanged.

Release build passed without warnings/errors, 3,429 local tests passed, and all
11 packages packed. An isolated net8 package consumer passed candidate identity
checks and restart recovery. Live SQL Server integration was not run.
See [the report](../reviews/2026-09-14-persistence-consolidation.md) for scope and
evidence. No commit, push or publication occurred. Next: review Core/Engine
execution ownership, then revisit HealthChecks/Hosting; preserve behavior before
chasing the initial nine-project target.

## Core/Engine boundary: first shared execution step

Latest internal step (2026-09-15): reviewed the Core/Engine execution boundary.
Both already share Core delivery, route selection and dependency ordering; Engine
still owns transactional revisions and stable port generations. Four JSON,
Fluent, service-factory and Engine callers now use one internal
FlowMapContext.ForMessage method. No public API or project boundary changed;
there are still 11 production and 29 solution projects. Local regression passed
3,433 tests in 19 reports, including lazy-payload and evaluation-isolation checks
on both frameworks. Release build passed without warnings/errors. A new package
consumer and live SQL Server integration were not run for this internal change.
Evidence: artifacts/runtime-boundary-20260915/verification.json.
Next: simplify component construction/cleanup ownership, then characterize a
whole workflow under both runtimes before replacing either lifetime. See
docs/reviews/2026-09-15-core-engine-boundary.md. Do not remove revision or stable
port behavior merely to reach the nine-project target. No commit or publication.

## Component preparation indirection removed

Latest step: component preparation now invokes factories directly within its
existing cleanup boundary. ApplicationRuntimeComponentActivator (one internal
class/file) and its constructor dependency are removed; no replacement layer was
added. Partial-construction cleanup now has an explicit cancellation regression
as well as factory-failure coverage. All 3,434 local tests passed in 19 reports;
Release build passed with zero warnings/errors. Public API and project counts
remain unchanged (11 production, 29 solution). No new package consumer or external
database run. Evidence: artifacts/component-preparation-20260915/verification.json.
See docs/reviews/2026-09-15-component-preparation.md. Next: compare full workflow
construction and lifetime across Core/Engine before merging their ownership.
No commit, push or publication occurred.

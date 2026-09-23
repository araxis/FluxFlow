# Deep cleanup audit — 2026-09-22

## Scope and method

Reviewed the post-request/reply workspace before editing. The production inventory
contained 100 C# files / 7,413 physical lines. Searched source, tests, benchmarks and
package consumers for removed types, remaining helper consumers and custom state.
Checked project-reference paths and central package versions. Inspected composition,
registration, per-section configuration/link setup, request/reply, retry, addresses,
event encoding, diagnostics and lifecycle/routing responsibility boundaries.

The symbol inventory is a textual candidate finder, not a compiler proof of unused
public APIs. Extensions, converters, generated contexts and externally consumed
diagnostic records were inspected rather than deleted for low reference counts.
This is a consumer/boundary audit, not a claim to have proved every runtime behavior
or reviewed every line of every file. The existing large dirty branch was preserved;
counts below describe only this turn's cleanup.

## Removed with evidence

1. **Parallel custom retry engine in Core**: six files / 353 lines comprising
   RetryPolicy and backoff enum, RetrySchedule, RetryPlanner, RetryStateMachine and
   state/directive types, RetryExecutor, and jitter interface/implementation.
   RetryExecutor had no production consumer; references outside that subsystem were
   its own tests and a release assembly-location assertion. The actual Retry component
   constructs a Polly ResiliencePipeline with RetryStrategyOptions. It, its policies,
   request/reply ports, cancellation and output lineage were not edited.
2. **Unused JSON traversal method**: ApplicationAddress.TryReadJsonValue had no callers
   in source, tests, benchmarks or package consumers. The address class still builds,
   validates and resolves addresses. Resource registrars still bind their own DTOs.
3. **Redundant registration forwarding method**: AddComponentFactory now contains the
   existing implementation. Removed only its one-caller private RegisterFactory layer;
   native keyed singleton identity and the ownership guard are unchanged.
4. **Unused central version entries**: Configuration.Abstractions, Logging.Abstractions
   and Options had no direct PackageReference in repository projects. Removing their
   PackageVersion entries does not remove or ban ordinary transitive dependencies.
5. **Stale metadata/documentation/tests**: removed old Core retry README and tests,
   updated Core/package descriptions and Configuration adapter list, removed the
   obsolete retry assembly-location assertion while retaining the mapping assertion.
   Empty Core/test Resilience folders were removed. Historical reviews were preserved.

These are intentional removals of public development APIs. External callers of the
old Core retry types or TryReadJsonValue must migrate; no binary compatibility or
identical Polly semantics for arbitrary old RetryPolicy values is claimed. Required
runtime retry behavior remains in the existing component. No new production class,
manager, factory abstraction or dependency was added.

## Reviewed and retained

- Native keyed DI remains the identity/instance mechanism. No production
  BuildServiceProvider call or second provider was found in Core/Configuration.
  The temporary ServiceCollection stages registrations atomically; it is not a provider.
- DI discovery collections contain immutable names/addresses rather than a second
  block cache. The weak identity guard runs during construction and prevents double
  ownership; it is not single-threaded registration locking that can simply be removed.
- Link identity is stored in keyed descriptors with one unkeyed execution alias.
  Conditions keep per-provider compilation reuse, not a duplicate component catalog.
- FlowOutput/FlowRouter and the lifecycle helpers implement fanout, backpressure,
  reservations, completion and shutdown. Their state is not replaced by DI registration.
- Event helpers are available to component authors through EmitEvent; diagnostic
  payload records carry the original Exception to host observers. Low internal use
  of these public boundary types is not sufficient grounds for deletion.
- Existing components remain separate projects. No project was merged merely to
  reduce the Visual Studio project count. Project references resolved during inventory
  and the build/package checks below.

## Focused follow-up candidates

1. FlowDependencyOrder.TryCreate computes and sorts topological layers. Its only
   production caller, FlowApplication.Validate, only checks whether the result exists.
   Narrowing this to cycle detection can remove unused ordering work. Preserve cycle
   rejection, duplicate-edge handling and the exclusion of terminal/control inputs.
   This was identified after inspecting runtime consumers and not changed in this pass.
2. FlowApplication still slices workflow/component names from dotted strings in several
   routing/lifetime operations. Review centralizing those operations in the existing
   address contract without introducing an endpoint model or registration cache.
3. FlowEvent contains construction, JSON conversion and reading helpers in one file.
   Those APIs do have component-author consumers. Any simplification needs a separate
   behavior-preserving review of encoding, timestamps and structured attributes.

These findings do not justify reopening deferred UI, application request APIs, hot
reload, persistence or new configuration features during cleanup.

## Validation

- Full solution Release: **1,764 passed, zero failures**. This is 22 executions fewer
  than 1,786: the removed custom engine had 11 tests per target. Existing actual Retry,
  RateLimit, request/reply, routing and configuration tests remain and pass.
- Source public API baseline was intentionally updated and passed again in the full suite.
- Isolated Core and FluxFlow.Components.Resilience packages both restored and ran on
  .NET 8 and .NET 10. The latter exercises the actual Polly-based Retry component.
- Project inventory found no broken ProjectReference paths.

Evidence: artifacts/cleanup-review-20260920/deep-cleanup-inventory.json,
deep-cleanup-retry-before.json, deep-cleanup-api.log, deep-cleanup-tests.log,
deep-cleanup-core-package.log and deep-cleanup-retry-package.log.
No commit or publication was performed.

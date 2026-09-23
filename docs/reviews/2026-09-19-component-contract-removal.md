# Removal of component activation and instance wrappers

The requested stage is complete: ComponentActivationContext and ComponentInstance
are physically deleted. Their production consumers, tests, package consumers and
benchmarks now use native node instances and the existing DI composition path.
This supersedes the preceding lifetime-preparation checkpoint.

## Result

- A type registration receives IServiceCollection, the component address and its
  ComponentDefinition. It registers a factory and native port selectors without
  constructing the node. Ordinary composition and revision preparation both use
  BuildApplicationAsync to construct the application.
- ComponentFactory(IServiceProvider) remains synchronous. A standard
  Func<IServiceProvider, ValueTask<IFlowNode>> overload preserves asynchronous
  construction where needed. No Application, Workflow, activation context or
  factory cancellation parameter was added.
- FlowWorkflow stores actual nodes and registered native blocks. The compiler and
  revision port binder read that structure directly. ComponentInputPort,
  ComponentOutputPort and ComponentEventSource are deleted, as are the non-owning
  node view and unused ApplicationRuntimeCleanup class.
- Option binding and resource lookup are stateless extensions. There is no mutable
  activation context, replacement instance wrapper, new lifecycle manager or
  parallel construction loop. ApplicationSetup.ComponentFactories was renamed to
  ComponentRegistrations to describe the callbacks accurately.
- Revision providers retain dependencies registered by callbacks until node
  cleanup finishes. Host dependencies remain borrowed. Workflow snapshots register
  existing nodes and blocks without acquiring disposal ownership.
- Revision routing, generation replacement, rollback, native input fault
  propagation, accepted-work drain and first-failure handling remain supported.
  The existing application cleanup can dispose components after revision-owned
  shutdown without repeating ordinary application shutdown or starting its routes.
- Named event outputs still receive address headers and support reliable routing.
  The existing event bridge uses a native bounded BroadcastBlock for best-effort
  observation, including the latest event before listener attachment. It does not
  create a second component or make observation lossless.

## Scope and migration

This is a breaking development API change. Register factories and native selectors
with AddComponentFactory/AddInputPort/AddOutputPort/AddEventPort instead of returning
ComponentInstance or configuring ComponentActivationContext. The current example
and resource/processing instructions are in [component registration](../40-component-registration.md).

The optional FluxFlow.Configuration package still accepts IConfigurationSection;
Core's document/revision entry accepts ComponentDefinition. These are distinct input
adapters over the same native registration and construction methods, not a claim
that there is only one configuration-input API.

Processing capabilities are supplied explicitly to each BindConfiguration or
ResolveProcessing call. A manually authored registration owns consuming/validating
its Processing property; the old context's implicit post-construction fallback
validation is gone. Profile mapping, capability checks and explicit-property
precedence remain available and tested. Nodes continue to own their work and local
resources through Completion and DisposeAsync.

Compiler port metadata, endpoint routing and revision coordination still exist.
This stage does not claim to remove every large pre-existing class or all remaining
architectural complexity. No compatibility wrappers were introduced.

## Size and structure

Compared with the source snapshot taken immediately before this stage:

- Six production C# files deleted; two added; 21 existing files modified.
- Production C# decreased by 594 physical lines, from 20,071 to 19,477, including
  comments and blank lines, excluding generated bin/obj files.
- The two new files contain an eight-line registration delegate declaration and
  a 95-line stateless binding extension class.
- Existing FlowApplication grew 304 to 316 lines to share cleanup safely; the
  revision port binder shrank 145 to 49 lines. This is not merely moving the two
  removed classes into a new large class.

Line counts describe this stage only, not the entire branch's accumulated changes.
Source details: artifacts/component-contract-removal/source-summary.json.

## Verification

- Release solution build: zero warnings and errors.
- Full configured suite: 2,517 passed, zero failed/skipped, 29 TRX reports.
- Public API baseline updated and checked; tests also verify the removed public
  types are absent. No production references to those types remain.
- New regressions cover candidate dependency lifetime and latest-event observation
  before listener attachment. Existing revision, cancellation, rollback, bounded
  delivery, drain and multiple cleanup-failure tests pass on their configured targets.
- The previous checkpoint had 2,526 cases. Eight tests for impossible independent
  metadata/block mismatches and four duplicated registration cases were removed;
  three regression cases were added. No failing behavior test was excluded to
  manufacture a passing result.
- Fresh package-only Core consumer passed on net8.0 and net10.0, including revision
  rejection followed by delivery through the active revision. Candidate version:
  0.0.0-local.20260919185236.
- Fresh Configuration/Scheduler package-only consumer passed on net8.0 and net10.0.
  Candidate Core/Scheduler/Configuration version: 0.0.0-local.20260919185257.
- Whitespace/diff check passed. Benchmarks were migrated and built; no new
  performance measurements or speedup claims were made.

Evidence directory: artifacts/component-contract-removal/. Final build/API logs are
build.log and api.log; full regression is release-tests.log and release-tests/.
Package evidence is core-consumer.log/core-consumer/ and
configuration-consumer.log/configuration-consumer/. Earlier intermediate logs remain
as development evidence and are superseded by these final runs.

Changes remain local and uncommitted. No package publication, push or external
consumer adoption occurred. The two-class deletion has no remaining implementation
step; further endpoint/link or setup simplification is a separate review boundary.

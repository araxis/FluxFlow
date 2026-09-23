# Native keyed-DI composition: completed qualification

The composition refactoring is complete. Component authors implement native Dataflow
blocks; registration assigns addresses and factories in standard .NET DI. Application
resolves them, prepares links, starts sources and drains work. No second ordinary
component/port lookup model is required.

## Final structure and contract

FlowWorkflow no longer stores input/output dictionaries or provides manual Add/Register
methods. It is an 18-line structural container with actual components, event observations
and necessary routing declarations. Native keyed singleton registrations hold components
and native ports; read-only address/metadata registrations support enumeration without
duplicating block storage. Component selectors execute once. One host provider owns the
ordinary Application and its services; repeated Build composes once.

ApplicationResourceScope and CreateLinkServices are deleted. Manual Workflow construction,
manual hosting and all remaining test-only compatibility construction paths are removed.
AddWorkflowSnapshots and redundant per-workflow revision providers are removed. A real
isolated candidate provider remains for staged replacement/rollback, with borrowed host
fallback and native DI ownership. This is required update behavior, not ordinary lookup.

The disposable-port decision is explicit: registered ports expose separate native blocks
without IDisposable/IAsyncDisposable. A component owns those blocks' cleanup; DI owns the
component. Rejecting disposable aliases before DI captures them prevents double disposal
without a wrapper or custom disposal manager. Built-in components already follow this
shape. Custom components exposing themselves as disposable ports must expose their
underlying blocks instead. FlowOutput retains pattern-based DisposeAsync/await using.

Application stops/drains before provider teardown, including failed construction and
unactivated revision candidates. Standard DI disposes its services in native dependency
order and stops on its first disposal exception, as accepted by the user. No custom
aggregate-disposal guarantee is claimed. A reference-identity guard rejects reuse of a
component across keys/providers before DI captures it.

Required fanout, backpressure, conditional/fallback routes, rejection policies, errors,
event stamping, control completion, cancellation and revision rollback remain covered.
Resource/revision APIs not migrated by this work remain explicitly documented; there is
no claim that all revision or expression contracts have been redesigned.

## Consumer migration

- Register components and ports before building the provider with AddComponentFactory,
  AddInputPort/AddOutputPort/AddEventPort; register LinkFactory delegates separately.
- Use BuildApplicationAsync and StartAsync, or AddFluxFlow() for host-driven startup.
  AddFluxFlow(application), FlowExtensions.Workflow and manual Workflow registration are gone.
- Keep the provider alive through Application stop/drain, then dispose it. Failed Build
  also requires host provider teardown. Do not expect a fresh Application per Build.
- Use native keyed lookup in composition; no app.GetInput/SendAsync facade is restored.
- Component constructors are synchronous; asynchronous activation belongs in startup.

The public API baseline was reviewed and intentionally updated for these removals.
Current onboarding, node/package authoring, registration and package READMEs use this
model. The repository's earlier design reports remain historical, not current API guides.

## Final verification

Evidence: artifacts/native-keyed-di-revisions-20260920/.

| Check | Result |
| --- | --- |
| Release solution build | 0 warnings, 0 errors |
| Complete solution regression | 2,675 passed, 0 failed/skipped, 29 TRX reports |
| Core | 532 passed on each of net8/net10 |
| Nodes | 342 passed on each of net8/net10 |
| Configuration | 73 passed on each of net8/net10 |
| Engine/revision | 497 passed on net10 |
| Release/API/docs | 111 passed on net10 |
| Fresh Core package consumer | net8/net10 passed; 0.0.0-local.20260920090320 |
| Fresh eight-package Configuration consumer | net8/net10 passed; 0.0.0-local.20260920090617 |

The remaining test executions are component and expression suites. Logs are
final-solution-build.log, final-solution-tests.log, final-test-summary.json,
final-core-consumer.log and final-configuration-consumer.log. Package-only acceptance used
fresh directories/feed/cache and no project references. Core consumer evidence is under
artifacts/native-keyed-di-final-core-consumer/; Configuration under
artifacts/native-keyed-di-final-configuration-consumer/.

The next planned direction is a bounded component configuration adapter, such as Router
predicate setup using existing expression compilers. That is separate work; this goal
does not add new components or external integrations. No commit/push/publication.

## Historical migration evidence

The earlier sections below preserve actual failures and intermediate milestones.
Their pending-work statements are superseded by the completed qualification above.

## Ordinary hosting now uses the same native provider

`AddFluxFlow()` replaces `AddFluxFlow(application)`. The existing hosted service receives
the native provider and singleton Application, composes at startup, then starts sources.
It drains during Stop; DI owns disposal at provider teardown. No new host, executor or
lookup provider. Existing explicit Application factory registrations retain link capacity.
Conflicting ordinary/update hosting registrations still fail before mutation.

All 15 hosting cases now use native DI and cover lazy construction, fanout, lineage,
drain, cancellation, failed startup, failure propagation and provider ownership. Full
Engine regression passes 497 tests (`hosting-engine.log`). Node/port binding fixtures
and direct link scenarios also migrated; Core passes 527 per target at the binding
milestone (`native-binding.log`) and the four subsequent changed link scenarios pass
per target (`native-link-fixtures-final.log`). Solution Release build has zero warnings
or errors (`hosting-solution-build.log`); 15 documentation checks pass (`hosting-doc.log`).

`OutputProbe.Attach` and unused test-only To/From helpers are deleted. Capture nodes
are registered before provider construction. Remaining manual graph/revision test callers
and production Workflow port dictionaries still require migration/removal. The intentional
hosting API change is not yet accepted into the final API baseline; package checks follow
the architectural migration. No release qualification is claimed.

## Latest verification: Core fixture ownership migration

The document helper now returns a plain `(FlowApplication, ServiceProvider)` tuple.
Its callers keep the provider alive through application shutdown. This fixes the previous
test hang without changing production ownership or adding a wrapper. All 527 Core tests
pass on both net8 and net10 (`core-native-fixtures.log`). Routing passes 11 tests per
target (`routing-native-fixtures.log`). The earlier failures below are historical.

Updated scenarios verify that failed construction stops accepted nodes while provider
teardown performs disposal. If disposal throws, native DI stops there; the tests no longer
expect a custom aggregate cleanup across all services. Resource ordering assertions verify
each resource outlives its actual consumers, allowing unrelated lifetimes to interleave.
Repeated Build returns the same application; independent hosts use independent providers.

Document/composition output probes now register before building the provider through the
same component/port/link APIs as other nodes. They no longer add an unowned manual node
to an already composed DI application. Gate/native input/concurrent-delivery fixtures also
use native registration. One legacy hosting probe and other manual graph fixtures remain.

Next structural work is still removing Workflow port dictionaries and manual registration,
with their remaining callers. API baselines and fresh package validation remain pending.

## Subsequent structural step: one provider for links

CreateLinkServices and FlowJson.ApplyLinks(FlowApplication,...) are now physically removed.
The ordinary composition code contains no BuildServiceProvider call. LinkFactory receives
the original host provider. AddFlowWorkflow/AddComponentFactory register one native
FlowApplication singleton; its composition uses RunOnce so repeated Build returns the same
application without repeating construction or link declarations. Named event outputs
resolve their existing address-stamping bridge through native DI, even before composition.
DI owns bridges, components and resources; Application waits for bridge completion during
shutdown. GetComponent for DI applications now resolves the canonical keyed node.

The legacy manual test helpers no longer build a replacement provider: they access their
existing fixture blocks and prepare actual FlowLink routes directly. This is not completion
of manual caller migration. Workflow dictionaries and public manual registration remain.

Evidence in the same artifact directory:

- `no-port-provider-build.log`: complete Release solution build, zero warnings/errors.
- `no-port-provider-core-final.log`: 110 focused Core scenarios passed per target.
- `no-port-provider-engine.log`: all 497 Engine scenarios passed.
- `same-provider-configuration.log`: 73 Configuration scenarios passed per target.
- `core-bounded.log`: full Core diagnostic run aborted at the 45-second inactivity bound,
  after 381 passes and 29 failures per target. The active document scenarios all use a
  helper that disposes its provider before returning Application. Retaining that provider
  until after Application shutdown is the next required fixture migration. The preceding
  unbounded Core run was explicitly terminated, not treated as successful completion.

One earlier Engine run had a timing-sensitive termination assertion failure; eight focused
repeats and the subsequent full suite passed. The assertion now includes actual errors in
its diagnostic message. No production behavior was changed to suppress that assertion.
API baselines and package consumer qualification still await the completed migration.

## Ownership contract

The user explicitly accepts native .NET DI disposal behavior, including stopping on
the first disposal exception. The host owns its provider. Application stops/drains
components before host teardown; it does not dispose components created by DI.
Independent hosts have independent providers. Ordinary Build does not promise fresh
instances on repeated calls against the same provider.

## Changes

- Revision component factories now receive the existing revision snapshot's fallback
  view, so borrowed host dependencies remain available. The candidate's native provider
  still supplies singleton caching and disposal. No replacement provider or catalog.
- An unactivated/rejected candidate explicitly stops its sources and completes controls
  before awaiting completion. Deferring node disposal to DI exposed a hang here: the old
  cleanup sequence waited for work that nobody had stopped.
- Component reference identity is claimed before returning from its keyed singleton
  factory. A second key cannot register disposal ownership for an already claimed node.
- Native input/output selectors now execute only in their keyed singleton factories.
  Composition reads ordinary validation metadata and resolves those same blocks. The
  duplicate executable selector path is deleted; duplicate port keys reject during Add.
- Configuration and selected Core tests retain the host provider until after Application
  shutdown and explicitly check that disposal happens at provider teardown.

## Verification

Evidence is in `artifacts/native-keyed-di-revisions-20260920/`:

- `engine-ports.log`: 497 Engine tests passed on net10, including revision rollback,
  disposal, event routing and lifecycle scenarios. The initial hanging candidate test
  was isolated with a 45-second bound and then verified after the lifecycle repair.
- `configuration-ports.log`: 73 Configuration tests passed on each of net8 and net10.
- `ownership-fixtures.log`: 25 focused Core tests passed on each of net8 and net10,
  covering native singleton/port identity, duplicate capture prevention, native disposal
  behavior, events and canonical native port behavior.
- `core.log`: full Core run recorded 82 failures per target before the selected fixture
  migrations. This is retained as failure evidence, not presented as a passing regression.

The remaining Core fixtures include a document helper that disposes its provider before
returning Application, tests asserting disposal before provider teardown, repeated Build
tests expecting fresh instances, and scoped dependencies incompatible with singleton
components. Migrate their actual owners/callers. Manual post-Build component additions
also need removal; DI cannot own objects added outside its registrations.

Workflow port dictionaries, manual Register methods, named event selector setup and
CreateLinkServices still remain. Their removal, remaining callers, API baselines, complete
regression and fresh package consumers are the next required work. No package/release
claim is made for this partial migration.

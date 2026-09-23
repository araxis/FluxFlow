# Native keyed DI composition refactoring

## Goal

Complete ordinary application composition using standard .NET keyed DI and native Dataflow
blocks. Configuration defines Application / Workflows / Resources and therefore unique
addresses. Register construction and port access once, then resolve the actual component
or native block directly by address from the host's provider. Application coordinates
composition and execution; it does not implement a second object lookup system.

One host instance runs one Application. Use the existing host provider; ordinary Build
must not build a resource provider or temporary port provider. Constructors/factories are
synchronous. Startup, draining and cleanup may legitimately be asynchronous.

## Verified starting point

- Async component factories removed; ComponentFactory returns IFlowNode synchronously.
- Latest recorded verification: clean Release build; 2,649 tests passed; Core and
  Configuration package-only consumers passed net8/net10.
- ComponentRegistrationExtensions currently registers factory delegates and port selectors.
- FlowApplication.Composition invokes factories, fills Workflow input/output dictionaries,
  and copies existing instances to CreateLinkServices. ApplicationResourceScope creates
  another provider. These are remaining migration targets, not completed improvements.
- Existing runtime routing, completion and revision code consumes those dictionaries too.
  Removing lookup does not authorize deleting their execution behavior.

## Execution rules

Approved ownership decision (2026-09-20): after an executable proof on net8/net10 showed
that native DI stops disposal on the first exception, the user explicitly chose standard
DI behavior and DI as the sole disposal owner. Do not reintroduce an all-services disposal
manager to preserve the previous aggregation contract. Application still stops/drains
before the host disposes the provider; disposal exceptions propagate to the host.

Work in small coherent changes; validate each changed behavior before proceeding. Keep
existing uncommitted work. Record actual evidence and remaining work after each milestone.
Do not stop for routine approval. Do not treat helper renaming, partial files or moving a
dictionary into a new class as architectural completion. No custom catalog, service locator
wrapper, instance cache, executor or activation context. Components receive explicit
dependencies, never the container. Plain registration extensions are appropriate.

## Steps and exit criteria

### 1. Establish native DI identity and ownership behavior

Inspect current lifecycle and revision consumers. Verify native keyed singleton behavior
with a focused executable test: registration is lazy, concurrent resolution creates one
node, input/output selectors return that node's actual blocks, and keys do not collide.
Cover disposable aliases and disposal failures as well as ordinary success. Decide one
owner for every disposable object; do not rely on two owners calling an idempotent method.
Keep application stop/drain before provider disposal. Failed composition must stop any
started work and preserve the initiating error. Document who tears down the host provider.

Exit: concrete tested registration/ownership model, including failure behavior; no
speculative infrastructure or silent loss of cleanup guarantees.

### 2. Register and resolve component instances through native DI

Adapt registration helpers so native keyed singleton registrations construct components.
Application resolves the registered instances when composing; it does not call factories
and put the results in a second lookup cache. Configuration reading creates no components.
Keep unique-address validation and protection against sharing a node under multiple keys.
Migrate ordinary resource registrations into the same service collection transactionally.
Remove the ordinary separate resource-provider path when no longer used.

Exit: repeated keyed resolution returns the same node; dependencies come from the host
provider; component/resource ownership and cleanup tests match the new documented model.

### 3. Resolve ports and make links from that same provider

Register native ITargetBlock/ISourceBlock access under full port addresses during Add.
Port resolution references the canonical keyed component. LinkFactory uses that provider.
Remove Workflow port dictionaries, RegisterInput/Output composition calls and
CreateLinkServices. Do not replace them with equivalent dictionaries elsewhere.
Keep only necessary routing declarations/execution state; explicitly distinguish them
from object lookup. Enumeration may use ordinary registered address data where required;
it must not duplicate component or block identity storage.

Exit: direct GetRequiredKeyedService for a component or native port works without traversing
Application/Workflow, and ordinary Build constructs no additional ServiceProvider.

### 4. Migrate execution and revision consumers

Update routing validation, input completion observation, control classification, events,
drain planning and revision binding to the canonical registrations. Preserve fanout,
backpressure, conditions, fallback, error/event delivery, source/control shutdown order,
cancellation and failed-revision rollback.
Revision replacement currently has isolated candidates and providers. Audit their actual
purpose separately: one provider for ordinary composition does not silently erase revision
isolation. Reuse the same registration contracts, remove redundant lookup snapshots, and
document any remaining provider that has a demonstrated revision-lifetime responsibility.

Exit: existing behavioral scenarios pass through the new model, with no parallel legacy
ordinary construction path. Any genuine conflict with required behavior is reported with
evidence before choosing to remove behavior.

### 5. Remove obsolete surface and qualify the result

Delete unused APIs/classes/overloads and migrate samples, test fixtures and consumers.
Tests must not restore removed production architecture behind compatibility adapters.
Update public API baselines after reviewing intentional changes. Update AGENTS.md,
CONTINUE-HERE.md, current documentation and the migration report. Historical reports remain
historical. Run Release build, appropriate complete regression and fresh package-only Core
and Configuration consumers for net8/net10, retaining evidence.

Exit: all acceptance criteria below hold, with no unexplained failing tests.

## Definition of done

- One ordinary host provider; no ordinary resource/port provider constructed during Build.
- Native keyed DI is the sole component/port lookup mechanism by unique address.
- Synchronous lazy construction and consistent instance identity are verified.
- No Workflow port dictionaries or replacement lookup wrappers/caches.
- Exactly-once disposal ownership and correct stop/drain order, including failure cases.
- Required routing, events, errors, cancellation and revision behavior preserved.
- Consumers and documentation use the simpler model; obsolete paths physically removed.
- Build, regression and package validation pass. Report structural changes and limitations
  honestly; line count alone is not a success criterion.

## Goal prompt

Execute this plan in the current FluxFlow checkout without resetting existing work. Use
standard .NET keyed DI and native Dataflow capabilities instead of custom lookup layers.
Complete short, verified steps until the definition of done is met. Start with an ownership
and identity proof, then migrate real composition and remove superseded paths. Preserve
required behavior and explain evidence-based conflicts rather than agreeing mechanically.
Do not ask for routine approvals. Do not add external integrations, new components or
speculative abstractions. Do not publish, push, merge or modify other projects. Keep the
goal active until the architecture, callers, documentation and validation are complete.

## Progress

**Complete — 2026-09-20. All five steps and the definition of done are verified.**

Workflow port dictionaries/manual APIs and their remaining fixtures are removed.
Routing, completion observation, native control classification, link validation and
revision binding enumerate registered DI address/metadata data and resolve canonical
native blocks. FlowWorkflow retains structural components and routing declarations,
not port lookup. Redundant per-workflow snapshot providers are removed; real isolated
revision candidates retain their ownership/rollback provider. No replacement model.

The user delegated the disposable-port choice: registered ports must be non-disposable
native blocks owned by their component. Reject IDisposable/IAsyncDisposable aliases before
DI captures them, with focused ownership tests. Native DI alone owns the component.
Current docs, public API baseline, package consumers and AGENTS.md are updated.

Final evidence under artifacts/native-keyed-di-revisions-20260920/:

- final-solution-build.log: Release, zero warnings/errors.
- final-solution-tests.log and final-solution-tests/: 2,675 passed, zero failed/skipped,
  29 reports; Core 532 each, Nodes 342 each, Configuration 73 each, Engine 497, Release 111.
- final-core-consumer.log: fresh Core package 0.0.0-local.20260920090320, net8/net10 pass.
- final-configuration-consumer.log: fresh eight-package candidate
  0.0.0-local.20260920090617, net8/net10 pass.

The final run supersedes earlier intentionally failing/partial migration runs below.
No commit, push, merge or publication. See the review and CONTINUE-HERE.md for continuation.

### Historical milestone evidence

- Ordinary hosting migrated: AddFluxFlow() composes the registered singleton application
  at host startup using the same native provider, then starts sources. The overload
  taking a manually built application is removed. All hosting fixtures migrated; full
  Engine suite passes 497 tests. Capture-node Attach and test-only To/From adapters
  removed. NodeRegistration, StandardComponentBinding, ComponentOwnership, DirectPort
  and FlowExtensions cases now use native registrations instead of manual Workflow APIs.
  Release solution build clean. Core binding milestone 527 passes per target; subsequent
  four link cases pass per target; documentation boundary suite 15 passes. Evidence
  hosting-solution-build.log, hosting-engine.log, native-binding.log,
  native-link-fixtures-final.log and hosting-doc.log in the revision artifact folder.
  Remaining manual graph/revision fixtures, Workflow dictionaries and final API/package
  qualification still require completion; no architecture completion is claimed.
- Full Core fixture migration now passes 527 tests on each of net8/net10. The document
  helper retains its provider explicitly through application shutdown; disposal, lazy
  registration and singleton assertions now match the approved DI contract. Native DI
  stops at the first disposal exception; tests retain original processing/build errors
  separately. Resource cleanup assertions follow actual dependency construction order.
  Core document/composition capture nodes now use ordinary pre-Build DI registration,
  removing unowned manual post-Build additions. Gate/native control/concurrent-delivery
  tests also use native registration; Routing passes 11 per target. Evidence:
  core-native-fixtures.log, routing-native-fixtures.log in the revision artifact folder.
  No production port dictionary removal is claimed: remaining manual graph/hosting
  callers must migrate, followed by Workflow registration removal and final qualification.
- Ordinary temporary port provider physically removed: CreateLinkServices and the unused
  FlowJson.ApplyLinks(FlowApplication,...) overload are deleted. LinkFactory receives the
  original host provider. FlowApplication is a native singleton and composes once via the
  existing RunOnce guard; repeated Build returns the same application. Native event bridge
  registrations expose stamped named source blocks directly from DI, including before Build.
  DI owns bridges; Application observes completion. DI GetComponent uses keyed resolution.
- Port discovery is plain read-only address data; routing delegates retain the existing
  mutation/identity/fallback checks. Workflow port dictionaries and manual registration are
  still pending and are not claimed removed. Legacy manual test routes use real FlowLink
  directly, with no replacement test-only provider or catalog.
- Validation for this structural slice: 110 focused Core tests per target; Engine 497 pass;
  Configuration 73 per target. Evidence no-port-provider-*.log and same-provider-configuration.log
  under artifacts/native-keyed-di-revisions-20260920/. Full Core diagnostic run aborted after
  381 passes/29 failures per target because DocumentApplicationFixture disposes its provider
  before returning Application. Fix host lifetime in that helper/callers next; do not restore
  Application ownership or an extra provider. API/package qualification remains pending.
- Revision ownership repair verified: borrowed host dependencies reach native singleton
  factories through the existing revision snapshot; unactivated candidates stop and drain
  before provider disposal. Engine suite passes all 497 scenarios.
- Identity claim moved before factory return to native DI. Reusing a node under another
  key cannot make DI capture its disposal again. Tests cover pre-composition resolution.
- Removed duplicate executable input/output selector registrations. Plain existing port
  metadata supports enumeration; composition resolves native block singletons. Selector
  invocation is lazy and exactly once; duplicate directional keys reject transactionally.
  Workflow port dictionaries, named event selectors and temporary link provider still remain.
- Configuration lifetime tests now use one Application per provider; independent-host tests
  create independent providers. Failed Build stops nodes, host teardown disposes them.
  Configuration passes 73 cases on each target. Focused Core ownership, event and native-port
  fixtures pass 25 cases on each target (net8/net10). Core full run before the latter fixture
  repairs had 82 failures per target; it is not qualified yet. DocumentApplicationFixture's
  prematurely disposed provider and remaining manual/per-Build/scoped ownership assumptions
  must be migrated. Do not restore old ownership to satisfy those obsolete expectations.
  Evidence: artifacts/native-keyed-di-revisions-20260920/. API and packages pending.
- Preparation complete: async factory removal, verified in the preceding step.
- Native DI proof complete: concurrent singleton identity, actual native port identity,
  duplicate disposal through aliases, and stop-on-first-dispose-failure verified net8/net10.
- Migration in progress: AddComponentFactory registers a native keyed IFlowNode singleton;
  Application resolves it and no longer disposes DI-created nodes. Manual composition
  still owns manually added nodes until its callers are migrated.
- AddApplicationResources now stages native descriptors directly into the host collection.
  ApplicationResourceScope and the extra resource provider are physically removed.
- Direct keyed input/output block registration added; Workflow dictionaries and the
  temporary link provider remain and must be removed, not relabeled.
- FlowOutput retains pattern-based DisposeAsync/await using but no longer implements
  IAsyncDisposable, preventing DI from owning a component-owned port as well. Component
  lifetime keeps explicit typed cleanup delegates for its outputs. No port wrapper added.
- NativeDiOwnershipTests and ResourceRegistrationStageTests pass (10 executions); resource
  configuration cases pass (18 executions). Build clean. Full migration regression is
  intentionally not green yet: legacy per-Build/resource ownership expectations and
  consumers still require migration; API baseline is not accepted for unfinished work.
- Next: finish port/address metadata migration and remove CreateLinkServices/Workflow
  lookup dictionaries; migrate all lifetime tests/revision dependencies to actual DI ownership.
  Evidence: artifacts/native-keyed-di-20260920/. No package claim for this unfinished step.

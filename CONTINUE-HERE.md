# Continue this project on another machine

## Latest typed configuration values (2026-09-22)

Implemented specialized OneOrManyLinks and data-only LinkConfiguration. Existing
strict parser moved out of DI registration; LinkRegistration keeps native deduplication.
Bundled adapters use ReadConfiguration<T> to bind top-level typed ports and standard
settings in one call. No generic union/binder system, DTO catalog or Core changes.
ExpressionSource changed from static utility into a TypeConverter-backed value;
Mapper/Router now expose that type and accept explicit UTF-8 base64: in addition to
the existing inline/file forms. Resolution remains at Build. Link conditions stay
inline. Full suite: 1,850 Release executions passed; Configuration 176 per target.
API baseline updated; isolated Configuration/engine consumer passed .NET 8/10.
See docs/reviews/2026-09-22-configuration-value-types.md for
migration and the deliberate setup-only repeated parsing tradeoff. No commit/publication.


## Latest expression registration naming (2026-09-22)

Implemented approved AddJsonataExpressionEngine(useAsDefault: true) and matching
AddCSharpExpressionEngine; flag is optional/false by default. Old plural registration
names are removed, all current callers/docs migrated. Default uses the existing
unkeyed DI path for omitted link Engine, with the same built-in engine instance;
explicit keys, required Mapper/Router Engine and compilation behavior are unchanged.
No new model/registry/Core change. Multiple explicit defaults follow native DI order.
Full Release suite: 1,800 passed. Isolated Configuration consumer (including both
engine packages and a real JSONata default condition) passed net8/net10. Public API
baseline updated. See docs/reviews/2026-09-22-expression-engine-registration.md.
ApplicationConditionCompiler was reviewed but not renamed in this step.
No commit/publication.

## Latest Core.Configuration removal (2026-09-22)

Applied the user's approved responsibility split. CompositionConfigurationException
moved into FluxFlow.Configuration. ResourceRegistrationExtensions (formerly
FluxFlowServiceCollectionExtensions) and FlowResourceExtensions are in Core.Application.
Removed Core.Configuration including its README and empty directories; Core now has
47 C# files. No runtime behavior/dependency changes or compatibility facades.
All source/test/consumer imports and the API baseline reflect the breaking namespace/
type location changes. Updated current docs with migration instructions.
All 1,782 cases pass after rerunning Release tests: the first solution run exposed
one obsolete test scanning the deleted folder; it now verifies compiled assembly
friend declarations. Core/Configuration isolated package consumers pass .NET 8/10.
See docs/reviews/2026-09-22-core-configuration-boundary.md. No commit/publication.

## Latest Core boundary review and event cleanup (2026-09-22)

Completed the three remaining scoped audit questions. Event reading no longer creates
temporary messages; serialization avoids intermediate FlowValue wrappers and reuses
private JSON options. Existing API/encoding/timestamp/snapshot behavior is retained.
Keep native hosting, shared expression contracts and runtime diagnostics in Core;
their actual consumers and package tradeoffs are documented, with no new layers.
Corrected stale Core README update-host/revision/document-builder statements.
Full Release: 1,782 passed; isolated Core package passed net8/net10. API baseline
changed only because an unchanged signature was put on one line, allowing the source
scanner to discover three already-existing methods instead of absorbing their bodies.
See docs/reviews/2026-09-22-core-boundary-and-events.md. Core remains 48 C# files.
This scoped cleanup is complete; continue concrete setup/component work rather than
an open-ended Core redesign. No commit/publication. Older next-step candidates below
are historical and superseded by this completed review.

## Latest address cleanup (2026-09-22)

Completed the address-slicing follow-up. ApplicationAddress now owns WorkflowName,
ComponentName, PortName and ComponentAddress extraction; Application/Configuration
consume these properties. Value remains the canonical DI key. No new model/cache,
registration path or component behavior. IConfiguration path splitting stays in
Configuration. All 1,778 Release executions passed; isolated Core and Configuration
consumers passed on .NET 8/10. API baseline updated for four additive properties.
See docs/reviews/2026-09-22-address-parts.md. No commit/publication.
Continue consumer-based review; FlowEvent's author-facing helpers remain an audit
candidate, not approved unused code. Do not remove error/diagnostic behavior to
shorten that class.

## Latest cycle detection simplification (2026-09-22)

Completed the first follow-up from the deep cleanup audit. Internal FlowCycleDetection
replaces FlowDependencyOrder and returns a bool rather than unused sorted layers.
Application.Validate calls it directly. Business-cycle rejection and terminal/control
link handling are unchanged; all 1,764 Release test executions pass. Public API baseline
passed without an update. Corrected the stale final Core README section that still
described removed runtime revisions. See docs/reviews/2026-09-22-cycle-detection.md.
Next reviewed candidate remains repeated address slicing in Application; do not add
an endpoint model or custom lookup cache. No commit/publication.

## Latest deep cleanup audit (2026-09-22)

Removed Core.Resilience: six production files implementing a custom retry engine
with no production consumers. The real Retry component uses Polly and is unchanged.
Removed unused ApplicationAddress.TryReadJsonValue, a private registration forwarding
method, three unused central package-version entries, old retry-only tests and stale
package metadata/docs. No replacement production abstraction was added.
Full suite: 1,764 Release tests passed; isolated Core and Retry packages passed .NET 8/10.
See docs/reviews/2026-09-22-deep-cleanup-audit.md for evidence and intentional API removals.
Next focused candidate: FlowDependencyOrder computes/sorts layers that its only runtime
caller uses solely to detect cycles. Review narrowing it to cycle validation while
preserving cyclic-graph rejection and terminal/control link handling. Address slicing
in Application is another candidate for centralization; no new address API was added.
These are review findings, not permission to remove lifecycle, fanout or diagnostics.
No commit/publication. Older preservation notes below are superseded by current code.

## Latest request/reply simplification (2026-09-22)

Replaced the generic PendingExchangeCoordinator layer and its public result/options
models with internal PendingRequests. FlowRequestReply keeps all public signatures;
Retry/RateLimit and tracing remain unchanged. No RabbitMQ relationship. One timer,
bounded pending requests, deadlines, cancellation, send draining and failure cleanup
remain. Removed settled history, unused generic context/outcome APIs and FaultAll.
Coordination production code: 7 files / 604 lines -> 2 files / 249 lines. Runtime
regressions use FlowRequestReply; generic-only contracts no longer have tests.
Full suite passes 1,786 Release executions. Isolated Core consumer passes .NET 8/10.
See docs/reviews/2026-09-22-request-reply-simplification.md for migration and evidence.
No commit/publication. Continue actual consumer-based review; do not recreate the
generic layer or remove runtime locking as if it were single-threaded registration.

## Latest processing-layer removal (2026-09-22)

Removed Core.Configuration.Processing entirely and its automatic processing.profile
registration. No adapter used ResolveProcessing. Removed seven production files,
one obsolete compatibility XML, profile-only tests and the package example. Generic
resource tests now use local DTOs and retain address/binding/validation/ownership checks.
Unused direct Core Logging.Abstractions/Options dependencies removed. Current guides
and API baseline updated. Full suite: 1,804 Release executions pass on .NET 8/10.
See docs/reviews/2026-09-22-processing-profile-removal.md for scope and evidence.
Older preservation notes below are historical. Continue consumer-based review of
actual responsibilities; do not recreate the profile layer, rename it or relocate it.

## Latest keyed link registration (2026-09-22)

Removed the temporary link dictionary. RegisterPort reads a port section and registers
the canonical keyed LinkFactory plus one unkeyed execution alias. DI descriptors are
used to skip identical declarations and reject delivery/fallback conflicts. LinkKey
uses record value equality; parameter signature semantics remain unchanged. Application
continues executing only unkeyed LinkFactory services. See
docs/reviews/2026-09-22-keyed-link-registration.md.
Next planned design review remains ChangeFilter/Deduplicate configuration keys and
equality. Do not add workflow-group aliases without a consumer requirement.

## Latest Batch setup (2026-09-22)

Added BatchConfiguration/AddBatch under Configuration/Batching, with Input, Output
and named Events. Required positive BatchSize/MaxWait, positive OutputCapacity=128;
settings snapshot at Add, host TimeProvider resolved lazily at Build. Core and Batch
behavior unchanged. Repeated Build preserves pending batch state. See
docs/reviews/2026-09-22-batch-configuration.md for validation and package evidence.
Next: review ChangeFilter/Deduplicate key types and equality before adding their
configuration adapters; do not invent implicit JSON equality or alter component logic.

## Latest documentation cleanup (2026-09-22)

Current resource/registration guides no longer claim the removed document APIs exist.
Source guidance correctly states repeated Build preserves the singleton and cursor.
Repository/Core folder descriptions and Configuration dependency lists are corrected.
docs/README.md now provides a focused current reading list with a separate historical
section. Historical guides/reports were not rewritten. See
docs/reviews/2026-09-22-current-documentation-cleanup.md.
This step changes documentation only; the latest runtime suite remains 1,796 passing
Release executions from the direct-link cleanup. Next: review remaining component
setup coverage and choose the next adapter, rather than continue deleting abstractions
without a concrete unused responsibility or duplicated behavior.

## Latest direct link sections (2026-09-22)

Completed direct IConfigurationSection link parsing; removed ReadLinkValue.
Parameters retain their existing JSON conversion and registration-time snapshots.
No runtime/public API changes or replacement model. See
docs/reviews/2026-09-22-direct-link-sections.md for evidence.
Next: review current documentation for remaining references to removed document
APIs, especially Configuration README resource guidance. Keep historical reports
historical; do not restart Application redesign or add setup features in cleanup.

## Latest native registration cleanup (2026-09-22)

Deleted unused parallel raw-block component/port/signal registrations and all three
NonOwningDataflowViews wrappers. Resource registration APIs remain; native application
component registration is unchanged. No replacement abstraction. Public baseline updated.
All 1,784 full-solution Release test executions passed.
See docs/reviews/2026-09-22-native-registration-cleanup.md for verification. No commit.
Next review candidates: direct section reading for links instead of the local JSON
conversion, then remaining historical/current documentation clarity. Do not assume
these justify changing runtime behavior or another Application redesign.

## Latest boundary — intermediate document path removed (2026-09-21)

User explicitly removed the entire intermediate definition model and dependencies.
This supersedes older requests to retain explicit document APIs or DocumentValidation.
All four document definitions, DocumentValidation/canonical helpers, document JSON
serializer/reader, FlowJson and document diagnostic compiler/projection APIs are gone.
Ordinary setup traverses sections and binds per-type DTOs. Internal per-port parsing
returns LinkFactory delegates directly; condition/parameter helpers now live in
FluxFlow.Configuration/Links. No replacement model, catalog or editor metadata layer.

Runtime parity tests migrated to Configuration; native delivery tests remain in Core.
Obsolete document-only tests and compiler benchmark removed. Public API baseline and
current docs updated. All 1,784 full-solution Release test executions passed. Core and
Configuration isolated packages both pass on .NET 8/10. Removed Configuration friend
access to Core; the final dependent build passed with zero warnings/errors.
See docs/reviews/2026-09-21-intermediate-document-removal.md for final suite evidence.
No commit/publication. Continue from this boundary, not older document-preservation notes.

## Latest property-order removal (2026-09-21)

User explicitly removed property-order tracking from Core; UI metadata is a future,
separate concern. PropertyOrder and serializer capture/replay are removed from all
three document models. Empty Resources are omitted, nonempty Resources precede
Workflows/components, and component Type precedes settings. No replacement metadata
layer; DocumentValidation unchanged. Core 834 and Configuration 224 Release test
executions pass on .NET 8/10. See docs/reviews/2026-09-21-property-order-removal.md.
No commit/publication.

## Latest section port discovery cleanup (2026-09-21)

Configuration now uses existing DI port-address registrations instead of treating
all keyed native block services as component ports. No new metadata/registry/API.
Two reproduced host-service regressions are fixed; all 224 Configuration test
executions passed on .NET 8/10. See docs/reviews/2026-09-21-port-discovery-cleanup.md.
DocumentValidation is explicitly deferred by the user; leave it unchanged for now.
No commit/publication. Continue evidence-based cleanup without inventing a new layer.

## Latest local expression/resource configuration (2026-09-21)

Workflow/application ExpressionEngine is removed. Links now declare Engine alongside
Condition; Mapper/Router keep their local Engine setting. Keys remain extensible strings.
ResourceInstanceDefinition is deleted; resource adapters bind their own DTOs directly,
while ResourceDefinition only stores raw JSON for explicit document serialization.
DefinitionRules became DocumentValidation with address checks delegated to ApplicationAddress;
unused canonical lookup helpers were deleted. No intermediate setup graph reintroduced.
All 2,014 Release test executions passed, and API baseline is updated. See
docs/reviews/2026-09-21-local-expression-and-resource-config.md for migration and evidence.
No commit/publication. No further architecture replacement is assumed; keep ordinary
section registration separate from explicit document serialization/diagnostics.

## Latest condition compiler boundary (2026-09-21)

Removed ApplicationConditionCompiler's document field, document constructor and
address-based Compile overload. Document consumers now choose the source engine before
calling shared compilation; ordinary section setup is unchanged. No new class/public API.
All 2,000 Release tests pass, including scoped engine selection, parameters, caching,
failed-compilation diagnostics and direct section registration. See
docs/reviews/2026-09-21-condition-compiler-boundary.md. No commit/publication.
Next: review the remaining explicit document API as its own feature boundary; do not
restore a document model into ordinary setup or remove serialization/diagnostics blindly.

## Latest processing cleanup (2026-09-21)

Removed document-based BindConfiguration/ResolveProcessing and JSON options injection.
ProcessingProfileExtensions.ResolveProcessing(IServiceProvider, reference,
componentAddress, capabilities) retains profile mapping and validation independently
of ComponentDefinition. Optional use and explicit-option precedence belong to adapters.
Full suite passed 2,000 Release executions; Core package consumer exercises direct profile
resolution on .NET 8/10. See docs/reviews/2026-09-21-direct-processing-profiles.md.
Next candidate: separate the shared condition compiler from its retained document-model
selection overload; preserve independent document diagnostics and compile-session reuse.

## Latest resource helper cleanup (2026-09-21)

Three unused document-based resource APIs are removed: GetResource, GetRequiredResource,
AddProcessingProfile. FlowResourceExtensions now contains only the unchanged direct DI
GetRequiredFlowResource extension. No replacement resource model/context was introduced.
Resource addressing tests now use native DI, preserving relative/full addresses,
workflow/application/host precedence and missing-resource failures. Full suite: 1,998
successful Release executions. See docs/reviews/2026-09-21-resource-helper-cleanup.md.
Next candidate: audit remaining definition-based option/processing binding consumers;
do not conflate independent document serialization/diagnostics with ordinary setup.

## Latest setup boundary — direct IConfiguration (2026-09-21)

AddFluxFlowApplicationAsync now reads sections directly and registers component,
resource and link factories without constructing a document model. ResourceRegistration
moved from Core to Configuration and now takes IConfigurationSection. Registrars capture
their settings during Add; Build resolves native DI services without rereading configuration.
Only declared link values use the shared JSON link grammar; there is no root conversion.
Explicit document serialization/diagnostics APIs still retain their data model.
See docs/reviews/2026-09-21-direct-configuration-registration.md for boundary details,
intentional preview API change and evidence. Build is clean; all 2,004 Release test
executions and both isolated Core/Configuration package consumers (.NET 8/10) passed.
No commit or publication. Next: assess the independent document API boundary before
moving/removing its model; do not restore it as a prerequisite for ordinary setup.

## Latest link registration — per component section (2026-09-21)

DocumentLinks is removed. AddFluxFlowApplicationAsync parses each component's
registered port properties in the existing section loop and registers LinkFactory
instances immediately. Build resolves addresses/compiles conditions; it never scans
the application document to discover ordinary configuration links. The existing
parser and condition compiler are shared internally with Configuration. No new
builder/catalog/context class. Registration-time duplicate checks are transactional.
The explicit public FlowJson.ApplyLinks document API remains for direct callers only;
normal setup does not call it. Keep both input/output directions, arrays, named ports,
relative/full addresses, source-workflow engines, bound parameters, delivery/fallback,
and one compilation session per provider. See
 docs/reviews/2026-09-21-section-link-registration.md.
Validation: 1,994 Release tests passed; zero build warnings/errors; isolated
Configuration package consumer passed on .NET 8/10. No commit/publication.


## Latest structure cleanup — Core folders (2026-09-21)

Twelve files moved with matching namespaces; no behavior change. Models now live in
FluxFlow.Core.Configuration.Model; ApplicationDefinitionJson and its converter in
FluxFlow.Core.Configuration.Json. FlowLink/FlowDeliveryPolicy are in Core.Execution,
ComponentActivityBridge in Core.Application, CompositionConfigurationException directly
in Core.Configuration. Eight empty legacy directories removed. Do not recreate old
namespace wrappers. SDK project inclusion exposes the same folders in Visual Studio.
See docs/reviews/2026-09-21-core-folder-simplification.md for migration and evidence.


## Latest audit — unused definition registration removed (2026-09-21)

Core.Configuration.Registration.ComponentRegistration and its AddComponent extension
are removed: no production consumers remained after authored-builder removal.
Unused FactoryRegistrationFixture and TestSink definition helpers are also removed.
Only Configuration.ComponentRegistration(IServiceCollection, IConfigurationSection)
remains for configuration-driven setup; native component factories are unchanged.
This supersedes earlier notes about retaining both registration delegates.
ApplicationLinkCompiler remains deliberately: it returns independent located diagnostics
and compiled declarations; runtime DocumentLinks applies routes and throws instead.
Do not equate these contracts or delete diagnostic behavior solely for lack of a runtime caller.
See docs/reviews/2026-09-21-registration-and-link-consumers.md.
Validation: 1,986 Release tests pass, build has zero warnings/errors, API baseline updated,
and isolated Core package consumer passes on .NET 8/10. No commit/publication.


## Latest cleanup — authored definition builders removed

The user explicitly approved deleting the C# builder/scope/handle layer, not moving
it. ApplicationDefinitionBuilder, WorkflowDefinitionBuilder, DefinitionScope,
component/port/resource handles, ApplicationSetup, its resource contracts and
ApplicationLinkDefinition are removed (13 production files). Do not restore them.
This supersedes older instructions retaining ApplicationSetup or authored contracts.
ApplicationDefinition/WorkflowDefinition/ComponentDefinition remain plain document
data. Configuration registers factories/selectors in DI; Build resolves them.
Native predicates use LinkFactory and ports.Link/LinkFallback directly.
FlowJson.ApplyLinks accepts only the document, with no authored-link merge path.

Release build: zero warnings/errors. Full suite passed 1,994 executions; final Core
rerun passed 417 per target after replacing one redundant two-case theory with a
Fact (final suite size 1,992). Both isolated package consumers passed on .NET 8/10.
API baseline updated. See docs/reviews/2026-09-20-authored-definition-removal.md
for changes, migrated coverage, limitations and evidence. No commit/publication.
Next: audit actual consumers of remaining definition registration/compiler APIs
before expanding setup; no further deletion is justified solely by naming/line count.

## Latest cleanup — one document serialization entry point

Removed FlowJson.Serialize, a redundant JsonSerializer forwarding method. FlowJson
now only applies links. Tests use ApplicationDefinitionJson.Serialize and retain
round-trip, portable delivery, predicate and non-mutation assertions. No tests or
configuration features were deleted. Custom settings use the native
JsonSerializer.Serialize(definition, options) API with the existing document converter.
The authored-definition and section registration contracts remain: different inputs
are not grounds for forcing another conversion layer. Processing-profile host
overrides and validation also remain real capabilities.
Validation: final Release solution build has zero warnings/errors and all 2,190
Release test executions pass. API baseline and whitespace checks passed.
Evidence: artifacts/cleanup-review-20260920/serialization-cleanup-{api,release,tests-final}.log.
No commit or publication. This was a small duplicate-API cleanup, not removal of the
authored-definition capability or a claim that the entire Core audit is complete.

## Latest cleanup — obsolete mapping adapters removed

Removed seven Core.Mapping types/files: DelegateFlowMapper, DelegateFlowPredicate,
ExpressionFlowMapper, ExpressionFlowPredicate, IFlowMapper, IFlowPredicate and
IFlowMapContextFactory. Production Mapper/Router/link setup already uses compiled
delegates directly. Core.Mapping now contains IFlowExpressionEngine and FlowMapContext.
No replacement abstraction was added. FlowAdapterTests and its private adapter fixtures
were removed; four default-engine regression cases moved to FlowExpressionEngineTests.
Eight obsolete adapter-only cases were deleted (sixteen executions across two targets).
Current docs/API baseline are updated; earlier descriptions of these adapters are historical.
Validation: 2,190 test executions passed, zero failures; Release build had zero warnings
and errors. Source/test/API scan found no remaining references to removed types.
Evidence: artifacts/cleanup-review-20260920/mapping-adapters-{api,tests,release}.log.
Whitespace check passed. This closes the mapping-adapter slice, not an assertion that
all possible Core simplifications are exhausted. No commit or publication.

## Latest step — separate Router Unmatched output

Router.Unmatched now receives normal messages only when no routes match. Inherited
Output retains existing errors and predicate failures. Setup registers Unmatched as
a native output and reserves its name. All-match routing remains unchanged. Update
old configurations: links for unmatched data move from Output to Unmatched; Output
links now consume errors only. No automatic sink or first-match mode was introduced.
Earlier descriptions combining unmatched/errors in Output are superseded.
Validation: final full solution rerun passed 2,206 test executions. Initial run had
one intermittent Core RetryTests.Executor_retries_results_with_deterministic_time
failure on net8; its isolated rerun and the final full run passed without changes to
Retry. Preserve this observation rather than claiming the first run was clean.
Evidence: artifacts/cleanup-review-20260920/router-unmatched-*.log. API baseline and
whitespace checks passed. No commit or publication.

## Latest step — Router configuration

AddRouter and RouterConfiguration now live in FluxFlow.Configuration.Routing.
The routes object maps named outputs to expression strings; links remain ordinary
flat properties with the same output names or can be declared at destination inputs.
One engine per Router; each expression may be inline or a file via ExpressionSource.
Registration snapshots configuration and registers native factories/selectors only.
Build compiles bool predicates once using FlowMapContext.ForMessage, constructs Router,
and exposes Input, named outputs, Output (unmatched/errors), and Events.
Reserved property/port collisions and invalid names reject registration atomically.
No changes to component behavior or new engine/catalog/context abstractions.
The earlier note saying Router setup is not implemented is superseded.
Validation: 2,204 solution test executions passed; fresh isolated Configuration package
consumer passed on .NET 8/10 with a named Router output in the workflow. API baseline
and whitespace checks passed. Evidence: artifacts/cleanup-review-20260920/router-setup-*.log.
No commit, push or publication. See src/FluxFlow.Configuration/README.md for JSON syntax.

## Latest step — Router message-aware predicates

RouterOptions.Routes now accepts Predicate<FlowMessage>, not Predicate<FlowValue>.
Router passes the original message to each predicate, preserving headers, IDs and
timestamp. Setup may compile once and evaluate FlowMapContext.ForMessage(message),
as Mapper and conditional links already do. Both CSharp and JSONata are covered by
an end-to-end Router predicate regression checking every identity field, timestamp,
nested value/header access and fresh context on the next message. Router still has
no engine dependency. Existing value-only callbacks now use message.Value.
This completes the predicate boundary decision; a Router configuration adapter itself
has not been added in this step. Earlier notes calling that decision pending are stale.
Validation: all 2,188 solution test executions passed, including both engines on .NET
8/10; public API baseline updated and whitespace check passed. Evidence is under
artifacts/cleanup-review-20260920/router-context-*.log. No commit/publication.

## Latest cleanup — configuration reading after revision removal

Removed IApplicationDefinitionSource and its static/configuration implementations.
The optional Configuration package now exposes ApplicationDefinitionConfiguration.Read
and owns ConfigurationJsonReader. Core retains JSON definitions but no IConfiguration
parser or explicit Configuration.Abstractions package reference. Normal Add still
registers factories only; its existing ValueTask signature is retained.

Authored ApplicationSetup.RegisterResources now reuses AddApplicationResources for
atomic registration, including cancellation after the final callback. No second
provider, resource instance cache or new manager was added. Both component registration
input contracts remain intentionally: IConfigurationSection versus authored definition.

See docs/reviews/2026-09-20-post-revision-cleanup.md for validation and remaining decisions.
Earlier checkpoints below are historical. Do not restore removed reload sources.

## Latest scope decision — hot reload is removed

The user explicitly removed revision/hot reload from the minimum Core. This overrides
ALL earlier instructions below to preserve candidate providers, stable ports or rollback.
Those features belong to a future separately designed optional package, not this task.

Removed Core.Runtime's revision host/ports/planner/snapshots/events, AddFluxFlowUpdates,
FluxFlowApplication, ReloadAsync/ApplyAsync, RevisionResourceRegistration and its context.
Ordinary host extensions moved to Core.Application; configuration sources/readers moved
to Core.Configuration. ResourceRegistration moved from optional Configuration into
Core.Configuration.Resources and is shared by authored resource contracts and setup.
Native DI, normal FlowApplication build/start/stop, cancellation, cleanup and ordinary
component events remain. No production BuildServiceProvider call remains.

Engine.Tests and FluxFlow.Testing projects are removed (solution: 30 projects). Ordinary
hosting and configuration-loader regressions were retained in Core.Tests. Revision-only
load/benchmark sources are removed; direct graph/payload/compilation benchmarks remain.
Read docs/reviews/2026-09-20-hot-reload-removal.md before continuing. Current ordinary
hosting/lifetime guides supersede older revision guides, now marked historical.

Release build is clean; all 2,172 remaining tests passed. Real Core and Configuration consumers passed on .NET 8/10.
Evidence: artifacts/cleanup-review-20260920/no-revisions-*.log. No commit/publication.
No replacement hot-reload module or new component setup was added. Old checkpoints below
are historical; do not resume their revision-resource migration tasks.

## Latest cleanup — revision resource delegates

Revision resource registration now uses RevisionResourceRegistration, a synchronous
delegate taking the existing ApplicationResourceRegistrationContext. Removed the
IApplicationResourceRegistrar interface, RegistrationIdentity forwarding and the entire
ApplicationResourceServiceCollectionExtensions helper (including generic registrar
registration). Callers use native AddSingleton<RevisionResourceRegistration>; embedded
ApplicationResourceContract carries the same delegate without implementing registration.
Standard delegate equality deduplicates host/embedded callbacks for the same target/method.
Distinct closure instances remain distinct. Host-owned disposable callback targets should
be registered separately in DI; delegate registration does not own its target.

The context and isolated candidate provider remain: definition, revision sequence/ID,
borrowed host services, candidate descriptors and rollback are actual capabilities.
This does NOT unify revision-wide callbacks with ordinary per-resource registration.
Two ComponentRegistration input contracts still need review; no new setup was added.

Verified: zero-warning/error solution build and all 2,646 solution tests pass, including
API checks, resource hot replacement/rollback/lifetime tests, duplicate delegate
registration and new context/order/cancellation regressions. Evidence:
artifacts/cleanup-review-20260920/resource-delegates-{build,api,tests}.log.
No commit/publication. The entries below describe earlier checkpoints.

## Latest cleanup — shared expression engines

Mapper setup and conditional links now resolve the same keyed IFlowExpressionEngine.
Configuration.ExpressionCompiler is physically removed. AddCSharpExpressions and
AddJsonataExpressions register the shared contract directly; both compile typed results.
Mapping still returns a FlowValue through the unchanged component callback. Predicate
results are strict booleans, not JSONata truthiness. Workflow engine overrides, bound
parameters and original input/payload/message variables remain available, alongside
separate value/headers/string IDs/timestamp. Static Compile convenience functions remain.
Engine packages now reference only Core. Mapper file loading stays in Configuration;
conditional-link file resolution has not been added. Do not claim that it has.

Validation: 2,642 passing solution tests, zero-warning/error build; intentional API
baseline update. Evidence: artifacts/cleanup-review-20260920/shared-expressions-*.log.
No commit or publication. User still wants cleanup before any additional setup.
Remaining: review the two registration inputs and revision-resource contract without
removing snapshot/rollback behavior; decide link expression file resolution explicitly.
The entries below describe earlier checkpoints and are superseded by this one.

## In-progress cleanup — compiled expression delegates

The user confirmed refactoring/cleanup should precede all new component setup.
The first expression slice now removes IFlowCompiledExpression<T>, its default
EvaluatingCompiledExpression<T> wrapper and the private BoundCondition wrapper.
IFlowExpressionEngine.Compile<T> returns Func<FlowMapContext,T>; existing compilation
scope, bound parameters, exceptions and default Evaluate fallback are preserved.
The fallback still rejects empty source before evaluation. This is an intentional
preview API change, reflected in the public API baseline.

Validation: solution build has zero warnings/errors; all 2,624 tests passed across
the solution targets. Evidence: artifacts/cleanup-review-20260920/
expression-delegates-build.log, expression-delegates-tests.log and
expression-delegates-api.log. No commit or publication.

This slice is NOT full expression integration: IFlowExpressionEngine, FlowMapContext
and Configuration.ExpressionCompiler remain separate. Do not claim bundled Mapper
engines now support conditional links. Continue the closure report's remaining work
before Router setup; do not introduce an adapter hierarchy merely to connect contracts.

## Next-step reminder — refactoring closure before more setup

The closure review has now been performed and is recorded in
docs/reviews/2026-09-20-refactoring-closure.md. Ordinary keyed-DI cleanup is verified;
expression integration is not complete: Mapper's bundled ExpressionCompiler registrations
do not satisfy conditional links' IFlowExpressionEngine dependency. Router also currently
accepts Predicate<FlowValue>, which cannot expose the real message headers/IDs to setup
expressions. Address this predicate boundary before adding Router setup. The two setup
ComponentRegistration delegates and revision resource registrar/context remain documented
integration work, not a second ordinary runtime or grounds for deleting revision isolation.
This latest review supersedes the pending-review wording below.

The user wants unfinished refactoring kept visible rather than forgotten as new setup
is added. Before the Router setup slice, review the remaining work against current code
and the agreed boundaries. Report separately: verified complete, genuinely unfinished,
and deliberately deferred. Passing tests alone do not prove architectural cleanup is
complete. Do not infer that every retained legacy path should be removed.

Review candidates already recorded: the legacy revision resource registrar/context,
the older revision/link expression contract versus setup ExpressionCompiler delegates,
and consistency of current docs/API examples. FlowWorkflow naming was discussed but
has not changed. These are review items, not approved redesigns. Output validation,
automatic error collection and low-level output-drain policy remain deliberately
deferred. Resolve concrete cleanup findings in small verified steps; then proceed with
Router setup. This reminder does not claim that this closure review has been performed.

## Pre-setup cleanup follow-up — 2026-09-20

Latest output-policy cleanup: FlowWorkflow.DiscardedOutputs and the ordinary
DiscardUnmatched API/DI callback are removed, together with JSON policy parsing,
definition-handle declarations and revision discard-set propagation. No replacement
lookup or automatic sink is added to production. Unmatched messages retain the
existing routing error; build-time output validation and application error collection
are deferred. Low-level ApplicationPortRuntimeBuilder's output-drain option and
system/error-isolation drain behavior remain outside this workflow-property removal.
Tests use explicitly registered terminal consumers rather than workflow discard flags.

Validation: all 2,618 solution tests passed; Release build has zero warnings/errors.
Evidence: artifacts/cleanup-review-20260920/discard-solution.log and
discard-release-build.log. Public API baseline updated for this intentional removal.
The revised guide 39 example compiled, started and stopped successfully. No commit,
push or publication. FlowWorkflow naming is unchanged.


Latest structure correction: FlowApplication.Composition.cs is removed. Its methods
and _compose state now live directly in FlowApplication.cs, which is no longer partial.
No extension/wrapper was introduced merely to relocate private-state logic. Component
address segments are computed once per component when matching registered ports.
Public API shape/behavior is unchanged; the source baseline reflects the removed partial
declaration. See current FlowApplication.cs for both composition and execution lifetime.
Validation for consolidation: 1,052 Core test executions and 494 runtime/revision tests
passed, plus the two API baseline checks. No commit/publication.

Latest documentation cleanup: guides 14/15/39/40/45 now describe native DI and current
hosting/registration; removed obsolete manual construction/context/FlowExecution examples
and retired package claims. README lists 32 projects (14 production, 16 tests, support,
benchmark). The C# example extracted from guides 03 and 39 compiled and successfully
started/stopped an application in artifacts/cleanup-review-20260920/docs-consumer.
No production code changed in this documentation increment.
Validation: all 111 Release/documentation checks passed; evidence is
artifacts/cleanup-review-20260920/docs-release-tests.log. Diff whitespace check passed.

Latest update supersedes all remaining-metadata notes below: ApplicationPortMetadata,
ApplicationPorts.Metadata and the duplicated runtime metadata table are removed.
Consumers inspect the existing Status.Ports. ApplicationSystemOutputMetadata is removed;
system-output type validation uses ordinary address/type pairs. Diagnostic enum name is
MissingSystemOutputType (numeric value preserved). No new metadata/descriptor class.
Final validation: 2,660 solution tests passed, no failures; evidence:
artifacts/cleanup-review-20260920/runtime-metadata-tests-final.log. Baseline/whitespace
checks passed. All targeted metadata records are removed; stale authoring-guide cleanup
remains. No commit or publication.

Latest increment supersedes the port-metadata remaining notes below:
ComponentPortMetadata, ComponentPorts and ComponentPortKind are now physically removed.
Registration stores canonical native blocks, address discovery, and the explicit signal
boolean only. Composition resolves blocks by registered addresses. Revision surfaces
use canonical FlowValue and preserve signal behavior. The older compiler validates
types directly, without metadata records. Typed compiler test inputs remain test-only.
ApplicationSystemOutputMetadata and ApplicationPortMetadata still exist in the older
system-output/stable revision API; do not claim all metadata in the solution is gone.
Validation: 2,660 solution tests passed; evidence is
artifacts/cleanup-review-20260920/port-metadata-tests-final.log. API baseline updated;
diff whitespace check passed. No commit or publication.

Latest small step: provider metadata record/projection/null-check removed, and
DisposeComponentsAsync renamed DisposeAfterDrainAsync. The actual candidate provider
and disposal/rollback behavior remain. ComponentPortMetadata, Single link restriction
and revision port metadata still need migration; do not claim complete removal.
Preserve JSON links declared from Input or Output, strings/arrays/conditions.

Next completed increment: ComponentPortLinkCardinality and its Single/Multiple
registration/handle parameters and exclusive-claim validator are removed. Mixed input
and output declarations now accept multiple links. Existing duplicate/condition/fallback
and cycle checks remain. ComponentPortMetadata still holds name/type/signal-kind for
the older compiler/revision path; eliminating that remaining record is NOT complete.

Review: docs/reviews/2026-09-20-pre-setup-cleanup.md.
Address generation in component/port registration now uses ApplicationAddress.Value
and Port(name). The factory identity lock remains necessary across concurrent keys.
Build now requires the registered singleton and preserves configured link capacity;
the explicit int-capacity overload remains. Configuration registration and revision
preparation explicitly register the application even without workflows. The latter
preserves empty revisions that previously relied on the unowned Build fallback.
Three Core regression tests cover the Build contract. Factory XML, README, Core package
guidance and ordinary-first hosting docs were refreshed; public API baseline updated.
Remaining cleanup: current authoring guide, redundant revision metadata contract,
misleading DisposeComponentsAsync name, and assessment of duplicate factory descriptors.
Do not remove actual revision providers or rollback behavior.
Validation: 2,681 tests passed across 29 target runs, zero failures, with evidence in
artifacts/cleanup-review-20260920/solution-tests-final.log. Diff whitespace check passed.

## Completed native keyed-DI composition — 2026-09-20

This is the current checkpoint and supersedes all intermediate migration instructions
in the historical sections below. The five-step native keyed-DI plan is complete.

- One standard host-owned provider constructs/owns the ordinary Application, components,
  resources and event bridges. Repeated Build returns the same composed Application.
- Native keyed component and port singletons are the only component/port lookup path.
  Configuration registration constructs neither components nor providers.
- FlowWorkflow input/output dictionaries and manual Add/Register APIs are physically
  removed. It retains actual components, event observations and route/discard declarations.
  ApplicationResourceScope, CreateLinkServices, manual hosting and redundant per-workflow
  revision provider snapshots are removed. No replacement wrapper/catalog/cache.
- The user delegated the disposable-port decision. Chosen contract: expose separate
  native blocks that do not implement IDisposable/IAsyncDisposable; the component owns
  their cleanup and DI owns the component. Disposable port aliases are rejected before
  DI captures them. The reference-identity guard rejects reusing a node under another key.
- Application stops/drains before host provider teardown, including failed composition.
  Standard DI alone disposes its created services and stops on the first disposal error,
  as explicitly accepted by the user. No all-services disposal manager.
- Real isolated revision candidate providers remain for staging/rollback and ownership,
  including borrowed host dependency fallback. They use the same native registrations.
  The legacy revision resource registrar/context contract still exists; it is not claimed
  removed. Routing, controls, events, cancellation and revision rollback remain supported.
- Consumers, benchmarks, test fixtures, current onboarding/authoring docs and public API
  baseline are migrated. AGENTS.md now states the current contracts without accumulated
  contradictory migration paragraphs. Historical design/review documents remain history.

Verification: Release solution build has zero warnings/errors. The final full solution
run passed 2,675 tests with zero failures/skips across 29 TRX reports. Core passed 532
per target; Nodes 342 per target; Configuration 73 per target; Engine 497; Release 111.
Fresh isolated package-only consumers passed on .NET 8.0.31 and .NET 10.0.12:
Core candidate 0.0.0-local.20260920090320 and the eight-package Configuration candidate
0.0.0-local.20260920090617. No project references or stale candidate packages were used.
Evidence: artifacts/native-keyed-di-revisions-20260920/final-solution-build.log,
final-solution-tests.log, final-solution-tests/, final-test-summary.json,
final-core-consumer.log and final-configuration-consumer.log. Consumer directories:
artifacts/native-keyed-di-final-core-consumer/ and
artifacts/native-keyed-di-final-configuration-consumer/.

Checkout remains codex/core-only-component-redesign at
6b85b45f72cf8ccaec27579e340799c1118aa3c2 with all previous uncommitted work retained.
No commit, push, merge, publication or changes to other projects.

### Current continuation prompt

Read this checkpoint and AGENTS.md; verify branch/HEAD and current files before editing.
The native keyed-DI composition goal is complete. Do not repeat that migration or restore
manual Workflow port dictionaries, disposable port aliases, temporary providers or async
component factories. Preserve native DI ownership and real revision isolation.
The next planned direction is component configuration setup, reusing the existing
registration delegates and expression compilers; Router predicate setup is a reasonable
next bounded slice. Choose that slice with the user before adding new scope. Do not add
external protocol components, new expression managers, or a function component merely to
continue. Any required questions must be in English only. Keep SOLID and KISS together.

## Historical continuation checkpoints

The sections below record earlier states, failures and decisions. Their pending-work
instructions do not override the completed checkpoint above.

## Active branch: minimum framework

LATEST HOSTING MIGRATION (2026-09-20): AddFluxFlow(application) is removed; ordinary
hosting uses AddFluxFlow() over the same host registrations. Existing hosted service
receives that provider and its singleton FlowApplication, composes at host startup,
then starts sources. Stop drains; native DI owns component/resource disposal. Registration
is lazy and no second provider/executor/context was added. An explicitly registered
FlowApplication factory can retain a custom link capacity. All 15 hosting cases migrated
to native component/port/link registrations; full Engine suite passes 497 tests.
OutputProbe.Attach is physically removed. All capture nodes register before Build.
NodeRegistration, StandardComponentBinding, ComponentOwnership, DirectPortRegistration
and FlowExtensions fixtures now use native DI; legacy test-only To/From helpers removed.
Verification: hosting-solution-build.log zero warnings/errors; native-binding.log 527 Core
passes per net8/net10; native-link-fixtures-final.log subsequent four changed cases pass
per target; hosting-engine.log 497 Engine passes; hosting-doc.log 15 documentation tests.
API baseline intentionally remains pending while removing the remaining manual paths.
NEXT: manual graph fixtures (especially InstanceFirstGraphTests, FlowExecutionTests,
RouteIsolationTests plus remaining Engine fixtures) still use Workflow.Register methods.
Migrate those callers, then remove Workflow port dictionaries/registration APIs and update
runtime/revision consumers to native DI enumeration. Do not reintroduce manual host overloads
or test-only replacement containers. Goal remains active; no package qualification yet.

LATEST FIXTURE MIGRATION (2026-09-20): full Core regression now completes and passes.
DocumentApplicationFixture returns a plain (Application, Provider) tuple; callers retain
the native provider through Application shutdown and dispose it afterward. No host wrapper
or replacement provider. Old Core assertions now verify native DI ownership: failed Build
stops constructed components, but disposal occurs only at provider teardown; a throwing
disposer stops subsequent DI cleanup. Resource ordering is dependency ordering, not a
custom all-components-before-all-resources contract. Repeated Build reuses the singleton.
DI scenario output probes now register their nodes/ports/link delegates before provider
construction, removing post-Build manual component additions in document/composition tests.
Gate and native control/concurrent-port scenarios also use real keyed DI registration.
Verified: core-native-fixtures.log has 527 passing Core tests EACH on net8/net10;
routing-native-fixtures.log has 11 passing Routing tests EACH. Earlier document fixture
hang and 29 old ownership failures are resolved; their failed logs remain as history.
Evidence artifacts/native-keyed-di-revisions-20260920/. Architecture is NOT complete:
Workflow port dictionaries/manual APIs still remain. Migrate remaining manual graph/hosting
fixtures and remove those production paths; preserve routing/drain behavior. OutputProbe.Attach
has one legacy hosting caller; Register uses ordinary factories/ports/link delegates only.
API baseline and package qualification remain pending. Keep the goal active.

LATEST STRUCTURAL SLICE (2026-09-20): ordinary links use the original host provider.
CreateLinkServices is PHYSICALLY REMOVED, including the unused FlowJson.ApplyLinks overload
taking FlowApplication. No BuildServiceProvider call remains in Core/Application.
FlowApplication is a native singleton registered by AddFlowWorkflow/AddComponentFactory;
Build resolves it and caches composition with the existing RunOnce publication guard.
Repeated Build returns the same Application, factories/routes execute once; do not restore
per-Build instances in one host. Link capacity is fixed by first composition.
Input/output address discovery is plain registered read-only address data. Named and unnamed
events now register the existing ComponentActivityBridge lazily in native DI; named outputs
resolve that bridge's stamped native output even before Build, with one selector invocation.
DI disposes bridges; Application awaits bridge completion during drain, not bridge disposal.
Link callbacks resolve the same Application and enforce its mutation/identity/fallback guards.
GetComponent on DI applications resolves native keyed IFlowNode, not Workflow.Components.
FlowLink construction now validates delivery/rejection options for every route path.
Legacy manual graph tests access existing native blocks/prepare actual FlowLink directly;
they no longer construct any test-only replacement provider. Their manual component setup,
Workflow port dictionaries/Register methods and manual ownership remain migration targets.
Current verification: no-port-provider-core-final.log has 110 focused cases per framework,
no-port-provider-engine.log has 497 Engine passes, same-provider-configuration.log has 73
Configuration cases per framework. NativeDiOwnershipTests verifies same host provider for
links, same singleton Application across Build and pre-Build named event identity.
Evidence artifacts/native-keyed-di-revisions-20260920/. API/packages are still pending.
Full Core run core-latest.log was explicitly terminated after confirmed testhost hang;
bounded diagnostic run core-bounded.log also aborted at 45 seconds: 381 passed/29 failed
per framework before abort, not a full passing result. Active tests were ConfigurationValue,
ReadableConfiguration, PortableDelivery fallback and DocumentKernelParity, all using
DocumentApplicationFixture. That helper still disposes its provider before returning App;
native DI now owns its event bridges and waits on un-stopped nodes, exposing a fixture hang.
NEXT: return explicit Application/provider lifetime from that test helper and retain provider
until after app stop; migrate callers and old ownership/scope expectations, then remove
Workflow port dictionaries/manual registrations. Do not restore a lookup provider or make
Application own DI nodes to work around test failures. Full regression cannot be claimed.
One Engine termination assertion failed once in same-provider-engine.log; diagnostic assertion
now prints actual errors, 8 focused repeats and the final 497-case run passed. No production
change was justified by that timing-sensitive assertion; keep evidence if it recurs.

LATEST VERIFIED SLICE (2026-09-20): native DI ownership migration remains IN PROGRESS.
Revision regressions repaired: the existing revision snapshot supplies borrowed host
dependencies to canonical keyed node factories while native DI retains singleton caching
and ownership. Rejected/unactivated candidates now explicitly stop production and complete
controls before waiting for completion and disposing their provider (previously hung).
The reference-identity guard now reserves a node before its factory returns to DI, so a
second key/provider cannot capture disposal of the same node. Normalize IServiceProvider
identity through GetService(typeof(IServiceProvider)) for root/factory scope equivalence.
Native input/output selectors now run only through keyed block singleton factories.
Removed their old executable selector registrations; composition reads existing plain
port metadata and resolves canonical blocks. Duplicate directional port keys reject at
registration; selectors are lazy and execute once. Named event selectors still use the
old bridge path. Workflow dictionaries/Register methods and CreateLinkServices remain.
Current verification: Engine 497 pass; Configuration 73 pass per net8/net10; focused Core
identity/ownership/event/native-port fixtures 25 pass per net8/net10. Evidence:
artifacts/native-keyed-di-revisions-20260920/{engine-ports,configuration-ports,ownership-fixtures}.log
Full Core run core.log recorded 82 failures per framework BEFORE fixing event/native-port
fixture lifetimes (focused rerun now green). Remaining Core fixture migrations are real
pending work, not a passing full regression. In particular DocumentApplicationFixture
currently disposes its host provider before returning Application; fix caller ownership,
not by restoring Application ownership. Other tests expect disposal before host teardown,
per-Build instances from one provider, or scoped dependencies inside singleton components.
Manual additions to a DI application are still a legacy path and have no DI ownership;
migrate those callers rather than introducing configurable ownership or a second container.
API/package validation is still pending; do not accept baselines or mark the goal complete.

IN-PROGRESS NATIVE DI MIGRATION (2026-09-20): keep active goal; do not mark complete.
NativeDiOwnershipTests proves singleton/port identity and standard DI disposal behavior.
User answered the ownership question: ACCEPT standard DI stopping at first disposal
failure; only DI owns disposal. No custom aggregate cleanup manager wanted.
AddComponentFactory now registers keyed IFlowNode singleton (in addition to the delegate
registration for existing validation). Application resolves it through DI and borrows it;
manual construction still disposes its manual nodes. ApplicationResourceScope deleted;
AddApplicationResources stages registrations directly in the host collection, no provider.
LifetimeOwner removed. Direct keyed input/output native block registration added.
FlowOutput no longer implements IAsyncDisposable; keeps DisposeAsync pattern and explicit
component-owned cleanup delegates so DI does not also own/dispose output aliases.
Build clean; focused native identity/ownership and resource tests pass. Full migration
regression is NOT green: old ownership/multiple-Build expectations need migration, along
with actual revision issues if revealed. API baseline not yet accepted for this phase.
Evidence artifacts/native-keyed-di-20260920/. Port dictionaries, selector duplication,
CreateLinkServices and legacy callers remain; finish their removal under the active plan.
Do not hide these remaining paths behind aliases or claim completion from focused tests.
The migration full-test command (session 65842) was explicitly interrupted after other
suites finished but the run did not terminate; write_stdin returned exit 1. Do not treat
it as a passing/full completed regression or restart merely to get green output. Inspect
migration-tests.log/TRX and isolate revision lifecycle hangs with bounded test timeouts.
Node suites passed 342 each; recorded failures include 81 Core cases per framework,
12 Configuration cases per framework and the intentionally unaccepted API baseline.
ComponentFactoryConfigurationTests was subsequently migrated but not rerun yet.

ACTIVE GOAL: complete the native keyed-DI composition refactoring following
docs/plans/2026-09-20-native-keyed-di-composition.md. The user explicitly requested a
step-by-step plan and an executable goal. Async factory removal is preparation only.
Start with native DI identity/disposal proof, then migrate real composition and remove
parallel lookup. Do not mark the goal complete merely for writing the plan or removing
one overload. See the plan's definition of done and record milestone evidence there.

LATEST DIRECTION AND STEP (2026-09-20): user approved native keyed-DI composition.
Target: one ordinary application provider; component and native port lookup by unique
address directly from DI, no Workflow port dictionaries or temporary lookup provider.
Native singleton resolution should supply identity; DI owns created instances and
Application coordinates source startup, stop/drain. Preserve real routing/events/control
completion while migrating; no replacement catalog, cache or endpoint wrapper.
FIRST PREPARATORY STEP COMPLETE ONLY: removed the async component factory overload and
its runtime/configuration branch. All component factories now return IFlowNode synchronously.
Async activation belongs in startup. BuildApplicationAsync still awaits failure cleanup.
Migrated tests and package consumers to synchronous factories; removed only duplicate
async-form cases, retaining lazy construction, collision and cleanup coverage.
Build clean; 2,649 tests passed, zero failed/skipped, 29 TRX (four fewer executions from
two former sync/async parameterized cases). API baseline accepted for intentional removal.
Evidence artifacts/synchronous-construction-20260920/.
Fresh Core and Configuration package-only consumers passed net8/net10.
NEXT REQUIRED: implement the actual keyed-singleton/port lookup and ownership migration.
Workflow Inputs/Outputs, CreateLinkServices and resource provider still exist. This step
does NOT complete the user's requested refactoring. Do not present renaming/moving them
as removal. Prior instructions retaining async construction are superseded.

LATEST CHANGE (2026-09-20): remaining resource cleanup slice verified.
Removed internal ApplicationRuntimeResourceSnapshotFactory; revision preparation now
directly registers its resource services before creating the existing snapshot/provider.
No replacement class. This removes forwarding, not revision lifecycle responsibility.
Shared AddProcessingProfile on ApplicationResourceRegistration eliminates duplicate
parsing and the intermediate JSON string. Ordinary configuration validates profiles at
Add, wraps registration errors with the resource address, preserves host overrides and
staged DI rollback. Immutable profile settings are captured; runtime resources remain lazy.
The legacy IApplicationResourceRegistrar/context and embedded resource contracts remain:
they still support revision identity, host dependencies, deduplication and change detection.
Do not claim those APIs were migrated or remove their behavior as dead code.
Release build zero warnings/errors; 2,653 tests passed, zero failed/skipped, 29 TRX.
API baseline updated for AddProcessingProfile; evidence artifacts/resource-cleanup-20260920/.
Fresh eight-package consumer passed net8/net10, candidate 0.0.0-local.20260920064511.
See docs/reviews/2026-09-20-resource-cleanup.md for package qualification and remaining scope.
NEXT: ordinary resource cleanup is complete for the current configuration contract.
Any migration of legacy revision contracts must preserve their actual capabilities;
do not restore registrars in ordinary Build or introduce a replacement catalog/context.

LATEST CHANGE (2026-09-20): ordinary resource registration moved from Build to Add.
User clarified one Application per host instance in operational language; repeated Build
tests only verify technical isolation. Configuration now invokes keyed ResourceRegistration
(IServiceCollection, ApplicationAddress, ResourceInstanceDefinition) while reading resources.
Removed Core ResourceRegistration(resources, host); no host provider/context in ordinary
registration callbacks. AddApplicationResources runs callbacks immediately, freezes native
descriptors, and registers a resource-provider factory for Build. It merges repeated Add
calls transactionally. Resource/component instances remain deferred; Build replays no
resource registration callbacks. Unknown resource type fails registration.
FlowApplication owns ComposeAsync -> AddResources -> AddComponentsAsync -> MakeLinks ->
Validate, with StartAsync still separate. BuildApplicationAsync is a small delegating
extension. Composition partial file is organization, not code-size/class-count reduction.
ApplicationResourceScope and temporary existing-port provider remain internal; resources
outlive components and host scopes remain borrowed. Legacy revision registrar/context path
is unchanged and must not be claimed removed. No new template base/executor/container.
Release build zero warnings/errors; 2,643 tests passed, zero failed/skipped, 29 TRX.
API baseline accepted. Fresh 8-package configuration consumer passed net8/net10 and
asserted one registration before Build and one resource instance after Build, including
both expression mappers and cleanup. Candidate 0.0.0-local.20260920062113.
Evidence artifacts/resource-registration-stage/; report
docs/reviews/2026-09-20-resource-registration-stage.md. No commit/push/publication.
NEXT: inspect this corrected Add/Build boundary before resuming further component setup.
Do not confuse creating a native provider from frozen registrations with invoking
registration callbacks. Do not restore late resource registrars in ordinary Build.

LATEST CHANGE (2026-09-20): Mapper configuration and optional expression engines complete.
User authorized autonomous continuation while away; no open required questions.
MapperOptions.MapAsync now takes FlowMessage (read payload via message.Value) plus CT,
returning FlowValue; Mapper retains WithValue/error/lifecycle behavior. Typed mapping
still needs no serialization. Core production code unchanged in this slice.
Added MapperConfiguration/AddMapper, keyed ExpressionCompiler and ExpressionSource in
Configuration. Registration snapshots settings, Build resolves engine, reads file if
needed, compiles once per component, then constructs Mapper; failures clean up partial Build.
Optional CSharp/Jsonata packages under src/Expressions, also in the VS solution, depend
on Configuration; components/Core never reference engines. DynamicExpresso 2.19.3,
JSONata.Net.Native/SystemTextJson 3.0.0. C# read-only lazy JSON/header views; JSONata native
DOM bridge. Separate payload/headers/IDs/timestamp arguments, per-message invocation state.
Shared inline/file rules with .cs/.csx/.jsonata inference and file:/inline: escapes;
one base directory, default AppContext.BaseDirectory. Files read once each Build, not hot reload.
Limitations: C# is expression subset, Decimal-default literals, exact-case JSON keys;
numeric fallback and explicit casts documented. JSONata top-level undefined is an error.
Timestamp is DateTimeOffset in C#, ISO string in JSONata. Cancellation brackets synchronous
evaluation, cannot preempt it. Old Core IFlowExpressionEngine revision/link path remains.
Release build zero warnings/errors; 2,641 tests passed, zero failed/skipped, 29 TRX.
Configuration package-only pipeline Scheduler -> Source -> Gate -> JSONata Mapper ->
C# Mapper -> async sink/resources passed net8/net10, 8 candidate packages version
0.0.0-local.20260919224844. Direct Mapping package consumer also passed both frameworks,
candidate 0.0.0-local.20260919224920. API baseline, manifests/layout/release ordering/docs
updated. Initial failed checks retained; final evidence artifacts/mapper-expressions-20260920/.
Report docs/reviews/2026-09-20-mapper-expression-setup.md. No commit/push/publication.
NEXT: this bounded Mapper slice is complete. Reuse the same external compiler approach
for the next chosen component setup (e.g. Router predicate) while reviewing the existing
revision/link expression contract before migration. Do not add MQTT/HTTP components,
hot reload, global caches or a new expression abstraction merely to continue working.

LATEST CHANGE (2026-09-20): expression dot-access feasibility probe completed.
User agreed expressions receive separate value/headers/string IDs, with interchangeable
engines confined to setup. FlowValue.Json is the existing lazy, cached JSON boundary.
Added isolated eng/expression-dot-access (not in solution or production dependencies)
using DynamicExpresso 2.19.3 and actual Core FlowValue. Read-only DynamicObject view
supports nested dot/array access without an eager object tree. Checked scalar kinds,
missing-field errors, header access, owned JSON, concurrency and once-only generation.
Compared direct JsonElement access, lazy view and eager Expando, cached/fresh values;
view used substantially fewer allocations than eager copying for sparse expressions.
This is a limited microprobe, not workflow throughput or full mapper qualification.
Report: docs/reviews/2026-09-20-expression-dot-access.md. Logs under
artifacts/expression-dot-access-20260920/. No production API/component changes.
NEXT: small external Mapper setup/engine slice. MapperOptions currently receives only
FlowValue; exposing headers/IDs requires passing FlowMessage to the supplied delegate,
without teaching components about engines or expression parameters. Input preparation
belongs to engine integration, using FlowValue.Json. Numeric rules, output conversion,
and inline/file expression resolution still need implementation/validation. No blanket
Expando conversion or reflection adapter. Source/Gate configuration remains complete.

LATEST CHANGE (2026-09-20): registration cleanup closed; Gate/Source configuration added.
Workflow's unused event-source dictionary is now an ordinal set of component names.
Named event outputs initialize event identity once instead of registering then updating;
observation-only/routable event behavior, stamping and cleanup remain distinct/intact.
Added GateConfiguration/AddGate and SourceConfiguration/AddSource in the external
FluxFlow.Configuration project. No component implementation changed, no new framework
factory/context/catalog. Configuration now depends on Routing and Source in addition
to Core/Scheduler; package verification packs/checks all five candidate packages.
Gate exposes Input/Open/Close/Output and default event observation; initially closed
unless configured, OutputCapacity positive/default 128. Other execution options keep
component defaults for this initial configuration slice.
User delegated Source format choice after discussion of strong typing. ValuesJson is
required JSON array text, preserving number/string/bool/null/object/array distinctions
that ordinary IConfiguration scalar values cannot retain. Standard JsonDocument parsing
at registration, owned JsonFlowValue snapshots, fresh Source cursor per Build. Empty array
allowed; no automatic repeat, file loader, new converter or timer. Escaping is documented.
Release build zero warnings/errors; 2,581 tests passed, zero failed/skipped, 29 TRX.
API baseline reviewed/updated for new configuration DTOs/extensions. Fresh package-only
Scheduler → Source → Gate → async sink/resource consumer passed net8/net10;
candidate 0.0.0-local.20260919220554. Evidence: artifacts/registration-closeout-20260920/;
report docs/reviews/2026-09-20-registration-closeout.md. Active package/docs updated;
historical dated reports remain historical. No commit/push/publication.
NEXT: current cleanup and these two configuration slices are complete. Choose the
next concrete component setup based on need; Mapper requires agreeing expression
engine/string/file input semantics, so do not invent those or resume speculative
framework abstraction changes. Source's inline ValuesJson escaping is an explicit
tradeoff, not lossless reconstruction of arbitrary IConfiguration sections.

LATEST CHANGE (2026-09-19): IConfiguration registration matches Core factory contracts.
Found and reproduced Configuration rejecting the existing async
Func<IServiceProvider, ValueTask<IFlowNode>> factory as missing. It now accepts either
that form or ComponentFactory, delegating all construction/cleanup to existing Build.
Removed the redundant pre-registration component check; AddFlowWorkflow and
AddComponentFactory retain duplicate guards. Missing-factory validation and staged
service-collection rollback remain. No production class or public API added/deleted.
Tests cover lazy sync/async construction, fresh instances per Build, async failure
cleanup, and missing-factory rollback. Initial async failures retained as evidence.
Release build zero warnings/errors; 2,553 tests passed, zero failed/skipped, 29 TRX.
API baseline unchanged and passed. Fresh Configuration/Scheduler package-only consumer
now mixes a synchronous Scheduler with an async sink and owned resource; passed
net8/net10, candidate 0.0.0-local.20260919215308.
Evidence: artifacts/configuration-factory-parity/; report:
docs/reviews/2026-09-19-configuration-factory-parity.md.
NEXT: continue targeted composition/registration review or resume component setup;
do not claim an additional class deletion or invent a new factory abstraction.
Endpoint removal remains complete. No commit/push/publication.

LATEST CHANGE (2026-09-19): FlowOutputEndpoint physically removed.
Workflow Outputs now stores native Block, Metadata, IsEvent, per-output Links and
DiscardsUnmatched as a tuple keyed by full address, alongside native input data.
Build-only keyed Action<FlowLink> and Action delegates record links/discard policy
under Gate/EnsureMutable, preserving source/target identity and fallback validation.
FlowExtensions retains public ports.Link/LinkFallback/DiscardUnmatched syntax and
validates delivery policy. No new production class, endpoint facade or global link
table. FlowEndpoints.cs became FlowLink.cs. Document output discovery uses a read-only
address list in DI; revision compilation/surface/drain reads workflow registrations.
Legacy tests use tuple inspection helpers, not a shipped replacement endpoint.
All routing, metadata, events, policy and completion responsibilities remain where
needed; deletion does not mean native LinkTo alone replaces bounded fan-out logic.
Final Release build zero warnings/errors; all 2,545 tests passed, zero failed/skipped,
29 TRX reports in artifacts/output-endpoint-removal/verified-tests. Initial run had
four failures from two obsolete wrapper-identity assertions on net8/net10; migrated
them to native block identity and retained the initial evidence. API baseline updated.
Fresh Core, Routing and Configuration/Scheduler package consumers passed net8/net10.
Report: docs/reviews/2026-09-19-output-endpoint-removal.md.
NEXT: endpoint-removal stage is complete. Review remaining composition/registration
responsibilities for concrete duplication before component setup; do not add another
wrapper/manager, restart endpoint removal or sacrifice delivery/lifetime behavior.
No commit/push/publication.

LATEST CHANGE (2026-09-19): execution routing no longer depends on endpoint objects.
FlowRouter now accepts the native source, output address, routes, discard setting,
capacity and fault callback. It no longer receives/retains FlowOutputEndpoint.
Application router lookup and producer completion dependencies use full output
addresses, preserving ordinal identity and existing dependency ordering. No new
class, public API, routing algorithm or lifetime algorithm. This reduces internal
coupling; it does not simplify component authoring further or delete the endpoint.
Prepared route/metadata storage and composition validation still use the internal
FlowOutputEndpoint. Release build zero warnings/errors; all 2,545 tests passed,
zero failed/skipped, 29 TRX reports. Source baseline updated for the public constructor
declaration on internal FlowRouter, not a new exported API. Fresh Core package-only
consumer passed net8/net10 (0.0.0-local.20260919213842). Routing/Configuration package
consumers were not repeated in this step. Evidence: artifacts/native-router-inputs/;
report docs/reviews/2026-09-19-native-router-inputs.md.
NEXT: review remaining output registration/route storage in workflow and composition;
do not replace FlowOutputEndpoint with another manager or lose validation/policies.
No commit/push/publication.

LATEST CHANGE (2026-09-19): public output endpoint dependency removed.
Removed application/workflow GetOutput; no public replacement lookup facade.
FlowLink.Output is the native ISourceBlock<FlowMessage>, with OutputAddress alongside
Input/InputAddress. FlowOutputEndpoint is INTERNAL, not physically deleted. It still
owns route declarations/policies; FlowRouter and completion coordination remain.
No replacement production class. This is an API simplification/preparation step,
not completed internal endpoint removal. Legacy tests have explicit test-only output
inspection helpers; package consumers now register sinks/links before Build via DI.
Configuration-driven source execution remains verified. Release build zero warnings/
errors; all 2,545 tests passed, zero failed/skipped, 29 TRX reports. Reviewed API
baseline updated. Fresh package-only Core, Routing, Configuration/Scheduler consumers
passed on net8/net10. Evidence: artifacts/output-api-cleanup/; report:
docs/reviews/2026-09-19-output-api-cleanup.md.
NEXT: simplify remaining internal output routing state without dropping fan-out,
fallback, delivery policies, mutation guards or completion dependencies. GetComponent
and revision-host ApplicationPorts remain. No commit/push/publication.

LATEST CHANGE (2026-09-19): ordinary Application manual ingress APIs removed.
User clarified configured sources/links drive execution; no app.GetInput or app.SendAsync
scenario is required. Deleted FlowApplication.SendAsync, FlowExtensions.GetInput and
FlowWorkflow.GetInput. Also removed public endpoint LinkTo/OtherwiseTo and To/From
extensions, which depended on application target lookup. ports.Link/LinkFallback
resolve the native target from keyed DI, and the internal route declaration validates
its registration identity without resolving/substituting it via Application.
No replacement manual input facade. Tests inject into native blocks; old scenarios
have explicitly test-only DI fixture helpers. Routing test friend access was added
for shared fixtures. Benchmarks capture native input before measurement; do not compare
their current ingress workload as if it still measured the deleted application API.
Preserved source/lifecycle/drain/delivery behavior; removed manual-ingress checks are
intentionally gone, not emulated. Revision-host ApplicationPorts is unchanged.
Build zero warnings/errors; all 2,545 tests passed, zero failed/skipped, 29 TRX reports.
Reviewed API baseline updated. Fresh package-only Core, Routing and Configuration/
Scheduler consumers passed net8/net10. Scheduler verifies config-driven generation
without manual application sends. Initial focused retry timing failure is preserved;
the final full run passed. Evidence: artifacts/application-input-api-removal/; report:
docs/reviews/2026-09-19-application-input-api-removal.md.
NEXT: review remaining FlowOutputEndpoint routing state. GetOutput/GetComponent and
revision-host port APIs still exist; do not claim their deletion. No commit/push or
publication. Keep the current working tree and the agreed configuration-driven scope.

LATEST CHANGE (2026-09-19): FlowInputEndpoint physically removed.
User approved endpoint refactoring after questioning the wrappers. Completed the
input slice: GetInput returns native ITargetBlock<FlowMessage>; workflow input data
is a tuple (Block, Metadata, IsControl) keyed by address, without another class.
Build DI registers native inputs and a plain read-only address list for discovery.
FlowLink has native Input plus InputAddress. Application.SendAsync and output
LinkTo/OtherwiseTo accept full input addresses. ports.Link/LinkFallback syntax is
unchanged and resolves the native target from DI; mismatched cross-app targets are
rejected. Input metadata/control identity, aliases, revision drain, bounded admission,
fan-in, lifecycle and disposal remain. Missing foreign-only addresses now raise
KeyNotFoundException; no endpoint object crosses app boundaries.
Existing workflow dictionary is exposed internally for composition lookup without
allocating public Workflows snapshots per Send; public snapshot behavior unchanged.
Breaking development-preview APIs migrated in tests, benchmarks, consumers and docs.
Release solution build zero warnings/errors; all 2,543 tests passed, zero skipped or
failed, 29 TRX reports. API baseline reviewed and updated. Fresh package-only Core,
Routing and Configuration/Scheduler consumers passed net8/net10. Evidence:
artifacts/input-endpoint-removal/; report docs/reviews/2026-09-19-input-endpoint-removal.md.
NEXT: FlowOutputEndpoint still owns route declarations/policies; FlowRouter and
completion coordination remain. Review that boundary separately. Do not claim all
endpoints are removed, replace the deleted input class with another wrapper, or
discard routing behavior to delete output state. No commit/push/publication.

LATEST CHANGE (2026-09-19): setup link execution no longer exports a temporary document.
FlowJson.ApplyLinks(ports, setup) passes resolved declarations directly to DocumentLinks,
which combines them with original JSON declarations in the existing property/port order.
Removed runtime dependency on ApplicationSetupExportExtensions.Project; Project is now
private to ToDefinition. No new production class or public API. Existing projection
values remain; this removes full-document copying and link serialization/reparsing,
not every intermediate value. Native predicates, deduplication, conflicting policies,
fallback, invalid-declaration validation and build cleanup are preserved.
Release build zero warnings/errors; final full suite 2,539 passed, zero failed/skipped,
29 TRX reports. Seven additional cases across net8/net10. Internal Apply signature
changed the textual API hash; reviewed baseline updated and final suite passed.
Fresh Core package 0.0.0-local.20260919204954 passed net8/net10 consumer scenarios,
including input-side setup links, JSON links and direct address delegates.
Evidence: artifacts/direct-setup-links/; report:
docs/reviews/2026-09-19-direct-setup-links.md.
NEXT: review remaining endpoint/runtime responsibilities for actual duplicated work,
then resume component setup. Do not conflate ordinary duplicate merging with revision
duplicate diagnostics, or move endpoint behavior into another large builder.
No commit/push/publication. Preserve this working tree and previous refactoring.

LATEST CHANGE (2026-09-19): link validation implementation cleanup.
DocumentLinks and ApplicationLinkCompiler already share parser/condition compilation.
Their duplicate-link and diagnostic behavior differs, so they were NOT merged.
Replaced ApplicationConditionCompiler's separate success/failure caches with one
session-local Dictionary of Lazy compiled expressions; .NET retains results/original
exceptions. Removed ApplicationAddressComparer because ApplicationAddress already
implements value equality. No new class/public API; 17 fewer production lines.
Extended existing mixed-origin coverage to verify fresh compilation retries both
successes and failures. Release build zero warnings/errors. Full run: 2,524 pass,
one source-baseline mismatch (scanner counted public members of the deleted private
comparer); reviewed/updated baseline and its normal rerun passed. Thus all 2,525
unique cases verified, with original failed evidence preserved. No package rerun.
Evidence: artifacts/link-validation-cleanup/; report:
docs/reviews/2026-09-19-link-validation-cleanup.md.
NEXT: ordinary document linking merges identical declarations whereas revision
compilation reports duplicate source/target pairs. Do not silently change this
behavior to unify the paths. Endpoint/runtime responsibility reduction and further
component setup remain separate work. No commit/push/publication.

LATEST CHANGE (2026-09-19): link delegate authoring now uses full addresses directly.
Reviewed dip-workflow-engine's ILink/Link/ILinkBuilder chain; retained the existing
LinkFactory delegate instead of copying its class hierarchy. FlowExtensions now
offers ports.Link(source, target, predicate), LinkFallback and DiscardUnmatched.
DocumentLinks and FlowJson's native-predicate path use those same lookup helpers.
DocumentLinks groups registered ports by owner once rather than repeatedly scanning
and slicing full addresses. No new production class/interface and no routing changes.
This simplifies authoring and removes duplicated lookup code; it does NOT delete
FlowInputEndpoint/FlowOutputEndpoint, FlowLink or FlowRouter. Their policy/state and
completion responsibilities remain. Do not present the helpers as a completed
endpoint architecture overhaul. Build still prepares routes; Start connects them.
Release build: zero warnings/errors. API baseline updated. All 2,525 tests passed
in 29 TRX reports, zero failed/skipped (eight additional cases across net8/net10).
Fresh Core package-only consumer passed on net8/net10 for JSON and address delegates.
Evidence: artifacts/link-address-composition/; report:
docs/reviews/2026-09-19-link-address-composition.md.
NEXT: further endpoint/runtime simplification needs a concrete responsibility
reduction; do not merely move their behavior into another builder. Component setup
can now use the simpler link delegate. No commit/push/publication.

LATEST CHANGE (2026-09-19): ComponentActivationContext and ComponentInstance are
physically DELETED. This completes the stage explicitly requested by the user and
supersedes the pending-removal statements below. Also deleted ComponentInputPort,
ComponentOutputPort, ComponentEventSource, ApplicationRuntimeCleanup and the nested
non-owning node view. Do not recreate these under new names.
ComponentRegistration now registers native factories/selectors into IServiceCollection
using an address and ComponentDefinition. Both ordinary and revision construction use
BuildApplicationAsync. Existing ComponentFactory remains synchronous; a native async
Func overload preserves async construction. ApplicationSetup.ComponentRegistrations
replaces ComponentFactories. Stateless extensions handle option/resource binding;
explicit Processing capability validation belongs to option construction, with no
hidden activation-context fallback. Optional IConfigurationSection registration stays
in FluxFlow.Configuration and shares these same native Core construction methods.
Revision lifetime, rollback, accepted-work drain, first-failure handling, event routing
and resource disposal order are retained and tested. No new large replacement class.
Six production files deleted, two small files added; net 594 fewer production C# lines.
Release build: zero warnings/errors. Full suite: 2,517 passed, zero failed/skipped,
29 TRX reports. Previous 2,526 became 2,517 by removing 12 obsolete/duplicated API
cases and adding three behavior cases. Fresh package-only Core and Configuration/
Scheduler consumers passed on net8/net10. API baseline/docs/benchmarks migrated.
Evidence: artifacts/component-contract-removal/; final tests in release-tests/.
Report: docs/reviews/2026-09-19-component-contract-removal.md.
NEXT: this deletion stage is complete. Review remaining endpoint/link or component
setup boundaries separately; do not restart removal or claim all complexity is gone.
Preserve the current working tree. No commit/push/publication.

LATEST CHANGE (2026-09-19): component lifetime overrides removed as a preparation
for deleting ComponentActivationContext and ComponentInstance. User authorized
responsibility migration so those two can be removed. This turn completed the
lifetime slice, NOT final deletion. SetLifetime and ComponentInstance completion/
disposeAsync arguments and storage are gone. Nodes own work and internal resources
through Completion/DisposeAsync; resource scopes and borrowed host ownership remain.
Revision completion now also observes registered native inputs, preserving early
fault propagation while node-owned accepted work settles. Migrated tests keep
first-failure identity, delayed completion and cleanup-failure coverage.
Consolidated BindAsync/event attachment into one cleanup boundary. Deleted
ComponentInstance.InitializeAsync and redundant ValidatePorts self-comparison;
duplicate/null/name/reserved-policy and event-collision validation remain.
No new wrapper/interface/lifetime registry. Release build zero warnings/errors;
all 2,526 tests passed in 29 TRX reports. API baseline updated. Fresh Core package
consumer passed on net8/net10. Evidence: artifacts/component-lifetime-ownership/;
report: docs/reviews/2026-09-19-component-lifetime-ownership.md.
NEXT: remaining configuration/processing helpers, port selectors and event ownership
must migrate to the existing registration/composition paths, then remove both
classes and update their revision/compiler consumers. Do not claim they are gone,
recreate them under another name, or remove revision behavior to force deletion.
No commit/push/publication.

LATEST CHANGE (2026-09-19): revision factory lookup uses native keyed DI.
Verified previous checkpoint branch/HEAD/evidence before edits. Removed
CreateFactoryMap, its temporary merge dictionaries and the separate
Configuration.Factories.ComponentFactory delegate/file. Revision AddComponent now
registers original sync/async Func delegates as keyed singletons under their type.
ApplicationRuntimePreparation resolves those delegates directly, invokes them and
binds the node. Candidate embedded callbacks remain in the existing ApplicationSetup
snapshot so updates can replace callbacks without rebuilding the host provider.
Host/embedded conflicts are validated before candidate resource/node creation;
same async delegate remains idempotent and rejected candidates preserve active routes.
Ordinary Application.ComponentFactory and its DI builder are unchanged. No new
factory catalog or public activation helper. Benchmark setup uses the internal binder
outside the measured link-compilation operation (benchmark assembly gained friend
access); benchmark workload unchanged, no performance claim.
Release build zero warnings/errors; all 2,524 tests passed in 29 TRX reports. Focused
revision assembler suite: 25 passed. API baseline updated. Fresh Core package-only
consumer passed net8/net10, including sync/async revision activation, unknown-type
rollback and continued active-revision delivery. Evidence:
artifacts/revision-keyed-factories/; report:
docs/reviews/2026-09-19-revision-keyed-factories.md.
ComponentActivationContext and ComponentInstance STILL exist for revision binding,
processing settings, event surfaces and extra lifetime work. Their responsibility
migration is the next boundary; do not claim full context removal or discard revision
behavior. No commit/push/publication.

LATEST CHANGE (2026-09-19): contextual direct-application construction removed.
User authorized deleting the remaining old ordinary construction path. Deleted
ApplicationConstructionExtensions and ComponentGraphView; workflows now store
actual IFlowNode instances only. Ordinary applications use the single DI builder.
ResourceRegistration registers descriptors at Build using the existing resource
scope; host dependencies remain borrowed, resources are disposed after components.
Configuration now supports root/workflow resource declarations through those
registrars without constructing resources while reading configuration. Named
AddEventPort/RegisterEvents overloads preserve routable stamped events using the
existing bridge. Native input faults remain observable alongside additional node
completion work. ComponentFactory remains synchronous, taking only IServiceProvider.
Migrated direct-builder behavior tests to DI; revision binder/runtime tests remain.
Release build zero warnings/errors; all 2,523 tests passed in 29 TRX reports.
Count is lower than the prior 2,533 because removed entry-point/overload cases were
consolidated. API baseline updated. Fresh package-only Core, Routing and Configuration
consumers passed on net8/net10, including lazy resources and cleanup ordering.
Evidence: artifacts/context-construction-removal/; report:
docs/reviews/2026-09-19-context-construction-removal.md.
IMPORTANT: ComponentActivationContext/ComponentInstance and related registrations
still serve revision activation. They are NOT removed from Core; ordinary Build
does not use them. Reviewing/migrating that remaining boundary is the next step,
without sacrificing revision replacement, resource snapshots or rollback behavior.
Do not restore removed construction APIs or pass Application/Workflow to factories.
Link-provider/endpoint redesign remains separate. No commit/push/publication.

LATEST CHANGE (2026-09-19): first parallel construction path removed.
User prioritized eliminating parallel construction, one step at a time. Inspection
found three entry paths (DI, portable bindings, contextual resource construction).
Removed ComponentBindings, both FlowJson.BuildAsync overloads and
PortableApplicationExtensions. FlowJson no longer constructs components or performs
its own rollback. Its new ApplyLinks(IServiceProvider, ApplicationSetup, ...) overload
preserves native C# predicates alongside document links inside a normal LinkFactory.
Document tests and Core/Routing package consumers now use the existing DI builder.
DocumentApplicationFixture is test-only scenario registration, never a production
catalog/API. Resource-boundary tests now exercise actual Configuration registration
and verify root/workflow resource declarations fail before callbacks and mutation.
Breaking preview API change; no compatibility construction shim. API baseline updated.
Release build zero warnings/errors; 2,533 tests passed in 29 TRX reports. Fresh
Core and Routing package-only consumers passed on net8/net10. Evidence in
artifacts/single-construction/; report docs/reviews/2026-09-19-construction-path-removal.md.
IMPORTANT: this is NOT complete construction convergence. The older
ApplicationConstructionExtensions + ComponentActivationContext path is still present,
including owned resources, asynchronous activation, extra cleanup and event/metadata
semantics. Revision hosting also uses those contracts. Its migration/removal remains
the next priority. Do not claim it was deleted, sacrifice its behavior, reintroduce
the deleted portable catalog, or pass Application/Workflow to component factories.
Link-provider/endpoint redesign remains outside this step. No commit/push/publication.

LATEST CHANGE (2026-09-19): one task-publication rule for application lifecycle.
User confirmed to continue after two mis-transcribed messages. Reviewed lifecycle
reentry and reproduced real failures before editing production code: a component
calling StopAsync from Complete during unstarted shutdown caused two Complete calls;
a component calling DisposeAsync during cleanup started duplicate disposal and got
a different task. All three regression cases failed on both net8/net10; evidence
is artifacts/lifecycle-single-run/before-tests.log.
Start/Stop/Dispose now use one private RunOnce(ref Task?, Func<Task>) under Gate.
It publishes a TaskCompletionSource task before invoking the operation, then forwards
success, cancellation/token and failure. StartCoreAsync now only handles startup and
cleanup, rethrowing the retained startup failure; no separate TCS protocol there.
No new manager/class or public API. Existing start/admission guards remain unchanged.
Added reentrant-start regression too; successful and failed reentrant dispose share
one actual cleanup, and cleanup remains incomplete until component disposal finishes.
Application grew 284 -> 298 lines: this is consistent lifecycle behavior and a bug
fix, not a claimed line-count reduction. Never replace publication-before-callback
with task ??= OperationAsync(), which assigns after synchronous callbacks execute.
Release build zero warnings/errors; all 2,531 cases passed in 29 reports, including
the existing API-baseline test. Evidence: artifacts/lifecycle-single-run/ (build.log,
tests.log, tests/, verification.json). No package rerun for this private-only change.
AGENTS and CHANGELOG updated. No commit/push/publication. Component setup still
deferred; routing initialization/endpoints and historical revision/resource APIs remain.

LATEST CHANGE (2026-09-19): consolidate Application lifecycle guards and cleanup.
Removed _frozen, _stopped and Freeze from FlowApplication. EnsureMutable now checks
the already-published start task and ingress cancellation; Start/Send use that same
cancellation state. This also fixes structural mutation after Fault before startup.
A precanceled Start still throws before publishing a task, leaving composition and
later startup available. Do not replace the cached start task/TCS casually: publishing
it before invoking source callbacks protects repeated/reentrant startup.
Removed DisposeRoutesAsync and its nested aggregate; route cleanup errors now join
component/owner errors in the application's existing cleanup collector. All original
exceptions remain available through Flatten; route-only aggregate grouping was removed.
No new class, public API or feature removal. FlowApplication shrank 303 -> 284 lines.
Added regressions for precanceled Start, Fault before Start closing mutation/admission,
and multiple cleanup failures with exactly-once disposal of components and owner.
Release build zero warnings/errors; all 2,523 tests passed in 29 reports. API baseline
check passed. Evidence: artifacts/application-lifecycle-state/ (build.log, api.log,
tests.log, tests/, verification.json). No package rerun for this internal-only change;
the preceding routing-consumer package verification remains the last package result.
Updated AGENTS and CHANGELOG. No commit/push/publication. Routing initialization and
endpoint wrappers still exist; this is an incremental simplification, not a claim
that all application complexity is gone. Component setup work has not resumed yet.

LATEST CHANGE (2026-09-19): remove repeated application routing/lifetime bookkeeping.
User authorized continued structural simplification before component setup, with no
feature loss. FlowApplication.Initialize now uses one incoming-business-link lookup
to find entry nodes and wait for each target's upstream outputs. Removed its second
dependency-order calculation, the extra component dictionary and repeated full-link
scans per output/component. Validate still rejects cycles; control inputs still do
not participate in business completion dependencies. FlowRouter reads Links and
DiscardsUnmatched directly from its output, removing redundant constructor inputs.
StopCoreAsync now shares Stop/wait/error aggregation for started and unstarted apps;
unstarted cleanup still closes all nodes/controls without connecting routes. No new
class, public API change, execution mode or factory/registration change. FlowApplication
shrunk 315 -> 303 lines. This is incremental deduplication, not a completed routing
architecture overhaul; Initialize still belongs to Application and endpoints remain.
New regression checks fan-in across workflows with receiver registered first, one
producer completed early, one delayed, and Stop waiting for the final message.
Release build zero warnings/errors; all 2,517 tests passed across 29 reports.
Evidence: artifacts/routing-lifetime-cleanup/ (build.log, api.log, tests.log,
tests/, verification.json, package.log, routing-consumer/). API baseline updated for
the internal router signature. No commit/push/publication. Existing functionality and
older revision/resource APIs retained. Continue evaluating concrete duplicated work;
do not move Application into a new executor/manager just to reduce this file's size.

LATEST CHANGE (2026-09-19): native keyed instance resolution for link construction.
User reinforced that simplification must preserve features and use .NET/DI where
appropriate. LinkFactory now accepts only IServiceProvider, replacing two endpoint
maps plus host provider. Build registers already-created IFlowNode instances by
component address, FlowInputEndpoint/FlowOutputEndpoint and actual native target/source
blocks by port address in a local standard ServiceCollection. Its provider is used
only while invoking link delegates and disposed before Build returns. These are
INSTANCE registrations: DI neither constructs nodes again nor disposes nodes/ports.
No host descriptors are copied, no custom container/registry/manager type is added.
Host services are separate: register dependency-using link delegates as
AddTransient<LinkFactory>(host => ports => ...). This preserves the caller's scope;
a singleton delegate factory would wrongly capture root services. JSON registration
uses transient and retains existing expression resolution. FlowJson.ApplyLinks now
accepts IServiceProvider; DocumentLinks enumerates keyed endpoints via AnyKey for
port discovery and resolves endpoints by full address. Old Application overload builds
the same local instance provider. Removed public endpoint-map overload and map copies.
Routing execution is unchanged: existing endpoints still retain policies/dependency
information, FlowRouter still handles fanout/backpressure/fallback, Application.Initialize
still coordinates routing setup. Do not claim wrapper removal or overall line reduction.
This step simplifies the composition contract using native DI; the additional provider
is Build-only, not a second long-lived runtime container. Do not retain it in runtime code.
Validation: Release zero warnings/errors; all 2,515 tests passed in 29 TRX reports.
New regression verifies scoped host dependencies, native block reference identity,
exactly one component construction per build, two builds from one provider, fanout,
shutdown drain and exactly-once component disposal with no premature DI disposal.
Evidence: artifacts/link-services/ (build.log, api.log, tests.log, tests/,
verification.json, package.log, configuration-consumer/). API baseline and current docs
updated. No commit/push/publication. Older revision/resource APIs remain in use.
Previous notes describing LinkFactory endpoint-map parameters are superseded.

LATEST CHANGE (2026-09-19): separate Build from runtime Application.
User reiterated that Application Build owns DI/configuration construction and runtime
Application should not resolve factories or ports. Added BuildApplicationAsync on
IServiceProvider in existing ComponentRegistrationExtensions. It constructs workflows,
components and selected ports, invokes existing LinkFactory delegates and validates
links, then returns an UNSTARTED FlowApplication. Configuration registration and provider
construction still create no nodes. Build owns rollback and cancellation between factories;
Application owns successful component instances and their execution/disposal. Host services
remain host-owned. Removed provider-taking FlowApplication constructor, _services,
_preparation and PrepareAsync. StartAsync no longer builds; Stop need not await a build.
No new builder/manager/runtime class. Address lookup helpers moved to existing
FlowExtensions (same source-call syntax with Application namespace; binary breaking change).
Workflow.GetInput/GetOutput now read the workflow's own dictionaries.
FlowApplication: 416 -> 315 lines. This is responsibility separation and removal of
build/lifecycle interleaving, NOT a claim of 101 lines deleted from the whole framework:
construction and lookup still exist in composition extensions.
Still unfinished: Application.Initialize owns routing setup and dependency coordination;
LinkFactory still accepts endpoint maps, not native keyed block resolution. Do not claim
these have been removed. Next step should simplify that boundary without new catalogs,
double-created nodes or DI double-disposal. Preserve fanout/backpressure/fallback/control
drain; ordinary multiple Dataflow.LinkTo calls do not implement broadcast semantics.
All 2,513 tests passed across 29 reports; Release build zero warnings/errors.
New tests cover build/start separation, unstarted disposal preserving host dependencies,
and original + cleanup errors. Existing cancellation/reuse/port failure tests migrated.
Evidence: artifacts/build-runtime-separation/ (build.log, api.log, tests.log, tests/).
Package consumer was migrated to the explicit Build call; its stale pre-Build assertion
was corrected. Final package verification is recorded in package-final.log and
configuration-consumer-final/. No commit/push/publication. Previous notes below placing
construction in StartAsync are superseded. Older revision/resource APIs remain in use.

LATEST COMPLETED CHANGE (2026-09-19): pure synchronous construction factories.
User rejected passing either Application or Workflow into a component factory, then
approved section-driven registration and synchronous construction. ComponentRegistration
now receives (IServiceCollection, IConfigurationSection), binds configuration and
registers ComponentFactory(IServiceProvider) -> IFlowNode plus native port selectors.
No CancellationToken or ValueTask in either delegate. Scheduler options are built in
the factory using captured configuration and runtime dependencies; only the instance
is returned. Application adds it to the correct workflow and registers selected ports.
Added small AddComponentFactory/AddInputPort/AddOutputPort/AddEventPort extensions;
no builder, context, component wrapper or catalog class. Native DI stores uniquely
keyed factories, ordinary Func port selectors and a read-only dictionary of workflow/
component names, needed because resolve-all does not return registration keys.
Removed WorkflowFactory. AddFlowWorkflow preserves empty workflows. LinkFactory is
synchronous and receives read-only input/output dictionaries and IServiceProvider,
not Application/Workflow. FlowJson shares the existing grammar through this narrower
port-based overload; removed now-unused application InputNames/OutputNames helpers.
Renamed entry point to AddFluxFlowApplicationAsync. Registration stages standard
service descriptors and commits only after success. GetComponentAddress derives
workflow.component from the section, including a selected host root.
Cancellation remains in application startup between constructors, not inside factories.
Failed construction/port selection/link setup cleans up added nodes; a returned node
already registered to another application is not disposed by the rejecting application.
Existing async-factory tests replaced with synchronous cancellation/cleanup tests.
Validation: Release zero warnings/errors; all 2,509 cases passed in 29 TRX reports.
Independent Configuration/Core/Scheduler package consumer passes net8/net10;
package evidence is artifacts/pure-factories/package.log and configuration-consumer/.
Evidence: artifacts/pure-factories/tests/, artifacts/pure-factory-build.log,
artifacts/pure-factory-api.log and artifacts/pure-factory-tests.log. API baseline,
current documentation, package consumer and AGENTS updated. No commit/push/publication.
The older activation/definition/resource/revision path is still used and remains;
do not claim it is deleted. This change replaces the new DI contracts and simplifies
component authoring, not the entire historical runtime. Previous factory signatures
and WorkflowFactory notes below are superseded.

LATEST COMPLETED CHANGE (2026-09-19): user clarified configuration registers
deferred delegates in native DI and Application invokes them at runtime.
Added WorkflowFactory and ComponentFactory (keyed by workflow/component address),
LinkFactory (enumerable), and Configuration's ComponentRegistration (type-keyed
delegate instance reading a section and returning ComponentFactory).
services.AddFluxFlowAsync(configuration) replaces ApplicationConfiguration.BuildAsync;
it captures settings, validates and stages DI registrations, without constructing
components or a temporary provider. Scheduler migrated to this path. Application
accepts the host-owned IServiceProvider and invokes factories in order: workflows,
components, links, then starts sources. Failed/aborted construction cleans up added
components; shutdown waits for an active factory. Repeated Start does not reconstruct.
Removed FlowExecution and its separate execution-owner registry. Lifecycle,
admission and disposal now live directly in FlowApplication, not in a forwarding
facade; hosting and all callers migrated. Workflow has no Start/Stop lifecycle.
This supersedes the earlier container-only application decision below.
Application-level request/reply remains removed. Resources and automatic event/error
aggregation are not implemented here. Existing definition/document/link and revision
infrastructure remains: this new configuration path registers one LinkFactory for
the parsed document, preserving custom ports/conditions/delivery/fallback grammar.
Do not claim those older layers have been removed or that lifecycle code vanished.
This is a contract/usage simplification; Application itself is larger because it
now directly contains the necessary lifetime coordination instead of FlowExecution.
Validation: Release zero warnings/errors; 2,505 test cases verified (full run had
one outdated argument-name assertion, corrected and retested separately). Focused
factory tests cover deferred creation, keyed resolution, phase ordering, repeated
Start, failure cleanup and disposal during async creation. Configuration tests cover
settings snapshots and fresh instances per application. Independent Configuration
package consumer passes net8/net10. Evidence: artifacts/factory-contracts/
(build.log, final-tests/, hosting-final/, package.log, configuration-consumer/).
No commit, push or publication. Next cleanup should target the retained document/
definition path if authorized, without reintroducing catalogs or activation contexts.

LATEST COMPLETED CHANGE (2026-09-19): removed ComponentOwnership entirely,
including Owned/Borrowed, the workflow.Add option, tuple storage and conditional
disposal. Every successfully registered component is stopped and disposed by
FlowExecution; failed registration keeps cleanup with the caller. No replacement
ownership mode or abstraction. Injected dependencies retain their existing lifetime
contracts. Reference-identity guards remain: the same instance cannot be added
under another name, workflow or application. Internal HasOwner is now IsRegistered.
Migrated tests away from borrowed-component cleanup; retained startup failure,
unstarted-source, native fault drain and rejected-registration coverage. Added
explicit same-instance reuse coverage across all three scopes, with exactly-once
cleanup. Updated current docs, API baseline and AGENTS.md.
Validation: Release build zero warnings/errors; all 2,493 tests pass in 29 reports;
isolated Core package consumer passes net8/net10. Evidence:
artifacts/remove-component-ownership/ (build.log, tests/, verification.json,
package.log, core-consumer/). Earlier borrowed-component notes are superseded.
No commit or push.

LATEST COMPLETED CHANGE (2026-09-19): user clarified FlowApplication must be only
a container, not an executor hidden across partial files. It now exposes read-only
Workflows by name and keeps structure/address lookup in one 84-line file. Removed
FlowApplication.Composition.cs and FlowApplication.Lifecycle.cs. StartAsync,
StopAsync, Completion, Fault, SendAsync, linkCapacity and IAsyncDisposable are no
longer application APIs. The existing FlowExecution now directly owns lifecycle,
admission, routing and owned-component cleanup; there is no application forwarding
layer. Construct exactly one FlowExecution(application, linkCapacity) and dispose
it even if never started. Its single-owner check prevents duplicate execution or
cleanup. Configuration construction failures use this same cleanup path.
Output endpoints own links and discard policy; removed central application link
and discard tables. FlowExecution.Validate checks execution dependencies. Hosting
now accepts AddFluxFlow(execution); callers retain execution disposal ownership.
Migrated callers, tests, benchmarks, package consumers and current documentation.
Resources remain deferred in the direct configuration slice. The older revision
runtime/activation APIs still exist and were NOT removed in this step. Do not claim
the entire framework has been reduced to just these containers. Runtime code is
still needed; this change reduces application responsibility and removes duplicated
state/forwarding, not all execution complexity. No new runtime class was introduced.
Validation: 2,491 tests across 29 reports passed; Release zero warnings/errors;
Core, Routing and Configuration isolated package consumers pass net8/net10.
Evidence: artifacts/application-container/ (verification.json combines verified-final/
with configuration-final/; build-clean-final.log; core-package-final.log,
routing-package.log, configuration-consumer/). First test run had three obsolete
DI/reflection expectations; initial core consumer missed an execution parameter;
all corrected. Final formatting accidentally removed one JSON-fixture closing brace;
both configuration frameworks passed after restoring it. An overlapping build/test
attempt hit a file lock; the subsequent sequential build had zero warnings/errors.
Previous application lifecycle notes below are superseded.

LATEST COMPLETED CHANGE (2026-09-19): user explicitly removed application-level
request/reply. Deleted MakeFunction, FlowFunction, CallFunction and its JSON and
composition registration; FlowApplication.RequestAsync/PendingRequestCount and
request constructor options are gone. The older ApplicationPorts.SendAndReceiveAsync
path, PortRequestResult/Status, its diagnostics/histogram, and pending-exchange
ownership were also removed. Application/FlowExecution/FlowRouter and legacy
output ports no longer observe replies or suppress fallback for them. Only ordinary
links and explicit terminal-output policy remain. FlowRequestReply remains for
Retry and RateLimit components; no replacement function component was introduced.
Consumers/tests now send normally and use linked sinks (OutputProbe is test-only,
Capture is consumer-only; both handle error values explicitly). Legacy port tests
and benchmarks register receive taps before sending, with explicit terminal-output
discard; these do not provide request correlation. Obsolete API-specific tests
removed; ordinary delivery, ownership, revision and component tests preserved.
API baseline, current docs and user instructions updated; dated plans are marked
historical. Evidence: artifacts/remove-application-requests/.
Verified full suite: 2,489 tests, 29 reports, zero failures (verified/ and verification.json).
Final Release build has zero warnings/errors. Core, Routing and Configuration isolated
package consumers pass net8/net10; core-package-final.log is the successful rerun
after correcting the consumer sink to accept error messages.

LATEST STRUCTURAL REFACTOR (2026-09-19): Removed FlowTopology and
FlowGraphSnapshot, not merely renamed them. FlowWorkflow now owns component
instance/node/ownership entries, input/output addresses and event registrations.
FlowApplication contains workflows and cross-workflow links; its composition
methods are in a partial source file, not a separate runtime model. FlowExecution
reads the application directly and handles completion dependency ordering.
Workflow has no Start/Stop API; messages drive its components. App startup still
prepares links before starting sources and shutdown drains processing.
Removed AdmitRequestAsync helper by checking native SendAsync admission inline.
Request/reply and endpoint public APIs remain. Resource construction and hot reload
have not been added to the new Configuration path. Avoid claiming they are done.
Static node ownership checks moved to FlowWorkflow; legacy construction callers
use that check. No activation context was added to the new config path.
Evidence: artifacts/refactor-application-20260919/.
Validation: Release build zero warnings/errors; all 2,590 tests pass in 29 TRX
reports under final/. Isolated Configuration package consumer passes net8/net10
(package.log). Earlier run had six outdated null-parameter-name expectations;
tests now assert the actual input/output/events parameter. Public API baseline
updated for removed internal declarations and partial source organization.

CURRENT FOLLOW-UP (2026-09-19): ApplicationConfiguration.BuildAsync now accepts
IConfiguration, so Workflows/Resources are at the supplied root without an
Application wrapper. Explicit host-selected sections still work. Component
registration still receives IConfigurationSection. Resource construction remains
unsupported on this new path. All 32 configuration tests and the public API
baseline check pass. The isolated package consumer uses root configuration and
passes net8/net10; evidence: artifacts/root-configuration-20260919/consumer/.
Reviewed Application/Topology/endpoints: no runtime restructuring in this step.
Request/reply coordination already lives in FlowRequestReply; application bridges
address lookup, running-state checks and output routing. Public endpoint wrappers
remain a simplification candidate, preserving routing and lifecycle behavior.

LATEST COMPLETED REFACTOR (2026-09-18, FlowComponent removal): user explicitly
requested removing the class and every dependency, not just hiding it from setup.
Deleted FlowComponent.cs and callback-based workflow.Add. FlowTopology now stores
real instance/node/ownership tuples by address, plus flat address-to-input/output
tables and event references; endpoints carry owner addresses and execution orders
address dependencies. No replacement production component wrapper/class. Add returns
actual T; app.GetComponent(address) returns the actual IFlowNode. Workflow.GetInput/
GetOutput support named endpoint access. RegisterInput/Output/Events stay external.
Updated CallFunction dependencies, execution snapshots, document links, composition
adapter, ComponentBindings callbacks (node,workflow,name), consumers and docs.
Ownership now transfers on successful Add, before subsequent port registration;
failed Add remains caller-owned. Existing builders clean up after later failure.
Regression scenarios use a test-only TestPorts fixture helper in tests/Support;
it is not shipped or stored in runtime, and direct API/package tests use no helper.
All source references to the FlowComponent type are gone (method names such as
AddFluxFlowComponent are unrelated existing DI APIs). Final Release zero warnings/
errors; all 2,588 tests pass in 29 reports. Core, Configuration and Routing isolated
package consumers pass net8/net10. Evidence: artifacts/remove-flowcomponent-20260918/.
Earlier partial-refactor notes below are superseded. Old activation infrastructure
is a separate issue and still exists for other callers. No commit/push/publication.

LATEST PARTIAL REFACTOR (2026-09-18, direct port setup): user requested removing
FlowComponent as an unnecessary wrapper. Direct setup now uses workflow.Add(name,
actualNode), returning the same concrete instance, then RegisterInput/Output/Events
on the workflow. Scheduler derives node name from section.Key; workflow supplies
scope. Native DI delegates now return ValueTask, not FlowComponent. Removed wrapper
dictionary from ApplicationConfiguration and duplicate dictionary in FlowJson;
shared document linking reads topology directly. Configuration source contains no
FlowComponent references. However FlowComponent STILL EXISTS in old public callback
APIs and Core execution bookkeeping; global removal is NOT complete. This change
simplifies setup usage but adds direct APIs alongside legacy ones; do not claim net
implementation simplification or full deletion. Remaining task: migrate legacy
callbacks and execution storage without changing ownership, graph lifecycle or
delivery. Full Release zero warnings/errors; 2,588 tests pass in 29 reports;
independent Configuration package consumer passes net8/net10. Evidence:
artifacts/direct-ports-20260918/. No commit/push.

LATEST CORRECTION (2026-09-18, direct section construction): user rejected the
remaining activation infrastructure and requested direct IConfigurationSection
input. FluxFlow.Configuration.ApplicationConfiguration.BuildAsync(section, services)
now creates FlowApplication/workflows inline and invokes native keyed DI delegates
Func<FlowWorkflow,IConfigurationSection,ValueTask<FlowComponent>>. AddScheduler
binds section.Get<SchedulerConfiguration>(), validates/builds options, creates the
real Scheduler and calls workflow.Add with actual ports; no activation context,
ComponentInstance, graph view, custom factory map or new context in this path.
Core FlowJson.ApplyLinks exposes the existing DocumentLinks rules for already
constructed components (no duplicate address/link parser). The existing definition
reader is reused. Host owns dependencies/services; this first direct path explicitly
rejects resource declarations. Old Core activation infrastructure still exists for
other callers and has NOT been globally deleted. Native DI last-key registration
semantics apply. Updated integration tests/consumer/docs; construction failures
dispose previously added components. Full Release: zero warnings/errors, all 2,588
tests pass in 29 reports. Independent configuration consumer passes net8/net10.
Evidence: artifacts/direct-configuration-20260918/. No commit/push.

LATEST CORRECTION (2026-09-18, separate configuration project): user rejected
configuration integration inside a component package. Moved SchedulerConfiguration
and SchedulerRegistrationExtensions into independent src/FluxFlow.Configuration,
namespace FluxFlow.Configuration.Scheduling. DTO remains data-only; registration
creates options, resolves dependencies and registers ports. New package references
Core and Scheduler; neither references Configuration. Separate Configuration.Tests
owns integration tests; Scheduler.Tests contains runtime tests only. Thirty solution
projects/twelve packages. Existing general Core configuration/graph infrastructure
is reused, not extracted or duplicated in this step. Updated manifest/release waves,
API baseline, docs and solution folders. Separate package consumers verify pure
Scheduler (exactly Core+Scheduler, no DTO in assembly) and Configuration end-to-end
(Core+Scheduler+Configuration), both net8/net10. Release build zero warnings/errors.
Full suite: 2,582/2,584 passed; two layout/package conventions corrected and targeted
recheck passes, so all 2,584 cases verified. Evidence:
artifacts/configuration-project-20260918/. No commit/push/publication.
Previous note placing registration inside Scheduler is superseded.

LATEST IMPLEMENTATION (2026-09-18, Scheduler configuration): first complete
configuration-to-runtime slice. Scheduler package now has Configuration folder:
data-only SchedulerConfiguration and AddScheduler registration for type scheduler.
Registration owns validation and Options construction (no ToOptions on DTO),
resolves optional host TimeProvider, registers Output/Events outside component.
Interval/Cron/At are exclusive; cron supports zone and seconds. Uses existing
ConfigurationApplicationDefinitionSource and BuildApplicationAsync, including
shared address resolution/link compilation; no parallel setup system, new project,
or changes to Scheduler behavior/Core. Tests exercise real IConfiguration JSON,
both declaration sides, relative/cross-workflow links, all schedule modes, invalid
settings and application shutdown. Independent package consumer now exercises
configuration + registration + linking + signal receipt + disposal on net8/net10.
All 2,584 tests pass in 27 reports; Release zero warnings/errors. Evidence:
artifacts/scheduler-configuration-20260918/. API baseline and Scheduler README
updated. Configuration integration currently stays in the Scheduler package;
separate Configuration package extraction remains a later structural decision.
No mapper engine/file-expression policy, other component migrations, hot reload,
commit or push. Next: review this concrete registration before broadening scope.

LATEST IMPLEMENTATION (2026-09-18, ChangeFilter/Deduplicate): user agreed both
behaviors are useful independently of Reducer. Chosen clearer name ChangeFilter
instead of Unchange/OnChange. Two separate projects/packages/tests, each only
depending on Core. Both generic over selected CLR key (not message/payload type)
with KeySelector(FlowMessage), serial ordered processing and unchanged message
forwarding. ChangeFilter compares with last forwarded key using AreEqual; dropped
inputs never move baseline. Deduplicate uses native IEqualityComparer, required
Retention, MaxKeys default 1024, monotonic TimeProvider; duplicates do not refresh
retention/FIFO. Expiry cleanup is lazy; full capacity evicts oldest remembered key.
Keys must be stable, non-null; errors bypass without selection. Filtering does not
ack suppressed requests or prove downstream success. Docs spell out these limits.
Twenty-eight projects/eleven production packages. All 2,558 tests pass in 27 TRX
reports, Release zero warnings/errors, independent package consumers pass net8/net10.
Evidence: artifacts/changefilter-deduplicate-20260918/. API baseline, manifest,
release-plan checks and docs updated. Split and Join remain deferred, not added.
No commit/push.

LATEST IMPLEMENTATION (2026-09-18, Batch): added independent Components.Batch
project/package and tests. BatchOptions requires BatchSize/MaxWait; first processed
ordinary message starts deadline, later messages do not restart it. Size or timer
publishes current collection; normal Complete flushes remainder, no empty batches.
FlowBatch : FlowValue snapshots original message references/headers/IDs; lazy JSON
array of envelopes, no conversion during collection. New outer message identity
does not borrow any member correlation. FlowError inputs bypass unchanged and can
overtake a pending collection. Serial ordered native processing, bounded output,
one gate serializes timer/size/flush with awaited publication; canceled timers are
joined. Abort discards unpublished partial work. TimeProvider supports fake-time
tests. No Core changes or Split implementation. Twenty-four projects/nine packages.
All 2,538 tests pass in 23 TRX reports; zero build warnings/errors. Fresh Batch/Core
package-only consumer passes net8/net10 (timed partial and completion flush plus
JSON representation). Evidence: artifacts/batch-20260918/. Docs/API baseline,
manifest and release-plan checks updated. No commit/push. Next discussed: Split.

LATEST IMPLEMENTATION (2026-09-18, Reducer/RateLimit): first two agreed general
components added as separate class-library/package/test projects. ReducerOptions
requires InitialState, Reduce and AreEqual. Serial ordered processing; equal
candidate is neither stored nor emitted, failures preserve previous immutable
state. No implicit JSON equality/conversion. RateLimit uses caller-owned native
System.Threading.RateLimiting RateLimiter (10.0.7), four Input/Request/Reply/Output
ports, and owned existing exchange coordinator. Lease stays held until reply,
timeout or cancellation; supports all four native limiter algorithms, queue/reject
semantics. Native limiter is configured in options, not a request callback. Default
bounded input 128 with parallel processing so limiter controls concurrency;
ResponseTimeout infinite by default, begins after permit acquisition. Rejection
produces transient rate_limit_rejected FlowError with optional native RetryAfter.
No Core changes, ASP.NET dependency, setup/JSON, or custom limiter engine.
Twenty-two projects/eight packages. Both component READMEs explain limitations,
ownership, deadlines, output ordering and state equality. All 2,522 tests pass in
21 TRX reports, Release build zero warnings/errors, each new package-only consumer
passes net8/net10. Evidence: artifacts/reducer-ratelimit-20260918/. Source API
baseline/manifest/release plan updated. Corrected older source/scheduler/resilience
consumer scripts' malformed packageSourceMapping tag from template renaming.
Next agreed stages: Batch then Split; OnChange/Distinct later; Join explicitly
excluded. Keep each new component a separate class library. No commit/push.

LATEST UPDATE (2026-09-18, RetryPolicy naming): user accepted renaming
RetryOptions.Policy to RetryPolicy; AttemptTimeout stays separate. Updated component,
tests, README/example, package consumer, changelog and public API baseline. No
behavior change. Solution builds without warnings/errors; all 8 Retry tests pass
on each of net8/net10; updated API baseline check passes; fresh package consumer
passes both frameworks. Evidence: artifacts/retry-policy-name-20260918/.

LATEST CORRECTION (2026-09-18, separate Source/Scheduler): user requires these two
components to be independent projects/packages. Removed combined Sources project.
Source/SourceOptions now in src/Components/FluxFlow.Components.Source, namespace
and package FluxFlow.Components.Source; depends only on Core. Scheduler/options/
Schedules now in FluxFlow.Components.Scheduler, owning Cronos and no Source
dependency. Split matching unit-test projects and independent package consumers.
Visual Studio src/Components shows both projects. Eighteen solution projects,
six production packages. Behavior unchanged; preview namespace/package migration.
Old combined project's remaining generated bin/obj directories removed after
verifying no source files remained. README, API overview/baseline, manifest and
release-plan checks updated; historical combined-package notes below superseded.
Validation: 2,498 cases verified (full suite: 2,497 pass; missing Scheduler entry
in API overview corrected, targeted recheck passes). Build zero warnings/errors.
Independent Source and Scheduler package consumers pass net8/net10. Source assets
contain no Cronos/Scheduler. Evidence: artifacts/split-source-scheduler-20260918/.
No commit/push. Do not proceed to another component without further user direction.

LATEST CORRECTION (2026-09-18, Retry ports): user rejected RequestAsync in Options.
Removed the delegate; Retry now exposes native Request output and Reply input,
alongside inherited Input/Output. Owns FlowRequestReply and routes attempts through
its bounded side output. Reply uses the existing independent terminal-input
lifetime so it remains open during graceful processing/output drain; ordinary
error replies resolve exchanges. No Core lifecycle changes or new setup system.
Options contain only policy, timeout and execution/capacity settings. Consumer
uses Request -> native TransformBlock -> Reply via LinkTo. Updated tests cover
late replies, terminal reply lifetime, concurrent out-of-order replies and errors.
Scheduler visibility checked: included/compiled in Sources project at
src/Components/FluxFlow.Components.Sources/Scheduler.cs, not a separate project.
Validation: 2,498 tests pass in 15 reports, Release zero warnings/errors, isolated
package-only native feedback-loop consumer passes net8/net10. Evidence:
artifacts/retry-ports-20260918/. Earlier delegate design below is superseded.
No commit/push.

LATEST IMPLEMENTATION (2026-09-18, Retry): added optional Components.Resilience
module with Retry : FlowTransformNode and RetryOptions. RequestAsync delegate
connects one attempt to existing FlowApplication/FlowRequestReply (must honor
timeout/cancellation and await a correlated reply). Polly.Core 8.8.0 supplies
native RetryStrategyOptions<FlowMessage>; no custom retry engine or Core changes.
Fresh message/correlation per attempt, same original payload/trace/headers;
final result restores outer correlation and points causation to final reply.
Incoming errors bypass. Defaults: 30s attempt timeout, three retries, 2s delay,
only transient FlowError results. Timeout retry is explicit opt-in. Final thrown
exceptions use existing processing-error/diagnostics path. Late replies, concurrent
budgets, exhaustion and cancellation tested. Sixteen projects/five packages.
Existing Core Resilience helpers were not redesigned or removed in this step.
Validation: all 2,498 cases verified (full suite: 2,497 passed, one packaging
convention failure fixed by explicit assembly/root namespace, focused recheck
passed). Release build zero warnings/errors. Fresh package-only Resilience/Core
consumer passes net8/net10 with actual exchange coordinator. Evidence:
artifacts/retry-20260918/. Documentation/API baseline/release manifest updated.
No commit/push. Scheduler remains independent; no reply tracking added to it.

LATEST IMPLEMENTATION (2026-09-18, Scheduler): added Scheduler : FlowSource in the
existing optional Sources module. SchedulerOptions supplies NextOccurrence,
TimeProvider and OutputCapacity. Schedules.Every/At/Cron provide interval,
one-off and Cronos 0.13.0 calendar schedules. First interval tick follows one
interval; late timers emit once, overdue later ticks are skipped; missed interval
cadence restarts from current time. Output remains bounded/reliable. New independent
null-valued messages, no request/reply, ack/nack, overlap gating or retry. User
explicitly deferred retry to a possible separate component later; do not add now.
No Core dependency or project added. Custom schedule errors fault through existing
source lifecycle. Documentation, source API baseline and package consumer updated.
Validation: 2,482 tests passed across 13 TRX reports; Release build zero warnings
and errors. Fresh package-only Scheduler -> Source -> Router consumer passes
net8/net10. Evidence: artifacts/scheduler-20260918/. No commit/push.

LATEST IMPLEMENTATION (2026-09-18, trigger-driven Source): user separated Scheduler
from sample data. Source now derives from FlowTransformNode, exposes Input and emits
one next value per ordinary trigger. Interval and source startup/production API are
removed from this component. WithValue preserves trigger lineage. Errors pass through
without advancing; empty/exhausted lists emit nothing; no automatic wrap/repeat.
Completion follows input/lifecycle, not exhaustion. Options retain Values and
OutputCapacity. Scheduler/cron implementation is NOT added yet. Tests, package
consumer and documentation migrated. Evidence: artifacts/triggered-source-20260918/.
Earlier timed-source notes below are superseded. Breaking preview component API.
Validation: all 2,470 tests pass in 13 reports, Release build has zero warnings/errors,
fresh Source -> Router package consumer passes net8/net10. No commit/push.

LATEST IMPLEMENTATION (2026-09-18, Router and sample Source): RouterOptions.Routes
is a snapshotted predicate list; Outputs[i] receives every matching route, inherited
Output receives unmatched/error messages. All predicates evaluate before sending;
route names/addresses remain external. All-match was the stated default while the
router preference question remained unanswered. User clarified Source is a sample
data list with an interval, NOT a generic stream delegate. Implemented SourceOptions
Values/Interval/OutputCapacity in optional FluxFlow.Components.Sources. First value
immediate, remaining values after delays, ends after list (no repeat), mixed types
allowed, list snapshotted, payloads shared, cancellation interrupts delay. No engine
or settings base. Four packages/14 solution projects. All 2,470 tests pass in 13
reports, zero build warnings/errors; fresh Source -> Router package consumer passes
net8/net10. Evidence: artifacts/router-source-20260918/. No commit/push.

LATEST IMPLEMENTATION (2026-09-18, Mapper): added independent optional module
src/Components/FluxFlow.Components.Mapping, its unit tests and package consumer.
Mapper(MapperOptions) takes a required MapAsync delegate with FlowValue input/result
and CancellationToken; options also hold native Execution options/OutputCapacity.
No engine interface, settings base, registration method or JSONata dependency.
Mapping engines and disposal remain caller-owned. Arrays stay single payloads;
existing errors bypass conversion, normal results derive lineage through WithValue.
Solution now has 12 projects and three packages. See module README. Evidence:
artifacts/mapper-20260918/. Next can wire an external engine when explicitly selected;
do not automatically reintroduce an expression engine into Core. No commit/push.
Validation: 2,456 cases verified (full run plus the package-manifest check rerun after
adding its required changelog entry); zero warnings/errors. Fresh Mapping/Core package
consumer passes net8/net10. API baseline and package release plan updated.

LATEST IMPLEMENTATION (2026-09-18, shared processing): FlowNode is a 30-line
explicit-emission specialization of FlowTransformNode; duplicate execution,
ports, errors and lifetime are removed. Combined bases: 343 -> 233 lines.
Direct error emission timing/backpressure/tracing is preserved by an internal mode,
not a new public setting. Terminal controls moved into the shared base.
FlowTransformNode's finite-result flush hook is now FlushAsync (migrate overrides);
FlowNode keeps its existing OnInputCompletedAsync. Its native input is now
TransformManyBlock, so the load harness's concrete ActionBlock cast was migrated.
Treat this preview inheritance/native-block change as requiring consumers to rebuild.
Evidence: artifacts/shared-processing-20260918/. See corresponding review report.
Validation: 2,446 cases verified (full suite plus Engine rerun after updating the
load harness cast); zero build warnings/errors; fresh Core/Routing consumers pass
net8/net10. Source baseline updated.
FlowControlInputs remains deleted. No new base, no FlowTracing redesign, no commit/push.

LATEST IMPLEMENTATION (2026-09-18, transform side outputs and flush proof):
FlowTransformNode supports owned AddOutput<T>/EmitAsync and a finite-result
OnInputCompletedAsync flush hook. NumberRouter in the real Core package consumer
now uses TransformMany; existing flush/disposal tests use that path too. Added lazy
flush-failure test: no partial main output; lifecycle and owned outputs fault.
All 2,446 tests pass in nine reports; build has zero warnings/errors. Fresh Core
consumer passes net8/net10. Evidence: artifacts/transform-outputs-20260918/.
FlowNode is NOT removed; this demonstrates migration of multi-output and flush
scenarios, not completed consolidation. Direct side emissions retain side-effect
ordering (not EnsureOrdered result ordering) and are not transactional. Next audit
explicit-emission/terminal-control callers before consolidating bases. No new base,
no FlowTracing redesign, no commit/push. See docs/reviews/2026-09-18-transform-outputs.md.

LATEST IMPLEMENTATION (2026-09-18, source lifetime): FlowControlInputs is deleted.
FlowSource constructs its native blocks and supplies RunAsync; FlowSourceLifetime
owns production, outputs, terminal blocks, fault/cancellation and disposal.
StopProduction/ProductionCompletion remain distinct from full Completion so Ack
can finish after production stops. Source authoring API is unchanged. Source is
336 -> 126 lines, but source plus lifetime totals 424 versus old source plus helper
404: this is responsibility separation, not net line reduction. Added regression
for control failure after successful ProductionCompletion. All 2,444 cases verified
(full run plus final Nodes and source-baseline reruns), zero build warnings/errors;
fresh Core/Routing package consumers pass net8/net10. Source baseline updated for
removal of the internal helper's public Dispose declaration and source delegation.
Evidence: artifacts/source-lifetime-20260918/. Next: review remaining processing
base overlap/multi-output/flush; no further FlowTracing redesign. No commit/push.

LATEST IMPLEMENTATION (2026-09-18, processing lifetime owns terminal inputs):
FlowNode now constructs its native control ActionBlocks and delegates their lifetime
to FlowProcessingLifetime. Removed its separate completion task, CompleteControlsAsync,
control helper instance and extra disposal path. Lifetime keeps terminal inputs open
through output drain, joins them before completion and cancels/faults them with the
other owned blocks. A late-control-failure regression verifies sibling cancellation
after output completion. FlowControlInputs currently remains ONLY for FlowSource;
source production shutdown needs a separate bounded refactor, not blind reuse of a
processing-input lifetime. No public signature change or FlowTracing redesign.
Evidence: artifacts/terminal-lifetime-20260918/.
Validation: 2,441 cases passed in full run; the sole source-baseline mismatch was
fixed by making the two new lifetime members internal, then passed on rerun.
All 2,442 cases verified; final Release build has zero warnings/errors.

LATEST IMPLEMENTATION (2026-09-18, control ownership): removed application-address
validation, name-based storage and reserved Input-label checks from terminal-control
implementation. Controls are owned by block identity; names are only nonblank
diagnostic labels and can repeat. FlowControlInputs remains internal because it
coordinates staged draining/cancellation in FlowNode and FlowSource. No public API
signature change. See docs/reviews/2026-09-18-control-labels.md and evidence
artifacts/control-labels-20260918/. Next remains explicit-emission/multi-output/flush
review before base consolidation. FlowTracing redesign remains deferred.
Release build: zero warnings/errors; 2,440 tests pass in nine reports.

LATEST IMPLEMENTATION (2026-09-18, TransformMany first step): the existing
FlowTransformNode now uses TransformManyBlock and returns zero/one/many messages.
Gate uses [] / [message] and ordinary Open/Close command inputs. No new base class.
Lazy results are materialized within processing so enumeration failures become
one error message without partially publishing results. CallFunction, test probes
and the standalone consumer are migrated. This is a breaking protected API change.
All 2,436 tests pass in nine reports; Release build has zero warnings/errors;
fresh Core and Routing package consumers pass net8/net10. Evidence:
artifacts/transform-many-20260918/. FlowNode and FlowControlInputs remain: the next
step is to review their actual multi-output/flush/terminal-control requirements
before consolidating them. Do not claim that wrapper duplication is already gone.
FlowTracing redesign remains deferred. No commit/push/publication.

PREVIOUS DIRECTION (2026-09-18): user deferred further FlowTracing size/location
changes and returned to Core simplification. Resume the current-continuation section
of docs/plans/2026-09-15-component-authoring-simplification.md, not its old pending
one-base target. First review terminal-input ownership versus application naming:
FlowControlInputs still validates a local control label with ApplicationAddress.
Preserve control diagnostics and staged drain semantics. Then review wrapper
duplication and request/reply against actual callers, with a bounded closeout.
This continuation only updates planning/current documentation; the findings are not
implemented changes. Runtime baseline remains the verified 2,428 tests below.

LATEST IMPLEMENTATION (2026-09-18, propagator-owned fields): supersedes the
trace-only filter below. FlowTracing no longer defines any protocol header names.
Propagator.Fields controls explicit-carrier precedence and removal of stale fields;
Inject/Extract callbacks are generic carrier adapters. All default-propagator fields
are now admitted, including baggage, which is restored for single-message processing
and request activities (using the chosen parent's baggage). Aggregate inputs do not
merge baggage. Any explicitly supplied propagator field now preserves the caller's
carrier, rather than checking only traceparent. The propagator remains a fixed BCL
default, with no SDK dependency. All 2,428 tests pass in nine full TRX reports;
Release build has zero warnings/errors. Evidence: artifacts/propagator-fields-20260918/.
No commit/push/publication.

LATEST IMPLEMENTATION (2026-09-18, standard trace propagation): FlowTracing now
uses the BCL DistributedContextPropagator.CreateDefaultPropagator for injection
and extraction. Getter/setter callbacks adapt FlowValue headers; only the existing
W3C trace fields are carried, with no automatic baggage or hierarchical context.
This fixed default is not the host-global Current propagator. Explicit context,
stale tracestate removal, lazy business headers, queued parents, fan-out and
request/reply behavior are retained. No SDK dependency or public API change.
Release build: zero warnings/errors; all 2,426 tests pass in nine full TRX reports.
Evidence: artifacts/propagation-20260918/. See docs/processing-trace-context.md.
The separate JSONata benchmark is C:/Projects/Jsonata.Benchmarks and has no
FluxFlow reference or solution membership. No commit/push/publication.

LATEST IMPLEMENTATION (2026-09-17, structured headers and namespaces): Headers
is IReadOnlyDictionary<string, FlowValue> throughout message creation, JSON,
tracing, runtime events and request/reply. Unchanged values are shared without JSON
materialization; serialized headers retain JSON shapes and deserialize as
JsonFlowValue. Strings have a small implicit FlowValue conversion; traceparent and
tracestate remain JSON strings. Header-driven output capture settings are NOT added.
The user additionally requested folder-aligned namespaces after a manual edit had
moved only FlowMessage. All Core source namespaces now follow FluxFlow.Core.<folder>:
Messaging, Nodes, Application, Ports, Coordination, Configuration.*, Runtime.*, etc.
Production callers, tests, benchmarks and package consumers are migrated; public
API baseline updated. No compatibility namespace shims. Two packages/ten projects
remain. All 2,422 tests passed in nine final reports; Release build has zero
warnings/errors; fresh Core/Routing consumers pass net8/net10.
See docs/reviews/2026-09-17-value-headers-and-namespaces.md and evidence
artifacts/value-headers-20260917/. Breaking preview API; rebuild/migrate consumers.
No commit/push/publication. Earlier notes retaining old namespaces/string headers
are superseded. Apply SOLID/KISS and keep component registration external.

LATEST IMPLEMENTATION (2026-09-17, envelope simplification): public message
generation is now Create(value) and input.WithValue(result), with typed overloads.
With/CreateError/WithError/Restore/RestoreError/ConvertValue/ConvertError are removed;
CreateInitial/CreateDerived/Rehydrate forwarding layers are deleted. One internal
validated constructor preserves stored identities for JSON and runtime adapters.
Typed projections remain internal for typed receive APIs, including error replies.
FlowError payloads ALWAYS indicate errors, even when passed as FlowValue; ordinary
error documents can be wrapped in JsonFlowValue. This deliberately replaces the
old method-dependent error classification. Headers remain string/string.
Fresh outputs retain trace/correlation/headers, get a new ID/time and input cause;
explicit causation remains available for CallFunction's nested reply relationship.
FlowMessage.cs: 316 -> 90 lines; envelope/typed/helpers combined: 437 -> 152 lines.
Tests use non-shipping TestMessages fixtures for persisted identity/typed-view
expectations; no production compatibility shim remains. All 2,415 tests passed
in nine reports; Release zero warnings/errors; fresh Core/Routing package consumers
pass net8/net10. See docs/reviews/2026-09-17-message-envelope.md and evidence in
artifacts/message-envelope-20260917/. Breaking preview API; rebuild/migrate callers.
No commit/push/publication. The previous helper extraction below is superseded.

LATEST IMPLEMENTATION (2026-09-17, message helpers): FlowMessage convenience
operations TryGetValue/TryGetError/Match/ConvertValue/ConvertError now live in
FlowMessageExtensions in the same namespace. FlowMessage<T> lives alongside it
in FlowMessageOfT.cs; its typed Match<TResult> remains a thin forwarding method
to preserve explicit-result-type calls. Envelope construction, validation and
identity/lineage remain in FlowMessage. Headers are STILL string/string; the
structured-header discussion is not implemented. Main file: 433 -> 316 lines;
this is responsibility/readability cleanup, not a claim of fewer concepts or
less total code. Existing source callers compile unchanged; moving instance
methods to extensions is binary-breaking and requires consumer rebuilds.
All 2,409 tests pass; Release zero warnings/errors; fresh Core/Routing package
consumers pass net8/net10. Evidence: artifacts/message-helpers-20260917/.
See docs/reviews/2026-09-17-message-helpers.md. No commit/push/publication.

LATEST IMPLEMENTATION (2026-09-16, lifetime construction): removed the separate
FlowProcessingLifetime.Initialize call. Wrappers first construct their native
processor and outputs, then pass input/output/events together to the lifetime
constructor. Transform forwarding receives its stopping token directly and leaves
terminal failure handling to lifetime; it no longer accesses a partially assigned
lifetime during construction. No new abstraction or public component API change.
Cancellation tests now also verify output termination and event completion.
All 2,409 tests pass in nine reports; Release zero warnings/errors; fresh Core and
Routing package consumers pass net8/net10. See
docs/reviews/2026-09-16-lifetime-construction.md and
artifacts/lifetime-construction-20260916/. No commit/push/publication.

LATEST IMPLEMENTATION (2026-09-16, port ownership): FlowNode and FlowTransformNode
now construct and expose their own Output/Events; FlowNode creates additional
outputs locally. FlowProcessingLifetime receives existing blocks and only
coordinates completion, cancellation, fault propagation, draining and disposal.
It no longer constructs ports, exposes ports or knows output capacity/message
types. No new abstraction or public component API change; FlowSource unchanged.
All 2,409 tests passed in nine reports; Release build zero warnings/errors;
fresh Core-only and Routing package consumers pass net8/net10. Source declaration
baseline refreshed for the internal helper. See
docs/reviews/2026-09-16-processing-port-ownership.md and
artifacts/port-ownership-20260916/. No commit/push/publication. The registration
boundary below remains in effect; do not add setup responsibilities to components.

LATEST IMPLEMENTATION (2026-09-16, registration boundary): RegisterPorts is removed
from Gate, IFlowNode and all three production bases. Component classes expose
blocks and behavior only. Existing workflow Add requires its registration callback;
JSON bindings take constructor plus typed registration callback. The composition
adapter maps already configured ports; no new setup system was designed.
Input declarations freeze when the business input/source output is exposed,
independently of application registration. Terminal control identity is preserved.
All 2,409 tests passed in nine reports; fresh Core-only and Routing consumers pass
net8/net10. Boundary guards cover inherited registration and Gate is tested under
externally chosen Enable/Disable names. API baseline updated; two production
packages / ten projects remain. No commit/push/publication. See
docs/reviews/2026-09-16-component-registration-boundary.md and evidence under
artifacts/registration-boundary-20260916/. Earlier notes saying correction is
pending are superseded. Continue focusing on component implementation; do not
design new setup/DI infrastructure unless requested.

LATEST USER DIRECTION (2026-09-16, supersedes registration design below): read
AGENTS.md. Apply SOLID and KISS together: the user's clarification explicitly
rejects dogmatic compliance and unnecessary interfaces/abstractions. SOLID guides
maintainability and extensibility; KISS prevents overengineering in its name.
Choose the simplest correct solution and explain concrete tradeoffs candidly. Patterns are
tools only when they simplify; use native C#/.NET, delegates and DI capabilities
instead of unnecessary factory classes/catalogs. Component behavior and native
ports are separate from setup, naming, addressing and application composition.
The user rejected RegisterPorts on components AND on their bases/IFlowNode.
That code still exists and needs correction; earlier passing tests do not mean
the design is accepted. Focus on component implementation now, without designing
replacement setup/DI infrastructure. This turn recorded instructions only; no
runtime refactoring or new validation run was performed.

LATEST STEP (2026-09-16, Gate module): user authorized adding real components with
one project per module under Components. Routing is now the first production
module at src/Components/FluxFlow.Components.Routing, mirrored in Visual Studio.
Gate has Input/Output/Open/Close plus Events; starts closed, discards while closed,
forwards exact messages while open. Commands ignore payload and use ordinary
native inputs. SendAsync means admission, not command execution; see module README.
Core adds protected FlowNode.AddInput(process), sharing error handling and owning
additional input shutdown. Gate has no new lifecycle/diagnostic boilerplate.
Two production packages / ten solution projects now exist. Other removed modules
remain absent. Tests stay flat under tests/Unit. API baseline and manifest updated.
Final: 2,399 tests passed in nine reports; Release zero warnings/errors; isolated
Routing+Core package consumers pass net8/net10 including JSON and addressed control.
Evidence: artifacts/gate-20260916/verification.json. Report:
docs/reviews/2026-09-16-gate-module.md. No commit/push/publication. Three bases remain.
Next review Gate authoring/registration with the user before choosing another
component; do not restore the previous component suite wholesale.

LATEST STEP (2026-09-16, authoring proof): package-only consumer adds NumberRouter,
a single FlowNode class with a named additional output, without custom tracing,
error-catching or lifetime code. Both addressed outputs preserve typed payload
identity and avoid JSON materialization; error recovery and additional-output
shutdown pass on net8/net10. No production source/API changes in this step.
Review recommends retaining the three wrappers for now: ActionBlock multi-emission,
TransformBlock ordered results, and staged source production are real differences.
Common-base replacement remains unproven; do not claim it complete or introduce
mode switches merely to reduce class count. See
docs/reviews/2026-09-16-component-authoring-proof.md and
artifacts/component-proof-20260916/qualified-consumer.log. The prior 2,379-test
regression result is historical; no new full solution regression was run here.
No commit, push or publication. The new class is a consumer fixture only.

LATEST STEP (2026-09-16, node contract): IFlowComponent is removed. IFlowNode
now includes RegisterPorts (default throws with callback guidance) and
TerminalInputs (default empty). Direct Add, JSON bindings, composition graph
views and drain planning use the same contract; explicit Add callbacks still
override instance registration. IFlowSource retains startup and phased shutdown.
All callers migrated; no compatibility interface. Breaking preview API baseline
updated. New regression tests cover missing-registration rollback and callback
precedence. All 2,379 tests passed in seven reports; Release zero warnings/errors;
fresh Core-only package consumers passed net8/net10 with both registration paths.
See docs/reviews/2026-09-16-node-contract.md and
artifacts/node-contract-20260916/verification.json. No commit/push/publication.
The three base classes remain; common-base proof is still pending. Do not
reintroduce IFlowComponent or treat source startup as ordinary input processing.

LATEST STEP (2026-09-15, shared emission): required output acceptance now uses
one internal FlowOutput.EmitAsync method for FlowNode, FlowTransformNode and
FlowSource. Removed the static processing-lifetime emission helper and source
rejection helper; retained original output failures and closed-output messages.
Public callable API and source/processing lifetimes are unchanged. Source baseline
updated for internal helper removal and async implementation modifiers.
All 2,375 tests passed in seven reports; Release zero warnings/errors; whitespace
check passed. Evidence: artifacts/shared-emission-20260915/verification.json.
No new package consumer, commit, push or publication. See
docs/reviews/2026-09-15-shared-emission.md. Three public bases still remain; the
single-base proof is pending. Do not replace their distinctions with mode switches
and call that simplification; preserve staged source shutdown and native ordering.

LATEST STEP (2026-09-15, native processing options): FlowNodeOptions is removed.
FlowNode, FlowTransformNode and CallFunction accept ExecutionDataflowBlockOptions
plus int outputCapacity = 128. Transform ordering uses native EnsureOrdered.
Omitted options retain bounded input 128; explicitly supplied options use native
defaults (including unbounded input when BoundedCapacity is omitted). Native
cancellation signals Stopping and terminates owned output delivery. The unused
Clock property is removed. All callers migrated; no replacement options class.
2,375 tests passed in seven reports, zero failed/skipped; Release zero warnings
and errors; fresh Core-only consumers passed net8/net10. Ten obsolete options
test executions removed, twelve behavior executions added. Public baseline updated.
See docs/reviews/2026-09-15-native-processing-options.md and
artifacts/native-options-20260915/verification.json. No commit/push/publication.
Three bases still remain; next review common-base ownership without losing native
ordered transforms, multi-output processing or two-phase source shutdown.

LATEST STEP (2026-09-15, source capacity): FlowSourceOptions is removed.
FlowSource now takes int outputCapacity = 128 directly; validation rejects zero
and negative values. Source lifecycle and backpressure are unchanged. All callers
migrated; four option-object test executions removed, rejection/runtime tests kept.
2,373 tests passed in seven reports; Release build zero warnings/errors. Fresh
Core-only consumers passed net8/net10, including a native-linked capacity-one
source. Baseline updated for breaking preview API. Evidence:
artifacts/source-capacity-20260915/verification.json. See
docs/reviews/2026-09-15-source-capacity.md. No commit/push/publication.
The three base classes remain. Common-base simplification is still pending;
do not describe this options cleanup as unifying the base-class hierarchy.

LATEST STEP (2026-09-15, payload helper): FlowValueTransformNode is deleted. The
protected TransformAsync(message, payloadMethod) overload on FlowTransformNode
handles payload-only adaptation with shared message.With validation. Existing
ordered-concurrency, error-forwarding and lazy-JSON tests migrated; no cases
removed. The package consumer's Increment class now uses this helper. All 2,377
tests passed in seven reports; Release build zero warnings/errors; fresh Core-only
consumers passed net8/net10. Baseline updated for breaking preview API removal.
Evidence: artifacts/value-transform-helper-20260915/verification.json. See
docs/reviews/2026-09-15-transform-payload-helper.md. No commit/push/publication.
Three bases remain (FlowNode, FlowTransformNode, FlowSource). Next review the
common ownership base, preserving multi-output emission, native ordered transform
execution and two-phase source shutdown. Do not claim one-base consolidation yet.

LATEST STEP (2026-09-15, typed message): FlowMessage<T> now inherits FlowMessage,
with T : FlowValue. Message identity, payload/error state and envelope JSON logic
are shared. Typed messages pass through common ports by upcast, retaining their
actual payload. Generic send/receive/request/DI message APIs carry the constraint;
raw CLR test/benchmark payloads were migrated to fixture-owned FlowValue types.
Message/identifier converters are public for generated-metadata compatibility.
See docs/reviews/2026-09-15-typed-message.md. All 2,377 tests passed in seven reports;
Release build zero warnings/errors. Fresh Core-only consumers passed net8/net10,
including typed request/reply without JSON materialization. Evidence:
artifacts/typed-message-20260915/verification.json and qualified-consumer/.
Public baseline updated for the breaking preview API; no commit/push/publication.
The generic constraint is COMPLETE; older pending notes below are superseded.
Next is the component-base simplification proof from the component-authoring plan:
keep component classes and native blocks, reduce base-class choices without losing
ordered processing, multi-port support, error-as-data or source shutdown semantics.

LATEST STEP (2026-09-15, remove content): FlowContent and its converter are removed
from minimum Core. Binary workload fixtures own their JSON content shape; exact
byte checks remain. 18 tests of the removed API are deleted. All remaining 2,369
tests passed in seven reports; fresh Core package consumers passed net8/net10.
Release build passed. See docs/reviews/2026-09-15-remove-content.md and evidence
under artifacts/remove-content-20260915/. Public baseline updated for the breaking
preview removal. No commit, push or publication.
The user's accepted direction is component CLASSES over native blocks, with a
simple base/helpers instead of multiple node subclasses. Generic FlowMessage<T>
is explicitly wanted with T : FlowValue; the constraint is NOT implemented yet.
Next migrate generic message/runtime-port APIs and consolidate message behavior,
then simplify the node bases; see docs/plans/2026-09-15-component-authoring-simplification.md.

LATEST STEP (2026-09-15, output lifetime): processing components now manage main
and additional outputs in one collection for fault, completion, drain and disposal.
The forwarding drain task is passed directly rather than stored in lifetime state.
Source production/control phases and FlowControlInputs remain unchanged: their
shutdown distinction is required, not removed by this internal cleanup. No new
abstraction or public API change. See docs/reviews/2026-09-15-output-lifetime.md.
All 2,387 tests passed in seven reports, zero failed/skipped; Release build has
zero warnings/errors. Evidence: artifacts/output-lifetime-20260915/verification.json.
No package consumer/benchmark, commit, push or publication. Still one production
package and eight solution projects. This bounded shutdown review is complete;
do not merge source and processor lifetimes merely to remove their real distinction.

LATEST STEP (2026-09-15, direct fanout): FlowDeliveryBatch is deleted. Its only
caller, FlowOutput, directly dispatches routes with preserved all-sibling offers,
fault cancellation, joined completion and selection-error precedence. Generic
state/target plumbing and send/fault delegates are removed. Public API unchanged.
FlowControlInputs was reviewed and remains shared terminal-input lifecycle code;
ordinary Open/Close commands do not need it. NativeControlRegistrationTests proves
Gate-like behavior using plain ActionBlocks and direct HasInput registration.
This fixture is not a production component. See docs/reviews/2026-09-15-direct-fanout.md.
Final validation: 2,387 tests in seven reports passed, zero failed/skipped; Release
build has zero warnings/errors. Public API baseline and whitespace checks passed.
Evidence: artifacts/direct-fanout-20260915/verification.json. No new package-only
consumer or benchmark run; prior package qualifications remain historical.
The earlier notes listing FlowDeliveryBatch as pending are superseded. No commit,
push or publication; retain the one-production-package/eight-solution-project tree.

LATEST COMPLETED STEP (2026-09-15): FlowPorts and CreateStandard are deleted.
All standard bases and JSON graph-view binding register native blocks directly.
Plain IFlowNode callers use Add's callback; optional IFlowComponent automatic
registration uses RegisterPorts(FlowComponent), with no replacement port bag.
HasEvents registers the best-effort event stream. TerminalInputs retains native
control identity for shutdown ordering; FlowControlInputs/FlowDeliveryBatch are
unchanged and remain the next simplification review. See
docs/reviews/2026-09-15-flowports-removal.md. All 2,385 tests passed, Release build
has zero warnings/errors, and package consumers passed on net8/net10. Evidence:
artifacts/remove-flowports-20260915/verification.json. Breaking preview API baseline
updated; still one production/eight solution projects. No commit/push/publication.
The earlier pending FlowPorts migration below is superseded.

LATEST STEP (2026-09-15): direct Add now accepts an IFlowNode and registration
callback with HasInput/HasOutput over actual Dataflow blocks. No FlowPorts or
IFlowComponent is required for this new path. FlowComponent.Instance returns
IFlowNode (breaking preview surface); public baseline updated. Existing automatic
FlowPorts consumers are NOT migrated yet, so do not claim FlowPorts removal.
Next: migrate bases/JSON graph binding and event/control registration, delete the
old port-description path, then review control and delivery helpers. See
docs/reviews/2026-09-15-direct-port-registration.md. Release build passed without
warnings/errors, 2,385 tests passed, and fresh package-only consumers passed on
net8/net10. Evidence: artifacts/direct-port-registration-20260915/. No commit,
push or publication. Core source is no longer byte-identical to the prior minimum
checkpoint described below; that comparison was specific to the removal step.

NEW DIRECTION (2026-09-15): the user explicitly approved a Core-only baseline.
The former nine-package target is superseded. Eight optional packages and their
dedicated tests/tools are removed: persistence, C#/JSONata engines, Hosting/health,
OpenTelemetry and ASP.NET Core/SSE. Only FluxFlow.Core is a production project.
Core algorithms and public APIs remain; its root source files are grouped by
responsibility. There are eight solution projects. Do not restore optional
modules automatically. Review docs/reviews/2026-09-15-minimum-framework.md.
The pre-removal uncommitted state is preserved in the verified local archive
artifacts/minimum-core-20260915/before-minimum.zip, separate from main's older
6a659218 checkpoint. No commit, push or publication. Earlier instructions and
project counts below are historical; preserve the modified working tree.

Minimum-baseline qualification passed: Release build zero warnings/errors;
2,381 tests in seven reports, zero failed/skipped. A fresh package-only direct
and JSON consumer passed on .NET 8.0.31 and .NET 10.0.12 with concurrent replies
and error recovery; only Core resolved. All 206 retained production C# files are
byte-identical to the prior checkpoint, with 14 moved; the obsolete Json friend
declaration is deleted. Eight removed modules account for 73 C# files / 16,031
lines. Evidence: artifacts/minimum-core-20260915/verification.json. Next: review
this baseline and select one component/module; do not restore extras by default.


FINAL FRAMEWORK CONSOLIDATION CHECKPOINT (2026-09-15): nine production packages,
27 solution projects. Engine now ships in Core/Runtime; HealthChecks ships in
Hosting/HealthChecks. Their old projects and manifest entries are removed, with
retained namespaces and no forwarding packages. ApplicationRuntimePlanFactory
and its temporary plan record are removed; preparation calls the shared link
compiler directly. FlowLifetime, FlowOutput, FlowRequestReply, route selection
and dependency ordering remain the shared execution mechanisms. Direct graph
disposal and revision retirement retain their distinct ownership semantics.

Final qualification: 3,436 local tests passed in 19 reports; Release build has
zero warnings/errors; nine packages packed. Fresh package-only consumers passed
net8 runtime/direct/health/SQLite restart acceptance and net8/net10 tracing,
logging, metrics (plus net10 SSE). Candidate hashes matched and no retired
Engine/HealthChecks dependency resolved. Live SQL Server and external collectors
were not run. Evidence: artifacts/final-core-20260915/verification.json.
Read docs/reviews/2026-09-15-consolidated-framework.md for the final structure,
migration and limits. The agreed framework consolidation is complete locally;
do not resume the superseded intermediate merger steps below. Component redesign,
external consumer adoption and publication are separate next work. No commit,
push or publication was performed; preserve this modified tree.

Latest characterization (2026-09-15): the same JSON/factories now run under both
Core and Engine in ApplicationLifetimeParityTests. Both drain accepted work,
fan out the same messages, propagate processing errors and continue later work.
Engine Stop retires/disposes components; Core Stop drains and Dispose releases
ownership. Both lifetimes are asserted explicitly and must be preserved when
converging execution. All 536 Engine tests passed on net10.0; no full-solution
rerun or package consumer was performed for this test-only addition. Production
code/API and counts are unchanged (11 production, 29 solution). Evidence:
artifacts/lifetime-parity-20260915/engine.log and its TRX report. See
docs/reviews/2026-09-15-application-lifetime-parity.md. Next: consolidate shared
execution while preserving graph disposal versus revision retirement. Other
cross-runtime behaviors are not proven equivalent by this one scenario.

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

Latest completed step: persistence is consolidated by provider into
FluxFlow.Persistence, FluxFlow.Persistence.Sqlite and FluxFlow.Persistence.SqlServer.
Six production projects and six unit test projects became three each; the two
SQL Server integration projects stay separate. Current count: 11 production
projects and 29 solution projects. All 62 production C# files moved unchanged;
namespaces and delivery behavior remain, but package/assembly identities changed.
Consumers must replace old durable package references and rebuild.
Release build passed without warnings/errors; 3,429 local tests passed in 19
reports. The three fewer cases are consolidated package-version assertions
(six packages became three), not removed behavior tests. Eleven packages packed;
an isolated net8 package consumer passed restart recovery and candidate identity
checks. Live SQL Server integration was not run. Evidence:
`artifacts/persistence-consolidation-20260914/verification.json` and
`consumer-qualified.log`. No publication, push or commit occurred.
Next: review Core/Engine execution ownership before any merger, then revisit
HealthChecks/Hosting. The initial nine-project target must not override required
behavior or justify forwarding layers. Earlier project counts below are history.

NuGet audit follow-up: with network access enabled, forced restore with
NuGetAudit=true completed without warnings/errors. Release build then passed
with zero warnings/errors. The earlier NU1900 connectivity limitation is resolved.
Evidence: `artifacts/hosting-consolidation-20260914/audit-network-retry-3.log`
and `build-after-network-retry.log` in the same directory.

Latest step: base Observability is now in Hosting/Diagnostics. Its separate
production/test projects are removed and its namespace is retained. There are
14 production packages and 35 solution projects. HealthChecks remains separate
because it depends on Engine; merging it now would force Engine into standalone
hosting and telemetry consumers. Revisit it after the Core/Engine boundary.
All 3,432 local tests passed; 14 packages packed. Isolated package consumers passed
on net8.0/net10.0 (including net10 SSE), with matching candidate hashes and no
Engine dependency. Evidence: `artifacts/hosting-consolidation-20260914/`.
Restore/build had NU1900 warnings because the online vulnerability service was
unreachable; compilation and tests succeeded. Package consumers used local sources.

Latest completed step: JSON and Composition now ship in Core/Configuration;
their separate production/test projects are removed. There are 15 production
projects and 37 solution projects. Namespace names remain, assembly identity
changes; consumers replace old package references with Core and rebuild. DI
dependencies formerly in Composition now belong to Core. All 3,430 local tests
passed, 15 packages packed, and package-only JSON/C# registration plus request/reply
consumers passed on net8.0/net10.0. Evidence: `artifacts/core-configuration-20260914/`.
Do not interpret this package merge as completion of runtime/API simplification.

Latest implementation: Mapping and Resilience are now included in Core, with
unchanged source behavior and namespaces. Their separate production and test
projects are removed; helper tests live in Core.Tests. There are 17 production
projects and 41 solution projects. Consumers must replace the two old package
references with Core and rebuild because assembly identity changed.
See `artifacts/core-helpers-20260914/` and the framework plan for this step.
Verification passed: 3,095 local tests, 17 packages, and an isolated Core-package
consumer on net8.0/net10.0. No packages or branches were published remotely.

Latest direction: simplify the framework's project/folder structure and internal
architecture before adding the first component. The initial source audit and
proposed consolidation are in
[the framework simplification plan](docs/plans/2026-09-14-framework-simplification.md).
The structural step is complete: production projects live at `src/<project>`,
tests at `tests/<kind>/<project>`, and the Visual Studio solution mirrors this
layout with six folders (previously 38). No packages were merged or renamed;
all production C# is unchanged. Release build, 3,070 existing regression tests,
20 targeted checks including a new layout guard, and all 19 packages passed.
Evidence: `artifacts/flat-layout-20260914/`. Next: consolidate project boundaries
and simplify runtime code in short verified steps, before rebuilding components.

The complete implementation through step 79 is preserved on local `main` at
`6a659218`. The active `codex/core-only-component-redesign` branch intentionally
removes all 44 component packages and their extensions, component tests and old
samples. The 19 framework/infrastructure packages remain. Follow
[the new plan](docs/plans/2026-09-14-component-redesign.md); do not restore old
components or continue the historical component migration below automatically.

The framework-only checkpoint passed Release build (zero warnings/errors), all
3,070 local regression tests, and packaging of the 19 remaining packages.
Evidence: `artifacts/core-only-20260914/`. No remote push or publication occurred.

## Latest local checkpoint (2026-09-14)

Step 79 groups the message envelope, payload, error, content, JSON converters and
identifiers in `src/FluxFlow.Core/Messaging/` (11 files moved unchanged).
The existing namespaces and APIs are retained; processing blocks stay in `Nodes/`.

The original transfer qualification below is historical. Subsequent authorized
refactoring has completed registration closeout (step 70): catalog/descriptor/activation forwarding and both
mutable registration builders are removed. See
[component loading ledger](docs/plans/2026-09-13-component-loading.md) and
[implementation ledger](docs/plans/2026-09-12-observability.md).
Fresh local package qualification and a consolidated local regression passed in
step 70. The subsequent Open/Close Gate component drops values while closed and
passed source validation (step 71); the package candidate predates Gate.
Step 72 adds direct context port registration: Gate uses AddComponent(type,
factory) and context.HasInput/HasOutput/HasEvents without ComponentContract.
Both runtime construction paths compile links against actual instances. Release
build is clean; the final local regression passed 4,640 tests in 76 reports under
artifacts/context-registration-20260913. The 44 older typed registrations remain
for a subsequent migration; their contracts are not yet removed globally.
No step-72 package qualification or publication occurred.
Step 73 adds async direct factories and context.SetLifetime for explicit completion
and additional owned-resource cleanup. Release build is clean; 4,646 local tests
passed in 76 reports under artifacts/async-context-registration-20260913.
This is migration preparation; the 44 typed registrations remain unchanged.
Step 74 migrates Routing: RoutingComponents and its three contracts are deleted;
typed AddWindow/AddCorrelation/AddJoin embed the same direct factories as AddRouting.
ApplicationSetup snapshots callbacks for Core and Engine, including revision change
detection. Release build is clean; 4,651 local tests passed in 76 reports under
artifacts/routing-context-migration-20260914. 41 legacy registrations remain.
No step-74 package qualification or publication occurred.
Step 75 migrates Assertions, Payloads and Validation: three more contract containers
are removed, retaining typed AddX methods and sharing context factories with DI.
No new framework API was needed. Release build is clean; 4,657 tests passed in
76 reports under artifacts/component-context-migration-20260914/regression-final.
38 legacy registrations remain. No new package qualification or publication.
Step 76 completes all remaining 38 builtin registrations. All 45 registrations
across 19 families now use direct context factories; 44 typed AddX methods and 55
capture overloads remain. The remaining 15 contract containers are removed.
Release build is clean; 4,688 local tests passed in 76 reports. Fresh candidate
0.0.0-local.20260914.context.1 contains 63 packages; independent package-only
consumers passed on net8.0/net10.0 and comprehensive package acceptance on net8.0.
Evidence is under artifacts/all-context-migration-20260914. See
[builtin migration report](docs/reviews/2026-09-14-builtin-context-registration-complete.md).
There are zero builtin registrations left to migrate. Legacy custom-template APIs
in Composition and custom samples/tests remain: audit those for the next cleanup,
then remove obsolete layers while preserving needed behavior. No publication or
external consumer adoption occurred. Earlier counts and pending migrations above
are step-specific history, superseded by step 76.
Step 77 completes the remaining custom/API cleanup. ComponentContract and its
helper layers, contract maps and the empty bootstrap are removed. Samples,
benchmarks, package fixtures and current documentation use direct context
factories. Final local regression: 4,627 passed, zero failed/skipped, 76 reports.
Fresh 63-package candidate 0.0.0-local.20260914.cleanup.2 passed all-family consumers
and comprehensive SQLFile restart acceptance on both net8.0 and net10.0.
The reduced test count reflects obsolete API-only cases removed/replaced; the
report describes coverage and the separate unfiltered prerequisite failures.
See [registration cleanup report](docs/reviews/2026-09-14-registration-cleanup.md).
No internal registration migration remains. Earlier pending cleanup statements
are historical and superseded. Next separate boundary: FluxMQ-NG adoption and
release/version preparation; neither was performed.
Step 78 completes the subsequent authorized FlowValue refactoring. FlowValue is
abstract with lazy Json/CreateJson; JsonFlowValue holds dynamic JSON, and the
materializer/helper APIs are removed. HTTP/MQTT requests and FlowError inherit the
base. Canonical FlowMessage stores one payload; IsError and persisted envelope
compatibility remain. Capture conversion errors are contained as error messages;
actual store failures remain fail-closed. Final local regression: 4,630 passed,
zero failed/skipped, 76 reports. Final candidate 0.0.0-local.20260914.value.2 has 63
packages and passed independent/generated-metadata consumers and comprehensive
SQLFile restart acceptance on net8.0/net10.0. See
[lazy value report](docs/reviews/2026-09-14-lazy-flow-value.md) and
artifacts/lazy-flowvalue-20260914/verification.json. Earlier API descriptions and
qualification counts are step-specific history. No internal work from this
refactoring remains; external adoption/publication is still separate.
Do not reset this modified tree to the original clean transfer checkpoint.

## Subsequent working-tree changes

After verifying the transfer checkpoint below, the user authorized the
Authoring-to-Definition rename and extraction of all editor/UI code into the
independent `FluxFlow.UI` package consumer, now located outside this repository at
`C:\Projects\FluxFlow.UI`. See
[UI extraction and current validation](docs/reviews/2026-09-12-ui-extraction.md).
These changes are not committed yet. The clean-transfer instructions below
describe the original checkpoint, not this modified working tree; preserve the
current work rather than resetting it to satisfy that historical check.

## Start here

Open this repository folder directly in a new session. Do not create another
worktree or depend on the previous machine's workspace. Copy the **FluxFlow
folder, including hidden `.git`**. This is a lightweight source checkpoint:
generated artifacts, package feeds, build outputs and cached analysis were
intentionally removed to reduce transfer size. Source, documentation, Git history
and the recovery stash are retained. A restore/build is required before execution.

Transfer branch: `work/core-refactoring-transfer-20260912`.
Canonical folder on the previous machine: `C:\Projects\FluxFlow`.
The folder may have a different absolute path on the new machine.

First read this file completely, then run the read-only transfer check:

```powershell
pwsh -NoProfile -File eng/verify-machine-transfer.ps1
```

This checks the standalone repository, transfer branch, working-tree cleanliness
and Git object connectivity without restoring or rebuilding. It does not verify
removed binaries or independently repeat the historical qualification.
Run it **before** editing, restoring or building. Do not try to recreate historical
test results by rewriting report paths or hashes.

## Current result

The approved deep simplification goal is complete **locally**. This is not a
published release and not external consumer acceptance.

- Canonical instance-first construction: application → workflows → component
  instances. Components can be constructed normally or through their own static
  factory. No general component creator is required for ordinary use.
- Core owns canonical messages/values, native bounded delivery, lifetime and
  request/reply. Optional authoring, configuration, update hosting, editor and
  operational capabilities stay outside ordinary Core execution.
- Preserve simplicity, SRP, SOLID and explicit dependencies. Do not add universal
  managers, parallel lifetime/transport algorithms, custom subscriptions, central
  type switches or backward-compatibility wrappers without a concrete requirement.
- Reports/dashboards and backend telemetry configuration are consumer-owned.
- Context-aware Add, templates and feedback/iteration remain explicitly deferred.

The final real-service run discovered a source-event/control shutdown dependency
cycle. The final correction orders source production and full/control completion
separately using the existing dependency sorter. Business writers drain before
controls close; event outputs drain before their linked readers close. The first
partial fix was rejected by the existing file-watch test and was corrected.
Core implementation and both Core binaries were unchanged by that correction.

## Verified checkpoint

Candidate: **`0.0.0-local.20260912.integrated.1`**.

The former local feed was
`artifacts/core-refactoring/local-candidate-20260912-integrated/feed`.
It is **not included** in this lightweight copy. The candidate and results below
describe the previous qualification, not files currently available in this folder.
Create a new uniquely versioned candidate and fresh evidence when validation is
authorized; do not claim that newly built packages are the same qualified bytes.

| Check | Result and scope |
| --- | --- |
| Full Release rebuild | Zero warnings and errors |
| Configured local regression | 4,868 passed; 76 nonempty suites, 81 reports, five explicitly excluded opt-in suites |
| Real database input/output | 90 + 117 passed on net10.0 |
| Real broker-to-HTTP workflow | One passed, including shutdown, on net10.0 |
| In-process acceptance | One passed, separate from the real-service and regression counts |
| Local packages | 81 packages, 81 symbol archives, 81 READMEs, 162 target DLLs verified |
| Fresh package-only consumers | All 36 commands passed on net8.0 and net10.0 |
| Final source/service identity | 1,492 stable source inputs; 218 stable test/runtime DLLs; 42 production copies matched the packages |

Focused/repeated tests overlap those totals; do not inflate unique test counts.
Real-service conformance was source-suite execution, **not** package-only or
net8.0 server conformance. Separately configured external-endpoint smoke is unrun.
Disposable containers were removed. The database container license was confirmed
for the previous machine's run; do not assume a service is configured here.

Primary reading, in order:

1. [Integrated candidate and final fix](docs/reviews/2026-09-12-integrated-local-candidate.md).
2. [Consolidated architecture and acceptance handoff](docs/reviews/2026-09-11-refactoring-handoff.md).
3. [Final requirement reconciliation](docs/reviews/2026-09-12-final-acceptance-reconciliation.md).
4. [Execution ledger](docs/plans/2026-09-08-core-refactoring-progress.md).
5. [Migration guide](docs/46-code-first-major-migration.md) and
   [component coverage ledger](docs/17-component-coverage-matrix.md).

Historical machine-readable evidence paths (not included in this copy):

- `artifacts/core-refactoring/local-candidate-20260912-integrated/acceptance.json`
- `artifacts/core-refactoring/real-integration-20260912/final-complete/verification.json`
- `artifacts/core-refactoring/real-integration-20260912/service-verification.json`
- `.testagent/event-drain-review.md`

Detailed findings remain in the tracked documents above. The original workspace
on the previous machine still holds the raw evidence and feeds as a recovery copy;
it is not a runtime or development dependency. Historical document paths are
provenance, not a requirement to recreate that workspace. Generate fresh outputs
under a new attempt/version when requested. The graph cache and historical nested
checkouts were removed. Two old machine-specific indexing hooks are preserved but
disabled in `.git/hooks`; configure indexing afresh only if needed.

## Performance interpretation

This engine targets regular applications, not competitive actor-framework or
gaming workloads. No numerical application SLO was supplied. Representative
performance, bounded-state recovery and retirement checks have been completed.
Their reports retain their original build identities and limitations; do not
present them as freshly measured on this machine or on the last shutdown patch.
Measured regressions and telemetry overhead were documented, not waived or called
speedups. Do not reopen a generic exhaustive benchmark matrix without a concrete
new requirement or changed behavior.

## What is not done, and the next decision

1. **External consumer adoption is not done.** The intended consumer is
   **FluxMQ-NG**, not the original FluxMQ. With explicit authorization,
   first build and qualify a fresh local package feed, then point that consumer
   at it, adopt the breaking API changes and run its real workflow/dashboard
   scenarios. Do not infer permission to message
   or edit another task/project simply from this continuation prompt.
2. **Publication is not done.** No new package release, tag or main-branch merge
   was part of qualification. This transfer branch is a source checkpoint, not a
   release. Consumer validation and a deliberate release/version decision come
   before publishing. Do not push additional changes or publish without a request.
3. **New-machine validation is not done.** First verify transfer integrity. Then,
   if requested, check installed SDK/runtimes and run a fresh, separately named
   verification attempt. `global.json` allows feature-band roll-forward; the
   qualified machine used SDK 10.0.400 and runtimes 8.0.30/10.0.11. Do not install
   runtimes, accept licenses, start external services or change system settings
   merely to recover historical status.

The old main checkout had unrelated uncommitted work. It was preserved locally
before checkout of this branch. The Git recovery stash is
`14f1a9ba36036398bdc35f2dc610fbf24283b387`, labelled
`Pre-transfer main checkout 2026-09-12`; it travels with `.git`.
Generated outputs and the extra working-folder backup were moved outside the project to
`C:\Projects\FluxFlow-transfer-recovery-20260912` on the previous machine.
The old checkout backup is inside `artifacts/transfer/before-20260912` there.
This is optional recovery material, not needed to continue this checkpoint.
Do not apply the stash onto this branch: it would reintroduce an older overlapping
state. Inspect or restore it separately if the user requests it.

## Ready-to-paste continuation prompt

> Continue this FluxFlow project from the folder I opened, directly in this
> checkout rather than a new worktree. Read CONTINUE-HERE.md completely and follow
> its reading order. Run eng/verify-machine-transfer.ps1 before modifying anything.
> Inspect the branch, commit, working-tree status and Git integrity. Summarize
> the verified current state, any actual transfer problems, and what remains
> unperformed. Treat the local simplification goal as completed; do not restart
> it or reopen completed checks without evidence. This is a source-only transfer:
> generated feeds and raw reports were intentionally removed; their absence is
> expected, not corruption. Preserve the pre-transfer recovery stash. Tell me the
> next concrete decision
> for FluxMQ-NG adoption and release preparation, then wait for my direction.
> Respond in English. Do not publish, push, merge, install dependencies, alter
> system configuration or coordinate another project merely because this session
> is a continuation.

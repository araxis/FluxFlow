Current status: steps 69–71 are complete locally, including Gate source validation. ComponentCatalog,
ComponentDescriptor, ComponentActivation and both mutable runtime registration
builders are removed. ComponentContract holds the factory and the port declarations
needed for activation/link validation. Components register through direct factories
and fixed native or selected-block bindings. Earlier entries below are history.

Registration closeout: fresh local package-only net8.0 acceptance and the complete
configured regression passed (step 70). Gate now uses the direct registration API;
see step 71 for its separate source validation. Sessions, Constants and revision-host
changes remain deferred.

# Simplify configuration-driven component construction

First step: removed ComponentCatalog and the separate ComponentActivation.CreateAsync
forwarding method. Configuration execution, link validation, DI registrations,
Engine consumers and tests now use IReadOnlyDictionary<string, ComponentDescriptor>.

CreateComponentMap is a small function in the existing registration extensions.
It builds an ordinal, ordered, read-only snapshot, accepts the same registration
instance more than once, and rejects conflicting registrations for a type.
There is no replacement catalog class. Dictionary lookup is exact; the removed
TryGetDescriptor helper's incidental whitespace trimming is not a dictionary
behavior. Definition and registration parsing retain their existing validation.

The direct build loop selects registrations[type].Factory and invokes it with
the construction context. Failed/null factories and failed addition still report
errors and clean up owned instances. Port/option metadata validation, resource
ownership and event handling remain.

This is not the end of registration simplification: ComponentDescriptor, its
factory wrapper, ComponentInstance and registration builders still exist.
Their metadata validates configuration before activation; their event and cleanup
behavior must survive simplification. The next bounded step is to separate passive
metadata from factory execution and reduce the registration API component authors
must use. Do not claim the entire path is already a dictionary of bare delegates.

The intended direction remains Type -> registered factory -> normal component,
without component-type switches in application construction.


Second step: flattened registration. AddFluxFlowComponents().AddComponent(type,
configure) replaces Advanced.AddDynamicComponent. Removed the Advanced registration
wrapper and the separate non-generic instance binding builder. UseInstanceFactory
returns the same registration builder, which owns the Has port declarations.
Removed unused inheritance hooks, configuration-copy scaffolding and the duplicate
ComponentDescriptor constructor. Snapshot isolation and duplicate-declaration tests
remain; the copy-hook test was removed with that unused API.

The generic typed port binding builder and ComponentDescriptor are still present.
The latter still owns activation validation/event attachment/cleanup, as described
above. Separating those responsibilities is the next bounded refactor; this step
does not claim complete removal of descriptors or all builders.


Third step: deleted FluxFlowRegistrationBuilder without a replacement class.
Component registration and all 19 family registration extensions now operate on
IServiceCollection. AddFluxFlowUpdates returns the same service collection too.
AddComponent(contract) and AddComponent(type, configure) initialize the registration
map themselves; AddFluxFlowComponents is only needed to register an empty map.
For example: services.AddMapping().AddMqtt(); no builder acquisition is necessary.
The Metrics family registration is AddFluxFlowMetrics to avoid the standard .NET
IServiceCollection.AddMetrics extension. Workflow AddMetrics is unchanged.

RuntimeComponentRegistrationBuilder, typed port binding and ComponentDescriptor
remain the next removal targets. This removes the outer registration layer only,
without replacing it by another custom wrapper.


Fourth step: removed ComponentBinding and its Input/Output/Events subclasses.
Registration now keeps private binding delegates alongside the original selector
identities. Snapshot creation orders bindings once by port role; activation invokes
them in a single pass. The original selector remains the registration-comparison
identity, so equivalent/conflicting declarations retain their behavior. Null selector
results retain the same diagnostics, and activation failure still disposes owners.
This is internal simplification of the remaining construction path, not removal of
ComponentDescriptor or the two remaining public builders; those targets remain.


Fifth step: standard component factories bypass the typed port builder.
UseStandardFactory(factory, optionalInputShape) registers a factory and its standard
native port bindings directly. Removed StandardComponentBindingExtensions and
HasStandardPorts. Migrated all 33 standard family registrations and the benchmark.
The standard path never constructs RuntimeComponentBindingBuilder<TNode>. Sync,
async and additional-lifetime factories share the existing activation/cleanup path.
Metadata remains available without activating the component; exact standard ports
are checked at activation. Custom port declarations still use UseFactory plus
explicit bindings. The main registration builder and ComponentDescriptor remain
next targets; this is not full elimination of the registration architecture.


Sixth step: removed the descriptor's factory wrapper and ComponentActivation class.
ComponentDescriptor.Factory is now the exact supplied delegate. ComponentInstance
owns ActivateAsync and its private physical-port checks; existing initialization,
processing setup, duplicate-owner protection, event attachment, null-result policy
and cleanup semantics remain. Direct and Engine construction use the same explicit
entry point. Tests now call that entry point and assert raw factory identity.

Migration: callers that previously invoked descriptor.Factory(context) to obtain a
fully initialized instance now call ComponentInstance.ActivateAsync(descriptor,
context). Calling Factory directly only runs the supplied factory. No new helper
class was introduced. Descriptor remains as metadata plus a factory reference;
its removal and the remaining builder removal remain the objective.


Seventh step: flattened ComponentContract and ComponentDescriptor into the existing
contract. Deleted ComponentDescriptor.cs and the Descriptor property. Registries and
application setups now contain ComponentContract directly; the setup property is
ComponentContracts. Typed contracts retain handle/options behavior and immutable
metadata, without a nested descriptor object. The temporary base registration used
during typed factory declaration is copied by reference for immutable fields and is
not retained as a wrapper. Duplicate registration uses the actual contract identity.
Activation still belongs to ComponentInstance; no new catalog/descriptor class was
introduced. Custom runtime and standard factory builders are still to be removed.

Migration: use ComponentContract from FluxFlow.Composition.Definition, read Inputs,
Outputs, Factory, Options and Resources directly on the contract, and use
ApplicationSetup.ComponentContracts. JSON structure is unchanged. This removes the
separate descriptor layer, not the metadata needed by configuration/link validation.


Eighth step: Mapper uses a direct standard contract declaration. Added the synchronous
ComponentContract.CreateStandard factory/options/handle overload with passive input
shape, processing, option and resource metadata. Its native-port adaptation constructs
no RuntimeComponentRegistrationBuilder or RuntimeComponentBindingBuilder. Deleted
ConfigureMapper and moved its metadata beside the typed contract declaration. The
factory still binds its internal options/resources in the existing component code.
Only Mapper is migrated in this step; other families still use the existing paths.
No unused async/additional-owner overloads were introduced. Those factories and the
remaining standard families need migration before deleting the remaining builders.

Existing standard-component tests now exercise both declarations for deferred factory
execution, shaped/unshaped input, native data/event identity, all invalid standard
port shapes, and cleanup failures. This is an end-to-end migration, not just an API
addition. The options object used by C# configuration remains; it is separate from
the removed registration-builder dependency in Mapper.


Ninth step: migrated 14 more registrations with the existing direct CreateStandard
API; no new framework API. Deleted their Configure callbacks and placed the same
ordered option/resource metadata with each contract. Factories still perform their
own internal binding/materialization. Migrated Assertion, EventExpectation, FileRead,
FileWrite, HttpClient, MetricAggregation, observability Counter/Logger/Metrics,
PayloadInspection, Projection, Window, StateReducer and JsonSchemaValidator.
There are now 15 direct registrations including Mapper, and 29 remaining callbacks
(18 standard factories plus 11 custom-port factories). MQTT factories are async,
Sessions have additional ownership, and Correlation uses shared metadata helpers;
those were not forced through the synchronous path.


Tenth step (step 65): removed the duplicate option/resource metadata layer.
ComponentContract no longer exposes Options/Resources dictionaries or accepts
those metadata arguments. Removed AddOption/AddResource from component registration,
ComponentOptions, ComponentResources, ComponentOptionMetadata,
ComponentResourceMetadata and ComponentConfigurationPreflight. This does not remove
application/workflow resources or typed component options. Factory code still uses
BindConfiguration and GetRequiredResource/GetResource; component constructors own
semantic validation. Configuration failures can now occur after earlier components
have activated, and build-failure cleanup releases them and owned resources.
Malformed present optional resource keys now fail at GetResource; absent/null
optional references remain valid. Native FlowValue port checks remain before activation.

This supersedes earlier entries describing passive option/resource schemas.
Port metadata, InputShape and remaining registration builders still exist and are
the next simplification target. Keep actual port names, directions, block bindings,
unique instance addresses, link limits and processing safety while removing duplicate
schemas. No visibility restrictions, constants, UI or vendor integrations were added.

Validation: Release build passed with zero warnings/errors. All 4,656 local test
executions passed across 76 TRX reports in artifacts/remove-config-metadata-20260913.
This includes malformed/missing resource reference checks and factory configuration/
resource failure cleanup tests. The source API baseline was reviewed and refreshed;
the full run verified it normally. External/container/acceptance/TSql integration
suites were excluded as in previous checkpoints.


### Eleventh step (step 66) — remove descriptive input schemas

Removed InputShape from ComponentContract, ComponentPortMetadata and both registration
builders; removed ValidateInputShape/CreateFlowValueInput and shape overloads. All
component registrations now declare native ports without repeating their payload
schema. Removed FlowValueShape/FlowValueShapeValidationResult and the five standalone
FileSystem/Storage/Sessions/Metrics/Projection input-shape helper files, which no
longer had production consumers. IFlowValueMaterializer now requires only Materialize;
JsonFlowValueMaterializer constructors take error code/category only. Actual conversion,
domain validation, FlowError and original exception diagnostics remain unchanged.

Retained port names, block selectors, kinds, link cardinality, processing capabilities,
factory ownership and unique instance addressing. The remaining port registration
records still serve link compilation and native-block validation; this step does not
claim to remove all registration builders. Updated current authoring/migration docs.
The old shape inspection tests were removed; canonical link compilation, deferred
factory/selector execution and block ownership tests remain. Reduced duplicate
shaped/unshaped cases and updated the expected number of registration overloads.

Validation: Release solution build passed with zero warnings/errors. The full local
run produced 76 TRX reports and 4,636 executions: 4,634 passed, two API surface tests
still expected the removed HasInput overload. Corrected those expectations and reran
the entire Composition suite: all 275 passed. No production changes followed that
full run. Reviewed and refreshed the API baseline before full regression, which
verified it normally. Evidence: artifacts/remove-input-shape-20260913,
artifacts/remove-input-shape-regression.log and artifacts/remove-input-shape-focused.log.
Do not describe the original TRX folder as all green. External/container/acceptance/
TSql integration suites remain excluded. No publication or consumer migration.


### Step 67 — direct registration for remaining synchronous standard factories

Migrated Serialization's six components, Timers Delay/Throttle/Debounce, and Routing
Correlation to the existing ComponentContract.CreateStandard API. Deleted ten
Configure callbacks and made their existing factories internal for direct declarations.
No new framework API. Serialization's helper no longer takes unused input/output
CLR type parameters; Storage's helper likewise keeps only its actual options type.
Storage remains on the builder path because all four factories are asynchronous and
own additional resources. MQTT and Sessions likewise retain their current ownership.

There are now 25 direct standard registrations and 19 remaining callbacks: eight
async/additional-owner standard factories and eleven custom-port registrations.
The remaining builders are not yet deleted. Next: migrate the eight standard factories
with their existing async/disposal behavior, then remove custom-port builder dependencies.

Validation: Release solution build passed with zero warnings/errors. All 4,636 local
executions passed in 76 TRX reports, including normal source API baseline verification
following the reviewed update. Evidence: artifacts/direct-standard-remainder-20260913
and artifacts/direct-standard-remainder-regression.log. External/container/acceptance/
TSql integration suites remain excluded. Whitespace checks passed. No publication.


### Step 68 — finish built-in direct factory migration

All 44 official component registrations now use CreateStandard or CreateNative;
no component package refers to either runtime registration builder. Removed the
eight remaining standard callbacks (MQTT, Sessions, Storage) and eleven custom-port
callbacks (sources, timers, file-system, retry, join, session replay, MQTT).
CreateStandard now supports asynchronous nodes and asynchronous additional-owner
activations. CreateNative reads declared names from the node's native Ports,
including controls and events; no second queues or reflective discovery. Standard
shape validation and native adaptation share one activation/cleanup implementation.
Factory identity, completion and additional-resource disposal remain explicit.

Deleted all four UseStandardFactory overloads and AddStandardPorts from the old
builder. Tests/benchmark callers moved to direct standard registration. Async tests
cover deferred construction, native identity, explicit completion, disposal exactly
once, and preservation of node/resource cleanup failures. Removed duplicate tests
of the now-deleted standard builder path. Updated the family convention test to
recognize CreateNative and current documentation/examples.

Important remaining work: RuntimeComponentRegistrationBuilder and
RuntimeComponentBindingBuilder still expose the low-level custom/instance callback
API through AddComponent(type, configure) and ComponentContract.Create. Tests and
benchmark fixtures still use it, including selected/aliased and multiple event ports.
Do not claim that these types have been deleted. Next migrate those uses to direct
contracts/native ports or explicit ComponentInstance factories without removing
aliasing, multiple events, cardinality, control lifetime, or failure-cleanup coverage;
then delete the remaining builder API and obsolete identity/binding machinery.
User has authorized this remainder; no additional approval question is needed.

Validation: final Release solution build passed with zero warnings/errors. Final
full local regression passed 4,625/4,625 executions in 76 TRX reports, including
normal API baseline verification after review/update. Evidence:
artifacts/direct-native-final-20260913, artifacts/direct-native-final-build.log and
artifacts/direct-native-final-regression.log. Earlier direct-native-20260913 run had
one convention-test failure for the newly introduced CreateNative name; it was
corrected before the final run. External/container/acceptance/TSql integration tests
remain excluded. No consumer project migration or publication. Whitespace clean.


### Step 69 — delete remaining mutable registration builders

Deleted RuntimeComponentRegistrationBuilder and RuntimeComponentBindingBuilder,
AddComponent(type, configure), mutable factory-mode state, ComponentFactoryMode,
ComponentBindingIdentity and the delegate/binding comparison machinery. IServiceCollection
now registers an existing ComponentContract only; repeated use of that object remains
idempotent and distinct contracts with the same type fail without changing registrations.
Typed ComponentContract.Create decorates an existing registration with handles/options.

Native component declarations remain CreateStandard/CreateNative. The selected-block
case uses CreateBound and an immutable ComponentPortBinding list: each name, direction,
selector and cardinality is declared once. This is a fixed value, not a replacement
mutable builder or registry. It preserves aliases, controls, multiple named event
sources, normal outputs named Events, selector ordering, lazy activation and native
block identity. Binding lists are snapshotted; duplicate names fail before factories run.
Already assembled ComponentInstance factories use the existing contract constructor.
Sync/async nodes and additional owners retain initialization-failure cleanup and
original exception details.

Migrated tests, samples, benchmarks and package-consumer fixture source. Reworked
builder-only tests into direct registration/binding tests, preserving ownership,
control draining, aliases, named events, cardinality, snapshot and null-selector checks.
Removed obsolete tests of mutable configuration and multiple factory modes. Current
README/authoring/API examples and migration guidance now show fixed direct declarations.
No compatibility builder has been retained in production or test helpers. Searches
found no old builder/factory-callback usages in src/samples/benchmarks/eng C# source.
Package consumer fixture source was migrated; fresh package-only qualification was not run.

Initial local regression: 4,608 passed and two convention tests failed (one stale
port-reflection assertion and one sample/fixture source expectation). Corrected the
assertions, migrated package fixture source, and all 24 focused convention tests and
249 Composition tests passed. Final regression evidence follows below.


Final step 69 validation: Release solution build completed with zero warnings and
zero errors. The second local solution run produced 76 TRX reports: 4,608 passed
and two failed out of 4,610 executions. Both failures were stale package-consumer
fixture source assertions (old HasInput/HasOutput/AddComponent syntax), not runtime
failures. Updated those assertions and reran the complete Release test project:
167 passed, zero failed. The focused Composition suite passed all 249 tests and
the focused convention suite passed all 24 tests. All runtime tests passed in both
solution runs; neither full-run TRX directory is presented as an all-green run.
The normal public API baseline check passed without acceptance enabled.

Evidence: artifacts/remove-builders-final-build.log,
artifacts/remove-builders-final-20260913,
artifacts/remove-builders-release-final.log,
artifacts/remove-builders-focused-final.log and
artifacts/remove-builders-conventions.log.
Acceptance, container, external-smoke and TSql integration suites were excluded.
No package publication or fresh package-only consumer qualification was performed.
Whitespace checks are clean.


### Step 70 — registration closeout

Corrected the current-status headers and transfer continuation note. Fresh local
candidate 0.0.0-local.20260913.registration.2 contains 63 packages; target assemblies
and archive hashes are recorded in verification.json (ASP.NET Core telemetry targets
net10.0 only). An isolated package-only net8.0 consumer passed JSON, direct/code-first
construction, resources, health, durability and separate-process restart recovery.
No project references or external consumer edits were used. The fixture needed a
missing Definition namespace import.

The first consolidated regression found a real shutdown race: a cleanup fault could
replace the initiating RequestReply fault. A deterministic reentrant timer-failure
case reproduces it. Publishing the initiating block fault before cancellation and
cleanup fixes it; all 52 RequestReply tests passed. Repacked with a new candidate
version after the fix. Final consolidated regression: 76 reports, 4,610 passed,
zero failed. External/container/acceptance-category/TSql suites remain excluded.

Evidence under artifacts/registration-closeout-20260913/: verification.json,
regression-final/, bridge-repro.log, bridge-fixed.log and acceptance-net8-verified.log.
The initial package restore's vulnerability lookup could not reach NuGet; the second
pack used NuGetAudit=false, so this is not vulnerability-audit evidence. Pack emits
an expected non-packable sample warning. Dependency restoration for the isolated
consumer succeeded with network access; later retries reused those downloaded
archives. Nothing was published. Next: an Open/Close Gate that drops closed-state data.


### Step 71 — Gate component using direct registration

Added GateNode to Routing and flow.gate to AddRouting via one CreateBound factory.
The user selected Open/Close signal inputs and dropping values while closed.
InitiallyOpen defaults to false; BoundedCapacity defaults to 128. The original
message is forwarded while open; existing error envelopes bypass closed-state
filtering under the normal FlowNode policy. No payload conversion, retained queue,
Gate builder/descriptor or new project was introduced. Direct C# uses new GateNode.

FlowNode now supports terminal controls through the existing FlowControlInputs:
shared instrumentation, bounded admission, output drain before control completion,
and joining/cancellation on fault/disposal. Nodes without controls keep their
existing completion task. Tests cover closed dropping, original envelopes, errors,
initial state, invalid capacity, real backpressure/fault release, native ports,
input completion, feedback to Close in FlowApplication, configuration activation,
and completion/fault with an active asynchronous control.

Release build: zero warnings/errors. Routing: 115 passing tests per target (net8.0
and net10.0); Core Nodes: 324 per target. Hosted configuration tests passed. The
first whole-solution run found two stale README/registration-matrix conventions;
updated them for contract terminology and the direct Gate registration. Reviewed
and accepted additive public API changes; the normal baseline check then passed.
Final whole-solution local regression: 4,630 passed, zero failed, 76 TRX reports.
This includes the focused suites; do not add their counts again.

Evidence under artifacts/registration-closeout-20260913/: gate-solution-build.log,
gate-runtime-verified.log, gate-core.log, gate-composition.log,
gate-conventions-complete.log, gate-regression-final/ and gate-tests.json.
The same external/container/acceptance-category/TSql exclusions apply. The verified
step-70 package candidate predates Gate; Gate validation is source-suite execution,
not a claim that Gate is included in that candidate. No publication or external
consumer changes. Detailed review: docs/reviews/2026-09-13-registration-closeout-and-gate.md.


### Step 72 — Declare native ports in the factory context

Added AddComponent(type, factory) with context.HasInput/HasOutput/HasEvents.
Gate now constructs its node and declares all five ports in the same callback;
RoutingComponents.Gate and its CreateBound declaration are removed. Registration
stores a type/factory pair, not a new ComponentContract or definition builder.

Composition and Engine now construct instances, validate native port declarations,
and compile links using each actual instance's ports. Port names may differ between
instances of the same type. Invalid declarations/links dispose newly created nodes;
shared owned nodes are rejected without disposing the original application. Engine
plans no longer carry a contract catalog, and the redundant built-component wrapper
was removed. Legacy typed/static-inspection APIs remain supported separately.

Validation: Release solution build passed with zero warnings/errors. Final configured
source regression: 4,640 passed, zero failed, 76 TRX reports. This includes direct
registration, custom names, conflicting registrations, failure cleanup, shared-node
ownership, Gate hosting, Engine, and normal public API baseline verification.
The public API baseline was reviewed/updated. Documentation examples now lead with
the direct callback. Evidence: artifacts/context-registration-20260913/build-final.log,
regression-final/ and verification.json. Acceptance, container, external-smoke and
TSql integration suites remain excluded; no new package candidate was qualified.

Remaining: migrate the 44 older typed registrations and their async/additional-owner
cases before removing ComponentContract/CreateBound globally. This step removes
those requirements from the new runtime registration path; it does not claim global
contract deletion or new external consumer acceptance.


### Step 73 — Async and additional ownership in context registration

Migration preparation: the direct AddComponent callback can now asynchronously
return IFlowNode. Context.SetLifetime supplies an optional explicit completion task
and additional disposal callback, reusing ComponentInstance lifetime handling.
Both callback shapes share activation/validation. Registration stays lazy and
idempotent, and sync/async type conflicts are rejected before DI mutation.
No new contract, builder or activation wrapper is required in the direct callback.

Six new tests cover awaited construction, laziness/idempotency, successful ownership
cleanup, cleanup after invalid declarations, preservation of node/resource cleanup
failures, sync/async registration conflicts, and explicit completion. Factory-local
resources remain factory-owned until a node is returned; failed constructors must
clean up what they acquired. Borrowed host resources must not be disposed here.

Release build: zero warnings/errors. Final local source regression: 4,646 passed,
zero failed, 76 reports. Public API baseline updated and normal verification passed.
Evidence: artifacts/async-context-registration-20260913/build.log, regression/,
verification.json. Same external/acceptance/container/TSql exclusions; no package
qualification, external consumer edits or publication.

The 44 typed registrations are still present. Next migration must reconcile their
code-first definition registrations with DI without introducing duplicate type
registrations, and preserve HTTP's parallel processing capability. This step
completes async/additional ownership prerequisites, not the family migration itself.


### Step 74 — Routing migrated off component contracts

Deleted RoutingComponents and its Window/Correlation/Join ComponentContract
properties. AddRouting registers the node factories directly; each declares its
native Input/Output/Events or Left/Right/Output/Events on context. Typed AddWindow,
AddCorrelation and AddJoin remain, including configured capture overloads and typed
port handles. Their callbacks are identical to JSON/DI callbacks, not a second
factory implementation. Gate remains on its already migrated direct path.

ApplicationSetup carries an immutable ComponentFactories dictionary. The existing
WorkflowDefinitionBuilder can attach a factory with configuration/handle creation,
without an additional registration class. Core construction and Engine merge this
with DI, accepting identical delegates and rejecting conflicts. Definition failure
does not commit the factory/component. Setup export and the portable-only build
path reject in-process callbacks. Engine revision comparison detects changed factory
delegates for each workflow; unchanged delegates retain the existing revision.

Release build: zero warnings/errors. Full configured source regression: 4,651
passed, zero failed, 76 reports. Added coverage for typed Window execution both
with and without family DI, failed definition rollback, factory snapshot immutability,
DI conflicts before activation, and revision callback changes. Existing hosted
Window/Correlation/Join/Gate tests passed. Reviewed the public API removal/additions
and updated the baseline; its normal check passed in the final suite.
Evidence: artifacts/routing-context-migration-20260914/build3.log, regression/,
verification.json. Same external/container/acceptance/TSql exclusions. No new
package qualification, publication or external consumer modifications.

41 legacy typed registrations remain. Next migrate remaining component families
onto this same shared definition/DI factory path, preserving async resource ownership
and HTTP parallel-processing capability. ComponentContract/CreateBound are not yet
globally removed. RoutingComponents source references must migrate to the retained
AddWindow/AddCorrelation/AddJoin methods; standalone node usage is unchanged.


### Step 75 — Assertions, Payloads and Validation migrated

Removed AssertionsComponents, PayloadsComponents and ValidationComponents and their
three ComponentContract properties. AddAssertions/AddPayloads/AddValidation now
register callbacks that construct nodes and call context.HasInput/HasOutput/HasEvents.
Typed AddAssertion/AddPayloadInspection/AddJsonSchemaValidator retain existing
options, optional configuration behavior and fluent capture overloads; they embed
those same callbacks. No additional framework API or registration layer was added.
Expression engine, optional context factory, schema selector and clock remain
host-owned; node algorithms and result/error envelopes are unchanged.

Registration tests now inspect activated native ports rather than static metadata.
Six new execution cases verify typed definitions with and without family DI across
all three families. Existing hosted assertion and validation behavioral tests pass.
Updated package READMEs and migration guide, including removal of obsolete UI hints.
The first full run found an old README contract-wording convention; updated that
convention for all four migrated families (including Routing), then reran the suite.

Final Release build: zero warnings/errors. Full configured local source regression:
4,657 passed, zero failed, 76 reports. Reviewed API removals and updated the baseline;
its normal verification passed in the final run. Evidence:
artifacts/component-context-migration-20260914/build-verified.log,
regression-final/ and verification.json. Same external/container/acceptance/TSql
exclusions apply. No package qualification, external consumer changes or publication.

38 legacy typed registrations remain. Continue family migration using the existing
shared definition/DI callback path. Async owned-resource families and HTTP parallel
processing still require preservation of their specific behavior. Global removal
of ComponentContract/CreateBound remains unfinished.


### Step 76 — All remaining 38 builtin registrations migrated

All 45 builtin registrations across 19 families now use direct context factories.
Removed the remaining 15 contract containers; retained 44 typed AddX methods and
55 capture overloads. Typed definitions and DI share callbacks. Async Sessions/
Storage ownership and HTTP parallel relaxed-order processing remain covered.
No builtin legacy contract/activation usage remains under src/Components.
Legacy custom-template APIs elsewhere remain a separate cleanup boundary.

Release build: zero warnings/errors. Final configured local regression: 4,688
passed, zero failed, 76 reports. Added 29 resource ownership cases and two HTTP
profile cases. Fixed a test-only Gate live-output subscription race. Reviewed API
baseline and updated all family registration documentation. Across 34 captured
production C# files, 3,478 -> 3,622 lines: this is structural simplification, not a
net line reduction.

Fresh candidate 0.0.0-local.20260914.context.1 produced 63 packages. Independent
package-only consumers passed on net8.0 and net10.0 (45 factories, typed
Serialization roundtrip); existing comprehensive package-only acceptance passed
on net8.0 including SQLFile restart. No external services, publication or external
consumer adoption. Local regression exclusions remain Acceptance,
ContainerIntegration, ExternalSmoke and TSql.IntegrationTests.
Evidence: artifacts/all-context-migration-20260914/verification.json and logs.
See docs/reviews/2026-09-14-builtin-context-registration-complete.md for scope.
Next: audit and remove remaining legacy custom-template APIs after migrating their
necessary samples/tests. There are zero builtin registrations left to migrate.

### Step 77 — Obsolete registration API cleanup complete

Removed all component contract/template APIs, activation wrappers, contract maps
and the empty AddFluxFlowComponents bootstrap. Custom samples, tests, benchmarks
and package acceptance now use direct context factories. Actual instance ports
are the sole compiler input. Resource contracts, typed AddX methods, async
ownership, processing profiles and runtime behavior remain supported.

Production C#: 846 removed / 162 added, net -684 lines across 18 changed paths.
Final local regression: 4,627 passed, zero failed/skipped, 76 reports. The 61-case
reduction from step 76 removes/replaces obsolete API-only assertions; retained
runtime scenarios were migrated. An unfiltered attempt encountered missing SQL
and container prerequisites and is separately recorded, not claimed as passing.

Final candidate 0.0.0-local.20260914.cleanup.2 contains 63 packages. All-family
package consumers and comprehensive SQLFile restart acceptance pass on both
net8.0 and net10.0. Public API baseline, current documentation and examples are
updated. Evidence: artifacts/legacy-cleanup-20260914/verification.json.
See docs/reviews/2026-09-14-registration-cleanup.md for details and limitations.
No internal registration cleanup remains. External consumer adoption and release
publication remain separate, unperformed work.

### Step 78 — Lazy FlowValue and local conversion complete

FlowValue is now an abstract class exposing Json/CreateJson with a thread-safe
lazy owned representation; JsonFlowValue handles dynamic JSON. Removed the old
From/Deserialize/Kind helpers and all framework materializer/result/base APIs.
HTTP/MQTT requests and FlowError inherit FlowValue. Components own conversion and
generated metadata; canonical FlowMessage has one payload with explicit IsError.
Persisted envelope shape and generic adapters remain. Capture prepares JSON and
converts a payload conversion failure to portable error data; actual store failure
remains fail-closed. Non-captured typed forwarding does not force JSON.

Final Release regression: 4,630 passed, zero failed/skipped, 76 reports. Candidate
0.0.0-local.20260914.value.2 contains 63 packages. All-family independent consumers
and comprehensive SQLFile restart acceptance pass on net8.0/net10.0. Source API
baseline, samples and current documentation are updated. No publication or external
consumer adoption. See docs/reviews/2026-09-14-lazy-flow-value.md and
artifacts/lazy-flowvalue-20260914/verification.json for evidence and scope limits.


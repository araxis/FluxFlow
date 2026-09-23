# Project instructions

## Typed configuration values (2026-09-22)

OneOrManyLinks is specialized for port values; no generic OneOrMany<T> was added.
It owns the existing strict section parser and returns LinkConfiguration items.
Internal LinkRegistration retains native keyed DI identity/conflict/alias behavior.
Bundled component DTOs expose fixed ports with this type and bind using
ReadConfiguration<T>, a narrow public-writable top-level DTO reader. Other properties
use the native binder with strict conversion; no recursive union binding or custom
catalog is provided. Native automatic port discovery still reads the same parser,
including dynamic Router ports and custom registrars. Do not add a parsed-model cache.
ExpressionSource is now a scalar-bindable immutable value with Value and instance
Read(baseDirectory); Mapper.Expression and Router.Routes use it. Inline/file rules
remain, plus explicit base64: strict UTF-8. Bare Base64 is not guessed; decoding is
not recursive. Files/decoding/compilation occur at Build, not per message. Link
conditions remain inline. Core/components are unchanged. Full Release: 1,850 passed;
API baseline updated; isolated Configuration/engine consumer passed .NET 8/10.
See docs/reviews/2026-09-22-configuration-value-types.md.


## Explicit expression engine registration (2026-09-22)

Bundled engines now expose AddJsonataExpressionEngine / AddCSharpExpressionEngine,
each with bool useAsDefault = false. Old AddJsonataExpressions/AddCSharpExpressions
methods are removed; update callers rather than adding wrappers. The flag registers
the same stateless engine instance as an unkeyed IFlowExpressionEngine for links
without Engine. False leaves any default untouched; multiple true flags use native
DI's last-registration-wins rule. Explicit link keys never fall back. Mapper/Router
still require Engine. No application/workflow ExpressionEngine setting is restored.
Compile helpers and Core stay unchanged. All 1,800 Release test executions and the
isolated Configuration/engine consumer on .NET 8/10 passed. API baseline updated.
See docs/reviews/2026-09-22-expression-engine-registration.md. No commit/publication.

## Core.Configuration folder removed (2026-09-22)

CompositionConfigurationException now belongs to FluxFlow.Configuration (namespace
and assembly). Native resource helpers live in FluxFlow.Core.Application:
ResourceRegistrationExtensions (renamed from FluxFlowServiceCollectionExtensions)
and FlowResourceExtensions. Method signatures, DI ownership, address validation and
lookup behavior are unchanged. Core.Configuration has no remaining folder/namespace;
do not restore a compatibility wrapper. This supersedes old resource-location notes.
Imports, package consumers, docs and API baseline are updated. Core has 47 C# files.
An obsolete source-directory release test now checks compiled Core friend attributes,
retaining the prohibition on Configuration accessing Core internals.
All 1,782 test cases pass after the Release test rerun; isolated Core and Configuration
packages pass .NET 8/10. See docs/reviews/2026-09-22-core-configuration-boundary.md.
No new project/dependency or commit/publication.

## Core boundary and event cleanup (2026-09-22)

Reviewed the remaining event/Hosting/expression/diagnostics candidates. Keep the small
native hosting adapter, the two shared expression contracts, and native runtime
instrumentation in Core; engines, configuration and exporters remain external.
Do not add forwarding packages merely to relocate these responsibilities.
FlowEvent reading now parses values without creating temporary envelopes. Attribute/
dimension serialization uses JsonElement and shared JSON options without intermediate
FlowValue wrappers. Preserve snapshots, invariant fallback, error rejection and the
existing canonical/typed timestamp distinction. No public API or production type change.
The source baseline now counts three existing declarations previously swallowed by
an attributed multiline signature; its general scanner limitation is not fixed here.
Final Release suite: 1,782 passed; isolated Core package passed on .NET 8/10.
Core remains 48 production C# files. Removed stale Core README revision/update-host
paragraphs. See docs/reviews/2026-09-22-core-boundary-and-events.md. This closes the
three scoped audit questions; further restructuring needs concrete evidence.
No commit/publication.

## Address parts belong to ApplicationAddress (2026-09-22)

ApplicationAddress exposes WorkflowName, ComponentName, PortName and ComponentAddress.
Application and Configuration use these instead of dotted-string slicing or segment
indexing. Absent parts return null; workflow-scoped resources have a WorkflowName,
system ports have a PortName but no workflow/component. Value remains the canonical
DI key; parsing, casing and equality are unchanged. IConfiguration colon-path parsing
remains in Configuration. No endpoint model or lookup cache was introduced.
All 1,778 Release executions and isolated Core/Configuration consumers on .NET 8/10
passed. Public API baseline updated for four additive properties. See
docs/reviews/2026-09-22-address-parts.md. No commit/publication.

## Cycle detection only (2026-09-22)

FlowDependencyOrder.TryCreate is replaced by internal FlowCycleDetection.HasCycle.
Its only production consumer needed cycle validation, not sorted topological layers.
Application.Validate calls it directly; the private GetDependencyOrder wrapper is gone.
Preserve duplicate-edge handling, empty/disconnected graphs, invalid endpoint/node
rejection, and exclusion of TerminalInputs from business cycles. No execution-order
contract or public API was added. Eight existing algorithm cases now test cycle
behavior; integration control/reply paths remain covered. Full Release suite: 1,764
passed. See docs/reviews/2026-09-22-cycle-detection.md. No commit/publication.

## Deep cleanup — unused Core retry engine removed (2026-09-22)

Core.Resilience is physically removed. Its custom RetryPolicy/Schedule/Planner/
StateMachine/Executor/jitter types had no production consumers; the actual Retry
component already uses Polly and remains unchanged. Do not restore a parallel retry
engine or move these unused helpers elsewhere. Removed their 11 tests per target;
actual component retry/request-reply tests and isolated package consumer remain.
ApplicationAddress.TryReadJsonValue also had no callers and is removed. Address
construction/resolution and native resource lookup remain. AddComponentFactory now
contains its own implementation instead of forwarding to a one-caller private method.
Unused central version entries for Configuration.Abstractions, Logging.Abstractions
and Options were removed; their normal transitive dependencies are not prohibited.
Full Release suite: 1,764 passed. Core and Retry isolated consumers passed .NET 8/10.
See docs/reviews/2026-09-22-deep-cleanup-audit.md for reviewed boundaries and follow-ups.

## Request/reply coordination simplified (2026-09-22)

PendingExchangeCoordinator and its options/start/feedback/completion/status models
are physically removed. FlowRequestReply retains its public signatures and is used
by Retry and RateLimit. Internal PendingRequests owns only active waiters and deadlines,
using a single TimeProvider timer and key-only priority queue. Do not restore generic
context/outcome APIs, settled-key history or FaultAll without a concrete requirement.
Fresh correlations plus expected reply address match requests; preserve timeout across
admission/reply, cancellation token and exception identity, capacity, atomic completion,
asynchronous continuations, timer failure draining and cooperative send cleanup.
This is runtime coordination, where synchronization remains necessary. It is unrelated
to RabbitMQ exchanges or application-level request/reply. Generic-only tests are removed;
runtime deadline/race/cleanup scenarios now use the public FlowRequestReply API.
Full Release suite: 1,786 executions passed. Isolated Core package consumer passed
on .NET 8/10. See docs/reviews/2026-09-22-request-reply-simplification.md.

## Processing-profile layer removed (2026-09-22)

User explicitly approved removing unnecessary processing abstractions after a consumer
audit. Core.Configuration.Processing is physically removed: profile/enums, capabilities,
settings, mapper interface/default implementation, resource type and ResolveProcessing.
No current component adapter consumed the resolver. Configuration no longer registers
processing.profile automatically. Do not restore this layer or move it to another project.
Adapters use their own supported settings and native Dataflow/component options;
components keep their runtime validation. Generic resource registration, addressing,
snapshot and ownership coverage remains, using resource-specific test DTOs.
This supersedes ALL earlier instructions preserving processing profiles or host mappers.
Also removed the unused old Composition compatibility XML and unused direct Core
Logging.Abstractions/Options references; hosting retains its standard dependencies.
See docs/reviews/2026-09-22-processing-profile-removal.md.

## Link identity in DI (2026-09-22)

Configuration LinkConfiguration.RegisterPort registers links directly in the staged
IServiceCollection. The temporary linked dictionary is removed. A private immutable
LinkKey identifies canonical source/target, condition, parameter signature and engine;
the DI key pairs identity with delivery/fallback for conflict validation. Existing
keyed LinkFactory descriptors are the only registration lookup. Equal declarations
are skipped; conflicting policies throw. One unkeyed alias resolves each canonical
keyed delegate, preserving FlowApplication's single GetServices<LinkFactory> execution
path. No workflow-key aliases, new runtime table or provider are introduced.

## Batch configuration adapter (2026-09-22)

FluxFlow.Configuration.Batching contains data-only BatchConfiguration and AddBatch.
Registration validates/snapshots BatchSize, MaxWait and OutputCapacity, then registers
a deferred Batch factory and native Input/Output/named Events ports. Build resolves
the host TimeProvider or uses System. No Core/Batch runtime changes. Configuration
now depends on the Batch package; isolated consumer packaging includes it.

## Current documentation entry point (2026-09-22)

docs/README.md separates the current reading list from historical designs. Current
resource setup uses per-type IConfigurationSection DTO binding, with no document API.
Repeated Build reuses Source and its cursor. Older preservation statements below
remain historical and are superseded by the latest removal notes and current code.

## Latest link-section cleanup (2026-09-22)

LinkConfiguration reads IConfigurationSection directly. ReadLinkValue and the
link-settings JSON round trip are removed. Only bound expression parameters use
ConfigurationJsonReader to preserve existing scalar/JSON conversion and Add-time
snapshots. Keep both link directions, arrays, field validation, atomic registration,
delivery/fallback, local engine selection and compile reuse. No public API changes.
See docs/reviews/2026-09-22-direct-link-sections.md.

## Latest registration cleanup (2026-09-22)

Removed the unused parallel raw-block component/input/output/signal registration
methods from FluxFlowServiceCollectionExtensions and deleted NonOwningDataflowViews
(DataflowBlockView, TargetBlockView, SourceBlockView). No callers remained outside
those helpers. Do not restore wrapper-based registration. Core.Application's native
AddComponentFactory/AddInputPort/AddOutputPort/AddEventPort is the sole component
path. AddFluxFlowResource and AddExternalFluxFlowResource remain unchanged in public
signature and keyed-DI ownership. See docs/reviews/2026-09-22-native-registration-cleanup.md.

## Latest boundary — no intermediate document model (2026-09-21)

The user explicitly approved removing the entire document-definition path and its
dependencies. This supersedes ALL earlier instructions retaining explicit document
APIs or deferring DocumentValidation. ApplicationDefinition, WorkflowDefinition,
ComponentDefinition, ResourceDefinition, DocumentValidation, canonical model helpers,
application document serializers/readers, FlowJson, and the document link compiler,
projection and diagnostic types are physically removed. Do not restore them.

Configuration traverses IConfigurationSection and each component/resource registrar
binds its own DTO, without a shared definition base. LinkConfiguration reads one port
property and returns LinkFactory delegates directly. It and the existing internal
condition compiler/parameter validation now belong to FluxFlow.Configuration/Links.
There is no whole-document scan or public link projection/model. Preserve input/output
syntax, relative/full addresses, conditions, Engine/Parameters, delivery/fallback,
transactional registration and per-provider condition caching. Core retains native
DI/Dataflow/runtime validation and message/value serialization.

Document-only tests and the document compiler benchmark are removed. Runtime parity
scenarios now test actual IConfiguration setup; native delivery/lifetime tests remain.
All 1,784 Release test executions and both isolated consumers on .NET 8/10 passed.
Configuration no longer has InternalsVisibleTo access to Core; keep that boundary.
See docs/reviews/2026-09-21-intermediate-document-removal.md. Historical reports may
describe removed APIs; they are not current architecture requirements.

Latest user decision: PropertyOrder is removed from ApplicationDefinition,
WorkflowDefinition and ComponentDefinition, including JSON capture/replay logic.
Do not restore document member-order tracking or editor metadata in Core.
Serialization writes nonempty Resources first, then Workflows/components; component
Type precedes settings. Empty Resources are omitted. Native JSON values/arrays remain
intact. UI metadata design is deferred. DocumentValidation remains unchanged.
Core and Configuration: 1,058 Release test executions pass on .NET 8/10; see
docs/reviews/2026-09-21-property-order-removal.md.

Latest section-discovery cleanup: Configuration reads the existing DI port-address
registrations, as runtime composition does; it must not treat arbitrary host keyed
native block services as FluxFlow ports. Regression coverage and all 224 Configuration
test executions pass on .NET 8/10. See docs/reviews/2026-09-21-port-discovery-cleanup.md.
The user explicitly deferred further changes to DocumentValidation; retain it for now.

## Latest expression/resource boundary (2026-09-21)

Expression engine selection is local: Mapper/Router retain Engine; conditional links
accept Engine beside Condition. ApplicationDefinition/WorkflowDefinition.ExpressionEngine
and their constructor arguments are removed, as is configuration scope selection.
Do not restore application/workflow engine defaults. An omitted link Engine retains the
host's unkeyed default; explicit keys resolve exactly through keyed DI and never fall back.
Keep string keys, not a closed framework enum. Engine requires a nonempty name and Condition,
is preserved in projection/compiled links, and participates in runtime link identity and cache.
ResourceInstanceDefinition is physically removed. ResourceDefinition is sealed raw document
JSON only; each resource registrar binds its own configuration DTO from IConfigurationSection.
DocumentValidation replaces DefinitionRules, delegates address validation to ApplicationAddress,
and keeps only document validation/copying. Unused canonical-property lookup helpers removed.
Full suite: 2,014 Release executions passed; build zero warnings/errors; public API baseline
updated. See docs/reviews/2026-09-21-local-expression-and-resource-config.md. This supersedes
all earlier notes about workflow engines and resource-instance document models.

## Latest condition compiler boundary (2026-09-21)

ApplicationConditionCompiler no longer accepts or stores ApplicationDefinition or
ApplicationAddress. Its sole Compile input selects the engine by name, with expression,
input type and bound parameters. Explicit document consumers (FlowJson.ApplyLinks and
ApplicationLinkCompiler) select the source workflow override/application default themselves.
Ordinary section setup already snapshots that choice during Add. No new selector/context
class; per-session caching and diagnostic behavior remain. Full Release suite passed
2,000 executions; public API baseline unchanged and passing. See
docs/reviews/2026-09-21-condition-compiler-boundary.md. No commit/publication.

## Latest processing cleanup (2026-09-21)

Definition-based BindConfiguration and ResolveProcessing are removed with
ComponentConfigurationExtensions and CompositionProcessingConfiguration. Their only
callers were legacy binding tests. Do not restore generic document-to-options JSON
serialization/property injection. ProcessingProfileExtensions.ResolveProcessing now
extends IServiceProvider and takes a resource reference, component address and explicit
capabilities. It preserves native resource resolution, capability checks, host mapper
overrides and default mapping. Combined supported capability flags are accepted.
Adapters bind their own configuration, optionally resolve a profile during construction,
and apply explicit options over profile defaults; nothing is automatically injected into
all component adapters. Full suite: 2,000 Release executions passed; isolated Core consumer
exercises the new API on .NET 8/10. API baseline updated. See
docs/reviews/2026-09-21-direct-processing-profiles.md. No commit/publication.

## Latest resource cleanup (2026-09-21)

Removed the unused definition-based GetResource/GetRequiredResource and AddProcessingProfile
APIs. ApplicationResourceRegistration is replaced by FlowResourceExtensions containing
only the unchanged GetRequiredFlowResource DI extension. Do not restore a document-to-DI
resource access path. Configuration owns section-to-profile binding. Explicit document
binding/serialization APIs otherwise remain. Resource lookup coverage now uses native DI;
tests for deleted optional/JSON-reference helpers were removed. Full Release suite passed
1,998 executions. API baseline updated. See docs/reviews/2026-09-21-resource-helper-cleanup.md.

## Latest setup boundary — direct IConfiguration (2026-09-21)

Ordinary AddFluxFlowApplicationAsync traverses IConfiguration sections directly.
Do not restore an intermediate ApplicationDefinition/WorkflowDefinition/ComponentDefinition
tree, root JSON serialization, or whole-document scan on this path. Component and
resource registrars receive original sections and register deferred native factories.
ResourceRegistration now belongs to FluxFlow.Configuration and takes IServiceCollection,
ApplicationAddress, IConfigurationSection; the Core definition-taking delegate is removed.
Typed component/profile DTOs remain appropriate for local binding and snapshots.
Only declared port values are converted for the shared link parser; bound parameters
retain the existing scalar conversion rules. Link engine names are captured at Add
from the source workflow, with one compilation session per provider at Build.
The public definition model, serializer, explicit reader and diagnostic compiler still
exist for explicit document callers; they are not runtime setup prerequisites.
Registration is transactional and frozen before descriptors are copied to the host.
Full Release suite: 2,004 executions passed; build zero warnings/errors; Core and
Configuration isolated package consumers passed on .NET 8/10. API baseline updated.
See docs/reviews/2026-09-21-direct-configuration-registration.md. No commit/publication.
This supersedes older instructions giving ResourceRegistration a definition parameter
or requiring ordinary setup to use ApplicationDefinitionConfiguration.Read.

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


FlowJson only applies links; its redundant Serialize wrapper is removed. Use
ApplicationDefinitionJson for ordinary document serialization or standard
JsonSerializer.Serialize(definition, options) for custom serializer settings.

Core.Mapping now contains only IFlowExpressionEngine and FlowMapContext. Removed
DelegateFlowMapper, DelegateFlowPredicate, ExpressionFlowMapper, ExpressionFlowPredicate,
IFlowMapper, IFlowPredicate and IFlowMapContextFactory: these had no production consumers.
Use plain callbacks and compiled delegates; do not restore an adapter/context-factory
layer. Engine default-compilation regression tests remain in FlowExpressionEngineTests.

Router's unmatched path is separate from errors: Unmatched receives original normal
messages only when no predicates match; inherited Output retains processing errors
and incoming FlowError messages. Setup registers Unmatched as a native named output
and reserves that name. All matching routes still receive the original message.
No first-match mode, priority ordering or automatic discard was added.

Router now has an adapter in FluxFlow.Configuration.Routing: AddRouter plus data-only
RouterConfiguration. Routes map output names to expression strings; flat named port
properties retain normal link syntax. Build compiles boolean delegates with the existing
engine and ExpressionSource, then constructs Router. Input/Output/Events and configuration
property names cannot be route names. No engine dependency belongs in the component.

Expression inputs follow one message-context contract everywhere: value, headers,
string messageId/traceId/correlationId/causationId and timestamp. CSharp exposes lazy
JSON dot access and DateTimeOffset; JSONata uses equivalent bindings and ISO time.
RouterOptions.Routes accepts Predicate<FlowMessage>; pass the original message.
Setup supplies engine-neutral callbacks using FlowMapContext.ForMessage, as Mapper
and conditional links do. Do not introduce engine knowledge into components.

Latest post-revision cleanup: `IApplicationDefinitionSource`,
`StaticApplicationDefinitionSource` and `ConfigurationApplicationDefinitionSource`
are removed. `FluxFlow.Configuration.ApplicationDefinitionConfiguration.Read` is
the synchronous IConfiguration-to-definition reader; ConfigurationJsonReader lives
in that optional project, not Core. ApplicationSetup and authored resource contracts were subsequently removed.
AddApplicationResources retains its registration transaction. No resource/component is constructed during registration.
Only the section-based ComponentRegistration in FluxFlow.Configuration remains.
The unused Core definition-based delegate and AddComponent extension were removed.

Read and follow `C:/Users/meisa/.codex/RTK.md` for shell command usage.

## Engineering principles — explicit user preferences

Apply SOLID and KISS together. SOLID guides responsibility boundaries,
extensibility and maintainability; KISS guards against overengineering, including
overengineering justified in SOLID's name. The user explicitly rejects mechanical
or dogmatic interpretations: neither an interface for every class nor an extra
abstraction merely to demonstrate compliance is wanted. Choose the simplest
correct, maintainable solution that meets actual requirements. Evaluate simplicity
in usage, implementation, responsibilities and structure, not just code size.
Explain concrete tradeoffs when principles pull in different directions; do not
silently sacrifice behavior or mix responsibilities for superficial convenience.

Give direct, evidence-based disagreement when a proposed approach or interpretation
would hurt correctness, simplicity or maintainability. Do not substitute agreement
with the user for technical evaluation. Conversely, do not invent hypothetical
requirements to justify extra infrastructure.

- Single responsibility: keep distinct reasons for change separate. Component
  behavior must not own application registration, naming or composition.
- Open/closed: support required variation through focused extension points;
  avoid central type switches or speculative extension frameworks.
- Liskov substitution: preserve behavioral contracts, including error handling,
  ordering, cancellation, backpressure and lifecycle, when changing abstractions.
- Interface segregation: consumers should depend only on the capabilities they
  need. Do not burden component contracts with setup or application concerns.
- Dependency inversion: keep policy independent of concrete infrastructure and
  composition decisions. Use focused abstractions where a real boundary needs
  them; SOLID does not require an interface or factory class for every class.
- KISS: choose the simplest clear, reliable implementation that preserves these
  principles and required behavior. Avoid unnecessary layers and concepts.

Design patterns are implementation tools, not goals or mandatory class diagrams.
Use a pattern only when it makes the actual solution simpler, clearer and reliable.
Prefer existing C#/.NET capabilities and native Dataflow blocks where they meet
the need. Delegates, Func/Action and DI registrations can implement factories or
strategies without dedicated factory/strategy class hierarchies. When working on
composition, consider existing keyed DI registration before introducing a custom
catalog. Do not duplicate a capability already provided adequately by the platform.
Do not inject a container into component logic or replace explicit dependencies
with service location merely to avoid a class.

## Current responsibility boundaries

The native keyed-DI migration is complete as of 2026-09-20; hot reload was subsequently removed from this minimum scope. This section replaces
the accumulated intermediate migration notes; see the plan and review for evidence:
docs/plans/2026-09-20-native-keyed-di-composition.md and
docs/reviews/2026-09-20-native-di-revision-ownership.md.
Ask any user questions in English only.

### Application construction and ownership

Each host instance runs one Application. Ordinary composition uses one host-owned
standard .NET provider. Component and port identity/lookup come from native keyed
singleton registrations by unique address. Do not restore an ordinary resource provider,
temporary port provider, custom lookup table, catalog, cache or endpoint wrapper.
ApplicationResourceScope, CreateLinkServices and per-workflow revision lookup snapshots
are physically removed. Ordinary Core/Application contains no BuildServiceProvider call.

Configuration reading registers factories and native port selectors; it constructs no
components or provider. ComponentRegistration(IServiceCollection, IConfigurationSection)
reads the section and registers ComponentFactory(IServiceProvider), returning IFlowNode
synchronously. The factory receives no Application, Workflow, configuration or cancellation
token. It constructs only the component with explicit dependencies. Components never
receive a service locator. There is no asynchronous component factory overload;
asynchronous activation belongs in startup. Missing factories reject staged registration
without changing the caller's service collection.

AddComponentFactory registers the canonical keyed IFlowNode singleton and ordinary
workflow/name discovery data. AddInputPort/AddOutputPort register keyed native blocks
selected from that component, lazily and once. AddEventPort uses the existing native
ComponentActivityBridge for address-stamped observations. Named events are routable and
can resolve before Build. Port discovery uses read-only addresses in DI, not duplicated
block storage or ComponentPortMetadata. The explicit signal registration option is a
keyed boolean; canonical message types come from the native registration contract.
ComponentPortMetadata, ComponentPorts and ComponentPortKind are removed, along with
Single/Multiple link cardinality. Duplicate keys/reserved names reject during Add.
ApplicationPortMetadata and ApplicationSystemOutputMetadata are also removed.
The compiler accepts ordinary address/type pairs for explicit system outputs.

Registered input/output blocks must not implement IDisposable or IAsyncDisposable.
Their component owns cleanup; DI owns the component. Reject disposable selector results
before DI captures them. FlowOutput retains pattern-based DisposeAsync/await using but
does not implement IAsyncDisposable. Do not add ownership wrappers or rely on duplicate
Dispose being harmless. A weak reference-identity registration guard rejects a component
reused under another key/provider before DI captures it; it is not an instance cache.

BuildApplicationAsync resolves the provider's singleton FlowApplication and delegates
composition. FlowApplication.ComposeAsync resolves resources, components and ports,
invokes links and validates; it does not register services or build a second provider.
Repeated Build returns the same application and does not rerun factories/links. It is
not a multi-application hosting feature. Build returns an unstarted application and checks
cancellation between factories. Failure stops/drains partial construction; native DI
still owns disposal. The host must dispose its provider, including after failed Build.

FlowApplication is a single class file, not partial. Composition methods live directly
in FlowApplication.cs. Use extensions only for genuinely independent helper behavior;
do not expose private lifecycle state or invent another class just to shorten the file.

AddFluxFlow() composes at host startup, then starts sources. The manual
AddFluxFlow(application) overload is removed. Explicit native FlowApplication registration
may configure link capacity. FlowApplication has no provider-taking constructor and
StartAsync does not construct components. Stop/dispose Application before provider
teardown. DI is the sole disposal owner for components, bridges and resources it creates.
The user explicitly accepts standard DI stopping on its first disposal exception; do not
restore a custom all-services disposal manager. Dependencies must be compatible with
singleton components and outlive their consumers through native dependency ordering.

### Runtime structure and routing

The configuration root represents Application, with workflows and resources. Workflows
process messages reactively; they have no Start/Stop lifecycle. FlowWorkflow contains
actual components, event bridge observations and necessary routing declarations only.
It has no Inputs/Outputs dictionaries, Add/RegisterInput/RegisterOutput/RegisterEvents
methods or application back-reference. Public FlowApplication.AddWorkflow and the manual
FlowExtensions.Workflow construction path are removed. Do not restore them.

LinkFactory receives only IServiceProvider and resolves canonical blocks/components by
address. Link/LinkFallback prepare routes under the application mutation
guard; Start connects routes and starts sources. Preserve native block identity checks,
predicates, fallback, delivery/rejection policy, fanout, backpressure and
completion dependencies. Workflow owns its route declarations; do not introduce
an application-wide parallel link table or a new LinkBuilder/context class.
FlowLink exposes native Input/Output and separate InputAddress/OutputAddress.
FlowRouter receives native source, address, routes and settings, not Application/Workflow.
Control classification compares native blocks with IFlowNode.TerminalInputs, not names.

Application/workflow GetInput/GetOutput, application.SendAsync and direct output
LinkTo/OtherwiseTo facades are removed. Native Dataflow APIs remain available to component
authors; do not add a public manual-ingress replacement. GetComponent is only a DI
composition extension. Test-only inspection helpers may inspect actual DI data; they
must not recreate a removed production construction path or replacement provider.

Keep lifecycle task publication before callbacks: Compose/Start/Stop/Dispose use RunOnce
under Gate. A task ??= OperationAsync() rewrite can reenter before assignment. Precanceled
Start leaves the application startable; Fault/Stop closes admission and structural changes.
Application attempts all stop/drain/route cleanup operations and retains initiating errors.
This is distinct from the approved standard DI disposal failure behavior.

### Component and configuration responsibilities

Core defines component/execution contracts, envelopes/values, data transfer, lifecycle,
errors and the component-owned request/reply helper. Components implement native blocks,
local state and business behavior. Setup chooses instances/options, names/addresses and
links. RegisterPorts is not a component/base/IFlowNode responsibility; keep it removed.
Each component module has its own project under src/Components and the matching solution
folder. SOLID/KISS applies to usage and responsibilities, not just code size.

FluxFlow.Configuration reads IConfigurationSection directly, using separate data-only
DTOs and registration methods. Scheduler, Gate, Source and Mapper have adapters there;
component packages have no configuration/engine dependency. Configuration-to-options
conversion belongs in registration/factory methods, not the DTO. No activation contexts.
Source ValuesJson is JSON array text parsed during Add to preserve heterogeneous JSON
types through IConfiguration. Do not infer types from scalar configuration strings.
One source instance has one cursor; repeated Build does not restart it. Source has no
timer or implicit repetition; Scheduler is separate.

ResourceRegistration(IServiceCollection, ApplicationAddress, ResourceInstanceDefinition)
is invoked during AddFluxFlowApplicationAsync. It registers native factories, not instances,
and receives no provider/context. AddApplicationResources stages descriptors transactionally
into the host collection. Registration callbacks never run during ordinary Build.
Keep processing-profile validation, explicit settings and host overrides.

MapperOptions.MapAsync accepts FlowMessage and CancellationToken and returns
ValueTask<FlowValue>. Mapper owns WithValue lineage and processing errors. Optional
FluxFlow.Expressions.CSharp/Jsonata depend on Core, not Configuration. Standard
keyed IFlowExpressionEngine services compile once per component construction. Mapper files are read
at Build relative to one host-selected directory, never per message; repeated Build on
the same provider does not reread/recompile. No global cache or interpreter manager.
C# parameters: value, headers, messageId, traceId, correlationId, causationId, timestamp;
IDs are strings, timestamp DateTimeOffset. JSONata has equivalent $ bindings and ISO time.
C# uses read-only lazy views over FlowValue.Json, missing fields error, no eager type tree.
Keep documented Integer/Decimal/finite Double rules. JSONata gets fresh environment/DOM
per evaluation. Cancellation brackets synchronous evaluation, which is not preemptible.
Core IFlowExpressionEngine is shared by Mapper setup and ordinary links.
Its Compile<T> methods now return Func<FlowMapContext,T>. IFlowCompiledExpression<T>,
EvaluatingCompiledExpression<T> and the BoundCondition wrapper are removed. Preserve
compile-session reuse, bound parameters and the documented default Evaluate fallback.
Configuration.ExpressionCompiler is removed. Bundled engines implement typed compilation
directly; bool results must not use JSONata truthiness or string/number coercion.
FlowMapContext.ForMessage supplies original message, input/payload aliases and flattened
value/header/ID/timestamp variables. Existing bound parameters may override flattened
variables; the existing input/payload/message reserved-name rules remain unchanged.

### Current minimum boundary — hot reload removed

The user explicitly removed hot reload and revision replacement from Core on 2026-09-20.
This supersedes earlier requirements to retain candidate providers or rollback. Do not
restore revision hosts, snapshot providers, stable revision ports, ReloadAsync/ApplyAsync,
update planning, revision event streams or their resource registration context. A future
optional library may implement live reconfiguration; no replacement is requested now.

Ordinary FlowApplication and AddFluxFlow remain in Core.Application. Configuration loaders
are in Core.Configuration. Native DI owns one provider. Cancellation, partial-build cleanup,
startup/shutdown, routing and component errors remain. Application.DisposeAfterDrainAsync
and its revision-only stop bypass are removed. ComponentActivityBridge is ordinary
address-stamped observation and remains.

ResourceRegistration now lives in Core.Configuration.Resources and is shared by optional
Configuration setup and authored resource contracts. Its parameters are IServiceCollection,
ApplicationAddress and ResourceInstanceDefinition. It registers factories, not instances.
Configuration resource callbacks run at registration time. No
revision context, sequence/ID or host service locator is passed to registration.

FlowComponent, ComponentActivationContext, ComponentInstance, physical endpoint wrappers,
ComponentOwnership, FlowTopology, FlowExecution, WorkflowFactory, ComponentBindings,
ApplicationConstructionExtensions, ComponentGraphView and PortableApplicationExtensions
are removed. Do not restore them under new names. FlowJson serializes documents/applies
links to existing keyed ports; FlowJson.BuildAsync is removed. Native predicates use LinkFactory directly; no authored setup object remains.

Application-level request/reply is removed: no MakeFunction, FlowFunction, CallFunction,
app.RequestAsync, ApplicationPorts.SendAndReceiveAsync or hidden reply routing observation.
FlowRequestReply is retained for components (Retry/RateLimit) with native Request/Reply
ports. Any future function component is deferred. Do not add it in a cleanup.

Do not reinterpret these principles as permission for new managers, descriptors,
catalogs or layers. Say clearly when a change only relocates complexity. Keep historical
reports historical; current code and this section supersede intermediate migration notes.

## Latest output-policy decision

Latest output-policy cleanup: FlowWorkflow.DiscardedOutputs and the ordinary
DiscardUnmatched API/DI callback are removed, together with JSON policy parsing,
definition-handle declarations and revision discard-set propagation. No replacement
lookup or automatic sink is added to production. Unmatched messages retain the
existing routing error; build-time output validation and application error collection
are deferred. The old ApplicationPortRuntimeBuilder and its output-drain option were
subsequently removed with revision hosting. Ordinary routing error-isolation remains.
Tests use explicitly registered terminal consumers rather than workflow discard flags.

# GOAL: Add a Typed, Code-First Application Authoring DSL

## Status

- State: complete
- Created: 2026-08-08
- Clarified: 2026-08-08 — the C# DSL is a developer-only compiled-code path; C# export, JSON serialization, and designer interoperability are outside this goal
- Repository: C:\Projects\FluxFlow
- Execution started: 2026-08-08
- Execution completed: 2026-08-08
- Breaking changes: allowed when they produce a smaller and clearer final API
- Backward-compatibility aliases: not required
- Commit, push, pull request, package publication, and release: not authorized by this goal
- Required result: implementation, migration, focused and full verification, documentation, website content, and memory updates

## Execution Instruction

Execute this goal completely in the existing FluxFlow repository.

Do not stop after creating interfaces, prototypes, tests, or documentation. Implement the smallest complete production design, migrate the repository-owned consumers, run the required verification, and record evidence in this file.

Preserve all unrelated user work already present in the working tree. The repository is intentionally dirty from the durability, package-consumer restart acceptance, typed component port-binding, and HasInput/HasOutput/HasEvents rounds. Never reset, discard, overwrite, or silently reformat unrelated changes.

At the start of execution:

1. Read the repository instructions and memory files required for the touched areas.
2. Inspect the current working tree and identify overlapping edits.
3. Read this goal completely.
4. Use the dotnet-vertical-slice-senior skill as the architecture anchor.
5. Use graph-aware repository inspection before changing cross-project contracts.
6. Before writing or changing tests, use the mandatory code-testing-agent entry skill and follow its required workflow.
7. Update the Status section to in progress.
8. Keep this file current with decisions, scope adjustments, test evidence, and final results.

## Objective

Create a first-class C# application-definition authoring experience that is:

- strongly typed for component contracts, handles, ports, and C# predicates;
- visually close to the existing JSON application structure;
- flat and fluent, with no callback nesting for workflow scopes;
- capable of local and cross-workflow connections;
- explicit, predictable, and free of reflection or hidden runtime registration;
- fully compatible with the existing JSON definition format and JSON hot-reload path;
- capable of representing code-only behavior when the application is intentionally authored and hosted in C#;
- lightweight enough to preserve FluxFlow's in-process character.

The completed public experience must let application authors write ordinary C# without repeating component type identifiers or port names:

~~~csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var main)
    .AddWorkflow("audit", out var audit);

main
    .AddComponent(
        "source",
        SampleComponents.Source,
        options => options.Messages = ["alpha", "beta"],
        out var source)
    .AddComponent(
        "review",
        SampleComponents.Review,
        out var review)
    .AddComponent(
        "priority",
        SampleComponents.Sink,
        out var priority)
    .AddComponent(
        "standard",
        SampleComponents.Sink,
        out var standard);

audit.AddComponent(
    "events",
    SampleComponents.EventCollector,
    out var events);

source.Output.ConnectTo(review.Input);

review.Output
    .ConnectTo(priority.Input, when: static order => order.Priority)
    .ConnectTo(standard.Input, when: static order => !order.Priority);

review.Events.ConnectTo(events.Input);
priority.Events.ConnectTo(events.Input);
standard.Events.ConnectTo(events.Input);

var definition = application.Build();
~~~

This is a true code-first application definition. It is not another syntax whose purpose is to produce JSON. Building, registering, validating, compiling, revising, and executing this application must not call or round-trip through JSON.

The developer writes C#, compiles the application, and runs the built definition directly in memory. The C# builder may use the full appropriate power of C#, including typed objects, typed handles, delegates, closures, and component-specific builders. It has no responsibility to produce JSON.

## Product Boundary

FluxFlow must continue to support two distinct, equally valid application-authoring paths:

### Portable definition path

- JSON, files, configuration providers, remote configuration, and hot reload.
- Component type names, port addresses, and link conditions are represented in the existing portable schema.
- Conditions are portable expression strings compiled by the configured IFlowExpressionEngine.
- A UI/designer authors this portable JSON representation.
- The current JSON wire shape and configuration behavior remain supported.

### Code-first definition path

- The application graph is authored directly in C# through ApplicationDefinitionBuilder.
- This API is for developers writing and compiling C# source code.
- Components and ports are represented through typed contracts and handles.
- Links may use no condition, a portable expression string, or a synchronous typed C# predicate.
- The built definition is registered and executed directly in memory.
- Normal code-first startup does not serialize, parse, or project through JSON.
- The C# builder is not required to produce a portable or serializable result, even when a particular graph happens to use only portable features.
- Static C# registration through the existing AddFluxFlow definition path remains supported.

### Shared runtime boundary

The authoring paths converge at normalization, validation, and compilation:

~~~text
JSON source -> parse ---------+
                              +-> normalize -> validate -> compile -> runtime
C# builder -------------------+
~~~

They do not converge at serialization, UI editing, or source representation. “The same application” means that equivalent graphs produce the same validated runtime behavior. It does not mean that every in-memory C# behavior has a JSON representation.

The governing rules are:

> Successful execution of a code-first application must never depend on calling, projecting through, or round-tripping through JSON.

> The UI/designer creates and persists JSON definitions; it does not create, open, edit, or export compiled C# builder definitions.

> Exporting a C# builder result to JSON is not part of this goal.

Do not weaken the JSON use case to implement the C# use case. Do not constrain the C# use case to the JSON feature set when the application author explicitly chooses code-first hosting. Do not make JSON the internal interchange format between ApplicationDefinitionBuilder and the runtime. Do not add C# export, portability metadata, serializer adapters, or designer bridges in this round.

## Non-Negotiable Design Principles

### Simplicity

- Prefer the smallest cohesive design that satisfies the complete behavior.
- Avoid framework-building around the DSL.
- Avoid universal option bags, generic repositories, service locators, and indirect registries.
- Do not add a dependency unless the existing runtime and BCL cannot express the requirement cleanly.
- Do not add a new project unless a real package boundary requires it.

### Explicit behavior

- No reflection.
- No assembly scanning.
- No source generation.
- No runtime compilation.
- No dynamic invocation.
- No hidden string keys standing in for delegates.
- No JSON sentinel value that secretly refers to process memory.
- No global or static delegate registry.
- No ambient current-builder state.
- No mutation after Build.

### SRP and IOC

- Authoring contracts describe how a known component is represented in an application definition.
- Component-specific builders own component-specific configuration.
- Application authoring owns graph construction.
- Link compilation owns conversion from authored conditions to executable matchers.
- Runtime ports own delivery and invoke only the compiled matcher abstraction.
- JSON parsing/serialization owns only the independent portable JSON path.
- The C# builder has no serialization or designer responsibility.
- Hosting and dependency injection remain responsible for component implementation activation.

### Flat fluent authoring

- Keep workflow creation and component authoring at one or two levels.
- Do not introduce nested AddWorkflow callbacks.
- Do not introduce nested component, port, connection, or condition callback trees.
- One component-specific Action<TOptions> configuration callback is acceptable.
- The main graph should read top-to-bottom like the JSON hierarchy while remaining ordinary typed C#.

## Current Baseline to Preserve

The current repository already contains important foundations. Reuse and simplify them rather than replacing them:

- ApplicationDefinitionBuilder owns resources, resource groups, workflows, application-level cross-workflow connections, validation, and immutable Build behavior.
- AddWorkflow(string) returns WorkflowDefinitionBuilder.
- AddWorkflow(string, out WorkflowDefinitionBuilder) returns ApplicationDefinitionBuilder for fluent capture.
- WorkflowDefinitionBuilder already supports component creation and local Connect calls.
- ApplicationDefinitionBuilder already supports explicit cross-workflow Connect calls.
- AuthoringScope already enforces common ownership and builder mutability.
- ComponentHandle, ComponentHandle<TComponent>, AuthoredComponentHandle, InputPortHandle<T>, OutputPortHandle<T>, and SignalInputPortHandle already exist.
- Reusable authored handle shapes such as InputOutputComponentHandle<TInput,TOutput> already exist.
- Official component Composition packages already provide typed AddX authoring extension methods and component-specific builders.
- ApplicationLinkCompiler validates addresses, directions, types, cardinality, and portable conditions.
- CompiledApplicationLink already centralizes condition matching and exception isolation.
- ApplicationOutputPort<T> already provides condition context and rejection observability.
- ApplicationDefinitionJsonConverter and ApplicationDefinitionJson own the JSON boundary.
- IApplicationDefinitionSource and the revision planner own reload and revision behavior.
- FluxFlow.Fluent is a separate node-instance graph API. It is not the same DSL and must remain separate.

The implementation must be incremental over these foundations.

## Authoritative Workflow API

Keep exactly these two workflow-authoring shapes:

### Handle-returning shape

~~~csharp
var application = new ApplicationDefinitionBuilder();
var main = application.AddWorkflow("main");
var audit = application.AddWorkflow("audit");
~~~

### Fluent capture shape

~~~csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var main)
    .AddWorkflow("audit", out var audit);
~~~

Both shapes must produce equivalent definitions.

Do not add any of these:

- AddWorkflow(name, workflow => ...)
- nested workflow configuration callbacks;
- BeginWorkflow/EndWorkflow state;
- ambient current workflow;
- implicit workflow selection;
- a third DSL entry point such as App.Main that duplicates the builder.

The callback shape is intentionally rejected because it creates nesting and does not improve type safety.

## Typed Component Authoring Contracts

### Required concept

Introduce one small explicit typed authoring-contract abstraction that contains only the information required to add a known component:

- the canonical component type identifier;
- the explicit function that creates the public typed authored handle from the underlying ComponentHandle;
- when applicable, the explicit function that creates/applies the component-specific configuration builder.

Use a repository-appropriate final name. The preferred naming is:

- ComponentAuthoringContract<THandle> for a component without configuration;
- ComponentAuthoringContract<TOptions,THandle> for a component with component-specific configuration.

THandle must be an AuthoredComponentHandle or another existing repository-approved typed handle shape. TOptions is the component-specific builder already owned by the component package.

The contract is data plus explicit delegates. It must not:

- discover ports through reflection;
- resolve services;
- instantiate runtime nodes;
- duplicate runtime component descriptors;
- become a second component catalog;
- contain mutable global state;
- validate runtime registration;
- infer component types from CLR generic types.

The canonical component type and port names remain declared by the owning component package. The contract refers to those constants explicitly.

### Required AddComponent overloads

Add strongly typed overloads to WorkflowDefinitionBuilder with this effective shape:

~~~csharp
public WorkflowDefinitionBuilder AddComponent<THandle>(
    string name,
    ComponentAuthoringContract<THandle> component,
    out THandle handle);

public WorkflowDefinitionBuilder AddComponent<TOptions, THandle>(
    string name,
    ComponentAuthoringContract<TOptions, THandle> component,
    Action<TOptions> configure,
    out THandle handle);
~~~

It is acceptable to provide corresponding handle-returning overloads when they reduce implementation duplication and match existing conventions:

~~~csharp
THandle AddComponent<THandle>(
    string name,
    ComponentAuthoringContract<THandle> component);

THandle AddComponent<TOptions, THandle>(
    string name,
    ComponentAuthoringContract<TOptions, THandle> component,
    Action<TOptions> configure);
~~~

The out overloads are required because they enable flat fluent component declarations while retaining typed references.

All overloads must delegate to the same existing component-add core. There must be one authority for:

- component-name validation;
- duplicate detection;
- type storage;
- property configuration;
- commit/rollback behavior;
- ownership;
- immutable snapshots.

### Low-level escape hatch

The existing string-based AddComponent(name, type, ...) surface may remain as the low-level path for:

- dynamic plugins;
- application types not known at compile time;
- adapters built from external configuration;
- advanced/custom authoring;
- raw compatibility fixtures.

It must be documented as the dynamic/low-level escape hatch, not the preferred normal C# authoring path.

Do not remove dynamic authoring capability merely to make the normal path typed.

### Application-owned custom components

An application must be able to define its own explicit contract without a framework extension or generated code.

The API must make this straightforward:

~~~csharp
internal static class SampleComponents
{
    public static ComponentAuthoringContract<SourceOptionsBuilder, SourceHandle> Source { get; }
        = ComponentAuthoringContract.Create(
            SampleComponentTypes.Source,
            static definition => new SourceHandle(
                definition,
                SampleComponentPorts.Output),
            static options => options.Apply);
}
~~~

The exact factory syntax may be simplified to fit the final implementation, but the following are mandatory:

- declaration is explicit;
- no reflection;
- no registration scan;
- no hidden convention based on property names;
- no repeated type or port strings at each application call site;
- a custom handle can expose meaningful named ports such as Input, Output, Events, Approved, Rejected, or Diagnostics.

### Official component families

Bring official Composition authoring onto the same core without creating two competing systems.

For every official authored component declaration:

- retain the familiar AddX extension when it remains useful;
- make AddX delegate to the typed contract/AddComponent core, or make both delegate to one smaller internal primitive;
- expose an explicit typed contract when required for the generic AddComponent(name, contract, out handle) experience;
- reuse existing component-specific option builders;
- reuse existing typed handle shapes or add a small feature-specific handle only when port names/shape require it;
- do not repeat canonical type IDs, port names, option application, or validation logic across AddX and contract code.

Preserve the current repository coverage matrix:

- all 19 component families;
- all 44 official component declarations;
- the 18 root composition families and the nested MQTT composition family;
- their typed option builders, resource handles, component handles, events, and signal inputs.

Do not mechanically invent a new type per declaration if an existing shared handle is clear. Do not force unlike components into an overly generic handle.

## Typed Component Handles and Ports

Normal authoring must expose ports as named properties:

~~~csharp
source.Output
upper.Input
upper.Output
review.Events
router.Matched
router.Unmatched
~~~

Normal code must not require:

~~~csharp
component.Input<Order>("Input")
component.Output<Order>("Output")
component.Output<ComponentEvent>("Events")
~~~

The raw ComponentHandle.Input, Output, and SignalInput methods may remain for dynamic/custom escape-hatch scenarios.

### Port requirements

- InputPortHandle<TMessage> and OutputPortHandle<TMessage> remain the type-safety foundation.
- Events are ordinary explicitly named typed output ports.
- There is no implicit Events port.
- There is no special UseEvents or ForwardEvents concept.
- The public property name should reflect the component contract, commonly Events.
- Port addresses remain fully qualified and stable.
- Handles remain bound to one AuthoringScope.
- Handle creation must not mutate the component definition.
- Component option configuration and handle construction must not execute runtime component factories.

The recently completed HasInput, HasOutput, HasSignalInput, and HasEvents terminology applies to runtime/designer component declaration. Do not regress those APIs back to AddInput/AddOutput/AddEvents.

## Connection API

Keep all three useful scopes, with one internal connection operation.

### Direct output-port connection

~~~csharp
source.Output.ConnectTo(upper.Input);
source.Output.ConnectTo(trigger.Signal);
~~~

This is the preferred concise form when both handles are already available.

### Workflow-scoped connection

~~~csharp
main.Connect(source.Output, upper.Input);
main.Connect(source.Output, trigger.Signal);
~~~

This remains explicit and must accept only endpoints belonging to the same workflow.

### Application-scoped connection

~~~csharp
application.Connect(source.Output, audit.Input);
application.Connect(source.Output, auditTrigger.Signal);
~~~

This remains the explicit builder-level form for cross-workflow connections.

### Naming

Use ConnectTo for direct port fluent calls.

Do not use LinkTo. In the .NET dataflow ecosystem, LinkTo normally means creating a live runtime link. This API records an application definition and does not immediately connect runtime objects.

### Fan-out

ConnectTo must return the same OutputPortHandle<TMessage>, enabling readable fan-out:

~~~csharp
review.Output
    .ConnectTo(priority.Input)
    .ConnectTo(standard.Input);
~~~

The returned source handle must be reference-identical to the receiver. Do not create wrapper chains.

### Cross-workflow behavior

- Direct ConnectTo may connect across workflows when source and target belong to the same ApplicationDefinitionBuilder/AuthoringScope.
- ApplicationDefinitionBuilder.Connect may connect across workflows under the same owner.
- WorkflowDefinitionBuilder.Connect must reject a cross-workflow target.
- Any connection between different application owners must fail before mutation.
- Full addresses, not local string concatenation at the call site, determine source and target.

### One internal operation

ConnectTo, workflow.Connect, and application.Connect must delegate to one internal connection-add operation so they share:

- ownership validation;
- workflow-scope validation;
- source/target direction validation;
- exact message type validation;
- signal-input rules;
- target link-cardinality validation;
- duplicate connection rules;
- condition representation;
- declaration ordering;
- error messages;
- transactional mutation behavior.

Do not maintain parallel connection logic in extension methods.

## Link Conditions

Support three explicit condition forms.

### Unconditional

~~~csharp
review.Output.ConnectTo(sink.Input);
~~~

### Portable expression

~~~csharp
review.Output.ConnectTo(
    priority.Input,
    when: "input.Priority == true");
~~~

This remains compatible with JSON, configuration sources, storage, hot reload, and IFlowExpressionEngine.

### Typed synchronous C# predicate

~~~csharp
review.Output.ConnectTo(
    priority.Input,
    when: static order => order.Priority);
~~~

The same typed predicate must be supported by workflow.Connect and application.Connect.

### Required overload shape

For typed input ports, provide non-ambiguous overloads equivalent to:

~~~csharp
OutputPortHandle<TMessage> ConnectTo(
    InputPortHandle<TMessage> target);

OutputPortHandle<TMessage> ConnectTo(
    InputPortHandle<TMessage> target,
    string when);

OutputPortHandle<TMessage> ConnectTo(
    InputPortHandle<TMessage> target,
    Func<TMessage, bool> when);
~~~

Mirror the condition forms for signal-input targets using the existing signal semantics. Mirror them on WorkflowDefinitionBuilder.Connect and ApplicationDefinitionBuilder.Connect.

Avoid a single object condition parameter, optional delegate/string ambiguity, or overloads that make null resolve unpredictably.

Null string/delegate arguments must fail immediately with the correct parameter name. An absent condition uses the unconditional overload.

### Predicate type safety

- TMessage comes from OutputPortHandle<TMessage>.
- The target input must accept the exact compatible message type under existing FluxFlow link rules.
- Wrong message types must fail at compile time for ordinary typed calls.
- No boxing-based dynamic predicate dispatch.
- No reflection-based generic invocation.
- No expression-tree translation.

### Predicate execution semantics

- Predicates are synchronous.
- Predicates execute once per candidate route and message.
- A true result delivers to that route.
- A false result rejects only that route.
- Other fan-out routes continue independently.
- A predicate exception is caught at the existing condition boundary.
- A predicate exception must emit the existing LinkConditionFailed diagnostic/system event with link context and exception details.
- A predicate exception must not terminate the application runtime.
- A successful null payload for a nullable/reference TMessage is passed to the predicate.
- For an error FlowMessage<TMessage>, the payload predicate is not invoked. The conditional route is treated as not matched through normal condition-rejection observability; other routes continue.
- Unconditional error delivery behavior remains unchanged.
- Portable expression behavior for error messages remains unchanged.

Do not add an advanced FlowMessage predicate overload in this round. Fault-specific routing should use existing explicit error/system-event facilities or a component. This keeps the primary contract understandable.

### Predicate responsibilities

Document that a code predicate should be:

- fast;
- side-effect free;
- non-blocking;
- safe for concurrent invocation when the runtime can route concurrently.

Captured closures are allowed. The application author owns the thread safety and lifetime implications of captured objects. I/O, service calls, retries, and stateful processing belong in a component, not in a link predicate.

Do not add:

- async predicates;
- CancellationToken predicate overloads;
- IServiceProvider injection;
- predicate dependency injection;
- retries;
- timeouts;
- policy pipelines;
- state stores;
- caching;
- automatic delegate serialization.

## First-Class Condition Ownership

A typed C# predicate must be owned immutably by the built code-first definition/result and its runtime revision.

Required lifetime behavior:

- Build snapshots the condition along with the graph.
- Mutating the builder after Build remains impossible under existing rules.
- The built definition keeps captured closure state alive for exactly as long as the definition/revision needs it.
- Replacing or retiring the revision releases the old definition and predicates when no runtime work references them.
- There is no process-global registry.
- There is no static dictionary keyed by generated IDs.
- There is no service locator.
- There is no sidecar whose lifetime can drift from the built code-first definition/result.
- There is no serialization attempt over a delegate.

Use a small explicit internal/public condition representation, such as a discriminated abstraction with:

- no condition;
- portable expression condition;
- typed code predicate condition.

The exact type names are implementation decisions, but the representation must be immutable, definition-owned, and type-safe.

## Shared Link Validation and Compilation

The JSON and C# source models may remain separate. Do not redesign the JSON model merely to store C# delegates.

They should converge at the smallest internal link-validation/compilation boundary:

- the JSON path continues to read portable link declarations from the established JSON/ApplicationDefinition representation;
- the C# builder produces immutable typed in-memory link declarations directly;
- a small shared compiler input describes source, target, message type, cardinality context, and condition;
- both adapters use the same address, direction, type, cardinality, duplicate, and diagnostic rules;
- both produce the same CompiledApplicationLink/runtime matcher form;
- runtime planning consumes compiled links without knowing their authoring source.

For any one built graph, each link must have one authoritative declaration. Do not store the same C# link once as a raw JSON-style property and again in a delegate registry.

Choose the smallest implementation after inspecting constructors, equality/revision behavior, runtime planning, and ApplicationLinkCompiler. It may extend the existing hostable result with immutable code link state, or use a small code-first hostable result accepted by the same runtime compiler.

Do not create a large public model hierarchy solely to distinguish “JSON” from “C#.” A small explicit code-first result is acceptable when it keeps the JSON DTO clean and reduces special cases. Source-model separation is allowed; duplicated validation, compilation, diagnostics, and runtime routing are not.

Whichever direction is chosen:

- raw JSON-shaped ApplicationDefinition construction continues to work where it is a supported low-level surface;
- the existing JSON shape remains canonical;
- code-first Build remains a direct in-memory operation;
- the runtime accepts the built code-first result directly;
- no JSON conversion occurs during C# Build, registration, compilation, revision planning, or execution;
- no production InternalsVisibleTo is added;
- no hidden global condition storage is used.

## Link Compilation and Runtime Matching

ApplicationLinkCompiler and CompiledApplicationLink must converge all condition kinds into one runtime matcher boundary.

### Portable expressions

- Continue to compile through IFlowExpressionEngine.
- Continue to expose the same input, message, and payload variables.
- Continue to fail clearly when an expression is present and no expression engine is configured.
- Preserve existing expression text and diagnostics.

### Typed predicates

- Must not require IFlowExpressionEngine.
- Must not be converted to a string.
- Must not compile through reflection or expression trees.
- Must retain its exact TMessage contract.
- Must use the same TryMatch exception-isolation boundary as portable conditions.
- Must produce the same route-level match/no-match result understood by ApplicationOutputPort<T>.

The runtime port should not need to know whether the author used JSON, a string expression, or a C# predicate. It should invoke one compiled condition/matcher abstraction.

Avoid adding condition-type branching throughout the engine.

## Revision, Equality, and Reload Semantics

Inspect and update:

- ApplicationRevisionPlanner;
- application definition equality/comparison;
- runtime plan creation;
- revision replacement;
- rollback behavior;
- retained/restarted runtime behavior.

Required semantics:

- Adding or removing a link changes the revision plan.
- Changing a portable expression changes the route.
- Changing a code predicate changes the route.
- A new definition containing a new delegate must never be incorrectly classified as unchanged.
- Do not hash delegate targets, inspect closures, reflect over methods, or invent process-stable semantic equality for arbitrary code.
- Reusing the same already-built definition may reuse its stable immutable condition identity where current planner behavior allows.
- Rebuilding the application with a new predicate instance may conservatively rebuild that route/revision.
- A conservative rebuild is acceptable; an incorrect unchanged classification is not.
- Revision activation failure must retain current rollback guarantees.
- Retired revisions must release their predicate/closure references after in-flight work completes.

JSON/configuration hot reload remains portable-expression-only. Do not attempt to hot-reload executable delegates from JSON.

## Independent JSON and Designer Path

Preserve:

- the current JSON property names and structure;
- existing resource, resource-group, workflow, component, view, check, and link representations;
- canonical ordering and formatting expectations;
- ApplicationDefinitionJson parsing and serialization;
- IApplicationDefinitionSource;
- file/configuration loading;
- hot reload;
- designer persistence and load behavior;
- portable expression compilation;
- existing raw definition examples that intentionally demonstrate JSON equivalence.

The UI/designer path is strictly JSON-based:

- it creates, edits, validates, loads, and saves portable JSON definitions;
- it continues to retain all existing port metadata and explicit Events semantics;
- it does not consume ApplicationDefinitionBuilder;
- it does not open or edit C# builder results;
- it does not need typed C# component handles;
- it does not need delegate conditions;
- it does not generate C# source.

Do not add or modify designer behavior for the C# DSL. Do not add a designer-to-C# bridge, C#-to-JSON exporter, portability capability API, serializer adapter, or code-condition converter in this round.

Only make a JSON/designer implementation change if the shared compiler refactor would otherwise break existing JSON loading, validation, persistence, or hot reload. Such changes must be compatibility preservation, not new cross-path functionality.

## Validation and Transactional Behavior

All authoring entry points must reject invalid input before partially changing builder state.

Cover:

- null contract;
- null configure callback;
- null predicate;
- empty/invalid application names where applicable;
- empty/invalid workflow names;
- empty/invalid component instance names;
- duplicate workflow names;
- duplicate component names;
- contract with invalid canonical type;
- invalid or duplicate port declarations in a custom handle;
- source and target from different applications;
- workflow.Connect with endpoints from different workflows;
- wrong direction;
- incompatible message types;
- invalid signal connection;
- target link-cardinality overflow;
- duplicate identical links;
- conflicting conditions for a cardinality-limited target;
- connect-after-Build;
- component-add-after-Build;
- Build after a failed operation.

Failed operations must not leave:

- a partially added component;
- a reserved component name;
- a partial property update;
- a partial connection;
- a consumed target cardinality slot;
- a mutated previously built definition;
- a leaked predicate reference in global state.

Error messages must include useful component/link addresses and the invalid operation. Do not expose implementation-only registry IDs.

## Required Sample Migration

Migrate samples/FluxFlow.SampleApp/SampleWorkspaceDefinition.cs from raw constructor/string-link authoring to the typed code-first DSL.

Remove normal sample patterns such as:

~~~csharp
new ApplicationDefinition(
    workflows:
    [
        new("main", new ApplicationWorkflowDefinition(
        [
            new("source", Component(
                "sample.source",
                ("messages", new[] { "alpha", "beta" }),
                ("Output", "upper.Input"))),
            new("upper", Component(
                "sample.uppercase",
                ("Output", "sink.Input"))),
            new("sink", Component("sample.sink"))
        ]))
    ]);
~~~

Replace them with:

- ApplicationDefinitionBuilder;
- the two approved workflow shapes as appropriate;
- sample-local explicit typed component contracts;
- sample-local component-specific option builders;
- typed authored component handles;
- named typed port properties;
- ConnectTo or scoped Connect calls;
- at least one typed C# predicate;
- explicit Events port connections where the sample uses events.

Preserve all sample behavior:

- source data;
- review/uppercase/processing behavior;
- sinks;
- events;
- views;
- checks;
- resources;
- console output relied on by process tests;
- startup and shutdown behavior.

The sample must demonstrate:

- configuration through a component-specific builder;
- out-var fluent capture;
- local typed connection;
- fan-out;
- typed predicate;
- cross-workflow connection if the sample already has a meaningful second workflow;
- explicit event output.

Do not distort the sample merely to demonstrate every overload. Documentation tests may cover alternate forms.

## Fluent Package Boundary

FluxFlow.Fluent remains a distinct runtime node-instance DSL.

Do not:

- merge FluxFlow.Fluent into ApplicationDefinitionBuilder;
- rename the current fluent package;
- change node-instance execution semantics;
- make ApplicationDefinitionBuilder activate nodes directly;
- imply that code-first ApplicationDefinition authoring replaces the node-instance DSL.

Update documentation and the Fluent sample to explain the distinction:

- ApplicationDefinitionBuilder creates a hostable application definition using registered component types and supports resources, workflows, revisions, JSON-equivalent definitions, and code-only predicates.
- FluxFlow.Fluent builds a direct in-process graph from node instances.

The two APIs may share conceptual words such as Connect, but they must not share hidden mutable state.

## Files and Areas to Inspect

Inspect all current locations before deciding the final edit list. At minimum, trace:

### Composition authoring

- src/FluxFlow.Composition/Authoring/ApplicationDefinitionBuilder.cs
- src/FluxFlow.Composition/Authoring/WorkflowDefinitionBuilder.cs
- src/FluxFlow.Composition/Authoring/AuthoringHandles.cs
- src/FluxFlow.Composition/Authoring/ComponentDefinitionBuilder.cs
- authoring scope, resource builders, component builder helpers, and definition rules

### Definition model and JSON

- src/FluxFlow.Composition/ApplicationModel/ApplicationDefinition.cs
- src/FluxFlow.Composition/ApplicationModel/ApplicationDefinitionJson.cs
- src/FluxFlow.Composition/ApplicationModel/ApplicationDefinitionJsonConverter.cs
- workflow/component definition types
- JSON converter fixtures and canonical snapshots

### Link compilation

- src/FluxFlow.Composition/ApplicationLinks/ApplicationLinkCompiler.cs
- src/FluxFlow.Composition/ApplicationLinks/CompiledApplicationLink.cs
- link projection/declaration types
- port metadata/cardinality validation

### Runtime and revisions

- src/FluxFlow.Engine/Ports/ApplicationOutputPort.cs
- application port binder/runtime assembler
- ApplicationRuntimePlanFactory
- ApplicationRevisionPlanner
- revision activation/rollback code
- application definition source and reload handling
- ApplicationPortEventPublisher and ApplicationSystemEventNames

### Component authoring packages

- every src/FluxFlow.Components.*.Composition project
- MQTT nested composition authoring
- shared authored handle helpers
- the complete family/declaration matrix

### Samples and fixtures

- samples/FluxFlow.SampleApp/SampleWorkspaceDefinition.cs
- samples/FluxFlow.SampleApp component contracts/registration
- samples/FluxFlow.CompositionSample
- samples/FluxFlow.FluentSample
- package-consumer acceptance fixture
- process/sample acceptance tests

### Documentation and memory

- README.md
- src/FluxFlow.Composition/README.md
- src/FluxFlow.Fluent/README.md
- component Composition package READMEs
- documentation site sources
- docs/02 or current application-definition documentation
- docs/10 or current expression/mapping documentation
- docs/14 or current public API documentation
- docs/15 or current compatibility documentation
- docs/23 or current migration documentation
- memory/00-index.md
- memory/01-current-state.md
- memory/297-typed-component-port-binding.md
- memory/298-declarative-component-port-naming.md

Use actual current paths if documentation has moved. Do not create duplicate documentation trees.

## Implementation Sequence

### Phase 1: Freeze the contract with tests

After using the mandatory testing skill:

1. Add focused failing tests for the two workflow shapes and explicit absence of callback workflow APIs.
2. Add typed component-contract/add overload tests.
3. Add typed handle/port tests.
4. Add direct ConnectTo tests.
5. Add local/cross-workflow ownership tests.
6. Add portable and code-condition tests.
7. Add C#-path/JSON-path isolation and JSON regression tests.
8. Add runtime routing/exception tests.
9. Add public surface/family convention tests.

Tests must assert public behavior, not private implementation names.

### Phase 2: Add the minimal typed contract core

1. Add the small explicit contract abstraction.
2. Add the required WorkflowDefinitionBuilder overloads.
3. Reuse AddComponentCore.
4. Preserve transactional behavior.
5. Support custom AuthoredComponentHandle construction.
6. Do not touch runtime activation.

### Phase 3: Add direct typed connections

1. Add ConnectTo overloads to OutputPortHandle<TMessage>.
2. Return the same output handle.
3. Delegate to the existing owner/workflow connection operation.
4. Permit same-owner cross-workflow direct connections.
5. Preserve workflow-local Connect restrictions.
6. Keep application-level cross-workflow Connect.
7. Eliminate duplicated connection validation.

### Phase 4: Introduce first-class condition ownership

1. Design the smallest immutable condition representation.
2. Store it with the built code-first definition/result and revision.
3. Add the smallest shared compiler input/adapters for JSON links and typed C# links.
4. Ensure one authoritative connection set per built graph.
5. Update immutable snapshots and mutation guards.
6. Preserve the independent JSON construction path without converting C# links to JSON.

Do not begin with a global delegate lookup workaround.

### Phase 5: Compile and execute both condition kinds

1. Adapt ApplicationLinkCompiler.
2. Compile portable expressions through IFlowExpressionEngine.
3. Wrap typed predicates directly without expression services.
4. Converge on one CompiledApplicationLink matcher.
5. Preserve exception isolation and diagnostics.
6. Implement the explicit error-message behavior.
7. Verify fan-out independence.

### Phase 6: Preserve revision correctness and JSON-path isolation

1. Update revision comparison/planning.
2. Verify replace/retire/rollback behavior.
3. Prove a code-only definition registers and executes without any JSON operation.
4. Preserve the existing canonical JSON definition path independently.
5. Verify configuration source/hot reload behavior remains portable.
6. Verify no new dependency connects the UI/designer to C# authoring contracts.
7. Do not implement C# builder serialization or export.

### Phase 7: Migrate official authoring

1. Move official AddX implementations onto the typed contract core.
2. Add contract access for generic AddComponent usage.
3. Remove duplicate type/port/configuration declarations created by the migration.
4. Verify the 19-family/44-declaration matrix.
5. Preserve public package boundaries.

### Phase 8: Migrate samples

1. Introduce explicit sample-local contracts/options/handles.
2. Rewrite SampleWorkspaceDefinition.
3. Preserve all views, checks, resources, and markers.
4. Update Composition examples.
5. Clarify the separate Fluent DSL.
6. Run samples as real processes.

### Phase 9: Documentation and memory

1. Update all affected README and documentation site pages.
2. Add migration guidance from raw string DSL to typed C# authoring.
3. Document code-only predicate portability.
4. Create memory/299-typed-code-first-application-authoring.md.
5. Update memory/00-index.md.
6. Update memory/01-current-state.md.
7. Record durable architectural decisions and exact verification evidence.

### Phase 10: Cleanup and full verification

1. Remove stale helpers and duplicated code made obsolete by the final design.
2. Scan for obsolete AddInput/AddOutput/AddEvents authoring terminology.
3. Scan samples/docs for avoidable component type and port-name strings.
4. Accept intentional public API baseline changes using the repository process.
5. Run focused and full gates.
6. Update this goal to complete only after every required result is green.

## Detailed Testing Requirements

Use the repository's existing xUnit and Shouldly conventions. Do not introduce a second test framework.

### ApplicationDefinitionBuilder tests

Verify:

- AddWorkflow(name) returns the workflow handle.
- AddWorkflow(name, out workflow) returns the application builder.
- both shapes build equivalent immutable definitions;
- no public AddWorkflow callback overload exists;
- chaining two out-var workflows works;
- duplicate/invalid workflow names remain correct;
- application Build freezes all child builders.

### Typed component contract tests

Verify:

- configuration-free contract;
- configured contract;
- handle-returning overload if exposed;
- out-var fluent overload;
- concrete inferred handle type;
- component-specific option builder;
- canonical type ID emitted once;
- canonical property shape;
- handle named ports use canonical addresses;
- custom application-owned contract;
- custom multi-port/event handle;
- null contract;
- null configure action;
- invalid type;
- duplicate component;
- option-builder failure leaves no partial component;
- handle-factory failure leaves no partial component;
- runtime factory is never executed during definition authoring;
- Build snapshot is immutable.

### Direct connection tests

Verify:

- Output.ConnectTo(Input);
- Output.ConnectTo(SignalInput);
- returned output handle is the same instance;
- fluent fan-out creates two routes in declaration order;
- same-workflow direct connection;
- cross-workflow direct connection under one owner;
- different-owner rejection;
- connect after Build rejection;
- duplicate route behavior;
- target cardinality;
- wrong direction/type is unavailable or rejected at the correct boundary;
- failed connect performs no mutation.

### Scoped Connect tests

Verify:

- workflow.Connect local success;
- workflow.Connect cross-workflow rejection;
- application.Connect local success;
- application.Connect cross-workflow success;
- all forms produce equivalent link declarations;
- all forms share validation and diagnostics;
- both typed input and signal input variants.

### Condition authoring tests

Verify:

- unconditional overload;
- exact string-expression preservation;
- typed predicate preservation;
- true and false predicates;
- static predicate;
- captured closure;
- successful null reference payload;
- null string rejection;
- null predicate rejection;
- no ambiguous public overload surface;
- condition identity retained in immutable Build;
- new predicate definition is treated as changed;
- no process-global delegate storage.

### ApplicationLinkCompiler tests

Verify:

- unconditional matcher;
- portable expression compilation;
- typed predicate compilation without IFlowExpressionEngine;
- portable expression still fails clearly without required engine;
- true/false match behavior;
- predicate exception returned through TryMatch rather than escaping;
- source/target/type/declaration metadata preserved;
- normal and signal target support;
- exact message type enforcement;
- error FlowMessage does not invoke payload predicate;
- portable error-message semantics unchanged.

### Engine routing tests

Use real application runtime assembly for:

- true predicate delivers;
- false predicate rejects only that route;
- fan-out route independence;
- local typed link;
- cross-workflow typed link;
- signal-input typed condition;
- captured closure result;
- predicate exception emits LinkConditionFailed;
- diagnostic contains source and target;
- runtime remains active after exception;
- later messages continue to process;
- unconditional error propagation unchanged;
- conditional error message does not invoke payload predicate;
- revision replacement changes routing;
- failed revision activation rolls back safely;
- retired revision/closure can be collected when no longer referenced, using a deterministic lifetime test without arbitrary sleeps.

### C# and JSON path-isolation tests

Verify:

- a code-only builder definition builds without invoking JSON;
- a code-only builder definition registers, compiles, and executes without invoking JSON;
- revision replacement for a code-only definition does not invoke JSON;
- ApplicationDefinitionBuilder exposes no ToJson, Serialize, Export, or designer-oriented API;
- the UI/designer receives no new dependency on C# builder contracts;
- existing portable JSON parsing and serialization remain unchanged;
- existing portable JSON round trip remains equivalent;
- existing JSON fixtures are unchanged;
- parsing JSON never creates a code predicate;
- JSON hot reload remains functional.

Treat JSON assertions only as regression coverage for the independent JSON feature. Do not serialize a C# builder result in these tests. Do not route C# execution through JSON merely to reuse JSON fixtures.

### Revision planner tests

Verify:

- unchanged portable definition behavior;
- added/removed link;
- changed expression;
- changed code predicate;
- same built definition behavior;
- no delegate reflection/hash dependency;
- rollback and old revision retention;
- old predicate ownership ends after retirement.

### Component-family convention tests

Verify the complete official matrix:

- every official AddX remains correctly typed where retained;
- every AddX and generic contract path produce equivalent component definitions;
- component type ID has one authority;
- port names have one authority;
- option builder application has one authority;
- all 19 families are covered;
- all 44 declarations are covered;
- MQTT nested family is covered;
- explicit Events ports remain explicit;
- HasInput/HasOutput/HasSignalInput/HasEvents naming remains enforced;
- no old AddInput/AddOutput/AddEvents invocation leaks into production/sample documentation.

### Sample and documentation tests

Verify:

- SampleWorkspaceDefinition uses the typed application builder;
- no raw helper recreates string links under a new name;
- sample has no avoidable component type/port-name string at call sites;
- sample process output/markers remain correct;
- Composition sample compiles and runs;
- Fluent sample compiles and its boundary documentation is correct;
- documentation snippets compile where the repository supports snippet tests;
- package README/documentation matrix remains complete.

### Test quality rules

- No skipped tests.
- No arbitrary Thread.Sleep or Task.Delay for synchronization.
- No order-dependent global state.
- No tests that pass only because they inspect an implementation-private class name.
- No mock-heavy substitute for runtime acceptance.
- Use deterministic synchronization for concurrency/lifetime tests.
- Assert both positive behavior and failure diagnostics.
- Preserve zero-warning builds.

## Public API and Compatibility

Breaking changes are allowed, but the final surface must be deliberate and smaller.

During implementation:

- inventory all new public types and overloads;
- avoid exposing internal condition plumbing;
- avoid duplicate convenience aliases;
- do not keep obsolete methods solely for backward compatibility;
- do not remove the low-level dynamic path without proving replacement coverage;
- update PublicApi.Shipped/Unshipped or the repository's current API baseline through its accepted process;
- run the normal public API verification after accepting intended changes;
- do not suppress baseline failures.

Do not add production InternalsVisibleTo for tests.

Do not change package versions, release metadata, or publish packages unless a separate explicit instruction authorizes it.

## Documentation Requirements

Update all relevant documentation in the same round.

Documentation must explain:

1. JSON and C# are independent first-class authoring sources.
2. Both sources converge at normalization, validation, and compilation—not serialization.
3. C# Build, registration, revision planning, and execution never require JSON.
4. The two supported workflow-authoring shapes.
5. Why callback-style workflow nesting is intentionally absent.
6. Typed component contracts.
7. How official and custom components expose typed handles.
8. Component-specific option builders.
9. Direct ConnectTo.
10. Workflow-local Connect.
11. Application-level cross-workflow Connect.
12. Fan-out chaining.
13. Typed Events outputs.
14. Portable expression conditions.
15. Typed C# predicates.
16. Predicate concurrency/side-effect guidance.
17. Error-message predicate semantics.
18. The separation between compiled C# authoring and portable JSON authoring.
19. JSON/configuration/hot-reload behavior as an independent path.
20. The UI/designer creates JSON and does not consume the C# DSL.
21. Code-first replacement through a newly built in-memory definition.
22. The low-level string escape hatch.
23. The distinction from FluxFlow.Fluent.
24. Migration from raw ApplicationDefinition constructors and string link addresses.

Include concise before/after examples and one complete realistic example.

Update:

- root README;
- FluxFlow.Composition README;
- affected component Composition package READMEs;
- FluxFlow.Fluent README/sample explanation;
- documentation site content;
- public API and compatibility pages;
- migration guide;
- conditional-link/expression documentation.

Do not duplicate the same long explanation across every component package. Put shared concepts in Composition documentation and link to them.

## Memory Requirements

Create memory/299-typed-code-first-application-authoring.md containing:

- problem statement;
- agreed public authoring shapes;
- typed contract architecture;
- handle/port design;
- connection scope semantics;
- the shared normalization/validation/compilation boundary;
- confirmation that C# execution has no JSON dependency;
- portable expression and typed C# predicate choices;
- predicate execution/error semantics;
- explicit exclusion of C# serialization/export and designer integration;
- revision/lifetime decision;
- component family migration result;
- sample migration result;
- final changed-file summary;
- exact test/build/sample/package evidence;
- any explicitly deferred follow-up.

Update:

- memory/00-index.md;
- memory/01-current-state.md.

The memory must describe the implemented reality, not the initial plan.

## Non-Goals

Do not include any of the following:

- workflow callback DSL;
- an App.Main third authoring syntax;
- async link predicates;
- DI-aware predicates;
- predicate retries/timeouts/policies;
- predicate expression-tree translation;
- delegate serialization;
- remote code loading;
- script compilation;
- reflection;
- source generation;
- assembly scanning;
- global condition registry;
- service locator;
- ambient builder context;
- JSON as the internal transport between the C# builder and runtime;
- mandatory C#-to-JSON projection;
- optional C#-to-JSON export or serialization;
- portability metadata for C# builder results;
- serializer/converter support for C# predicates;
- a JSON round trip during code-first startup, validation, revision planning, or execution;
- JSON schema redesign;
- designer C# editor;
- designer consumption of C# builder results;
- C# source generation from the designer;
- automatic conversion of arbitrary delegates to portable expressions;
- changes to FluxFlow.Fluent execution semantics;
- new orchestration/distributed execution features;
- changes to durability/storage providers;
- package version bumps;
- publishing or releasing.

## Dependency and Complexity Guardrails

- Prefer zero new package dependencies.
- Prefer zero new project dependencies.
- Keep dependency direction consistent with existing Composition and Engine boundaries.
- Do not make Composition depend on a concrete component family.
- Do not make component contracts activate runtime nodes.
- Do not make Engine depend on component-specific option builders.
- Use small immutable records/classes and explicit delegates.
- Keep the public abstraction count minimal.
- Avoid one interface plus multiple factories when one sealed value object suffices.
- Avoid a generic abstraction when an existing AuthoredComponentHandle already expresses the behavior.
- Remove duplicated helpers after migration.
- Keep hot-path allocations no worse than the current expression-condition path; a typed predicate should normally be cheaper.
- Do not add per-message dictionaries solely for typed predicates when the current context can be adapted once.
- Do not optimize with unsafe caching before measurements.

## Verification Sequence

Use the repository's current exact commands and project names. Record command, count, warnings, duration where available, and result.

Run sequentially where repository-wide builds share outputs.

### Focused verification

1. FluxFlow.Composition.Tests authoring, definition, JSON, and link compiler tests.
2. FluxFlow.Engine.Tests application assembler, output routing, system event, and revision tests.
3. Each affected component Composition test project.
4. Component family convention/matrix tests.
5. Sample/documentation source-shape tests.
6. Public API tests/baseline verification.

### Sample verification

Run the affected samples as real processes:

- FluxFlow.SampleApp;
- FluxFlow.CompositionSample;
- FluxFlow.FluentSample if touched.

Assert their expected markers and clean termination.

### Full repository verification

Run the current equivalents of:

- full Release solution build;
- full solution tests;
- full FluxFlow.Release.Tests;
- documentation verification;
- dotnet format --verify-no-changes;
- git diff --check;
- package vulnerability audit;
- repository hygiene/source convention scans.

Current baselines at goal creation are approximately:

- Release build: 134 projects;
- full solution tests: 2,563 tests;
- public component matrix: 19 families and 44 declarations.

Treat these as orientation, not permission to ignore legitimate count changes from new tests.

### Package consumer acceptance

Because this changes public authoring packages, run the existing package-only consumer acceptance in real pack mode unless repository inspection proves the public package closure is unaffected.

Verify:

- all expected public packages are packed from the current source;
- consumer restore resolves only the candidate package closure;
- no ProjectReference is present;
- the typed code-only ApplicationDefinition example compiles and runs directly without JSON;
- existing JSON definition acceptance remains green through its existing independent gate;
- existing restart/durability seed and recovery markers remain green;
- isolated work directories are cleaned under the existing acceptance rules.

Do not weaken the established package-consumer gate.

### Hygiene scans

Scan touched production, samples, tests, and docs for:

- obsolete AddInput/AddOutput/AddEvents invocations;
- avoidable raw component type strings in normal C# authoring;
- avoidable raw port-name/address strings in normal C# authoring;
- LinkTo naming;
- global delegate registries;
- reflection APIs;
- dynamic invocation;
- TODO/FIXME placeholders;
- skipped tests;
- arbitrary sleeps;
- production InternalsVisibleTo additions;
- duplicate condition/link authorities;
- C# builder serialization/export methods;
- new designer dependencies on C# authoring contracts.

Classify intentional low-level/dynamic examples rather than deleting them blindly.

## Acceptance Criteria

This goal is complete only when all statements are true:

- ApplicationDefinitionBuilder supports the two approved workflow shapes.
- No callback-style AddWorkflow API was added.
- WorkflowDefinitionBuilder supports typed component contracts with out-var capture.
- Component-specific configuration remains component-specific.
- Custom application components can define explicit typed contracts without reflection.
- Normal authoring no longer repeats component type IDs or port names.
- Typed handles expose named Input, Output, Events, and other ports.
- OutputPortHandle<T>.ConnectTo exists and returns the same output handle.
- Direct ConnectTo supports same-owner cross-workflow links.
- Workflow Connect remains workflow-local.
- Application Connect remains the explicit cross-workflow builder form.
- Every connection path delegates to one validation/mutation core.
- Unconditional, portable expression, and typed synchronous predicate conditions work.
- Typed predicate execution does not require IFlowExpressionEngine.
- False and exception behavior are route-local and fan-out safe.
- Predicate exceptions emit LinkConditionFailed and do not stop the runtime.
- Error FlowMessage predicate behavior is explicit and tested.
- Predicates are owned by the built definition/revision with no global registry.
- Link compilation uses one runtime matcher boundary.
- Revision planning cannot misclassify a changed code predicate as unchanged.
- JSON and C# are independent authoring sources that converge at normalization, validation, and compilation.
- A code-first definition builds, registers, revises, and executes without JSON serialization or parsing.
- JSON is not used as an internal interchange format for code-first execution.
- The C# builder has no JSON serialization/export surface added by this goal.
- The UI/designer remains a JSON-only authoring path and has no dependency on the C# DSL.
- Portable JSON shape and hot reload remain compatible.
- Official component authoring converges on the same core.
- The 19-family/44-declaration matrix remains green.
- SampleWorkspaceDefinition uses the typed DSL and preserves behavior.
- FluxFlow.Fluent remains separate and documented accurately.
- Public API baselines are intentionally updated and verified.
- Documentation site and READMEs are current.
- memory/299, memory index, and current-state memory are current.
- Focused tests are green with zero warnings.
- Full solution tests are green with zero warnings.
- Release tests are green.
- Sample processes are green.
- Package-only consumer acceptance is green.
- Format, vulnerability, diff, and hygiene gates are green.
- No unrelated working-tree changes were lost.

## Final Report Requirements

When execution is complete, report:

1. The final public authoring API.
2. One complete typed code-first example.
3. Confirmation that code-first execution has no JSON dependency.
4. The shared normalization/validation/compilation boundary.
5. The separation between the developer C# path and UI/designer JSON path.
6. The exact error-message predicate semantics.
7. The condition model.
8. The revision/lifetime solution.
9. Component family and sample migrations.
10. Any intentional breaking changes.
11. Exact focused and full verification results.
12. Exact package-consumer acceptance result.
13. Documentation and memory files updated.
14. Any remaining limitations or intentionally deferred work.
15. Confirmation that no commit, push, publication, or release was performed.

## Execution Evidence

| Requirement | Evidence |
|---|---|
| Repository and dirty-tree inspection | Completed before editing; the existing durability, package-consumer, typed binding, and `Has*` work was preserved. |
| Architecture/dependency trace | `ApplicationDefinitionBuilder` → immutable definition links → `ApplicationLinkCompiler` → runtime matcher → revision planner/activation traced with Graphify and source inspection. |
| Testing skill workflow | `code-testing-agent` workflow completed; research, plan, audit, and Requirement/Evidence records are in `.testagent`. |
| Two workflow shapes | `AddWorkflow(name)` and fluent `AddWorkflow(name, out workflow)` are implemented; reflection tests reject callback/third entry forms. |
| Typed component contracts | `ComponentAuthoringContract<THandle>` and `ComponentAuthoringContract<TOptions,THandle>` use explicit delegates and no reflection. |
| Typed out-var AddComponent | Required typed `AddComponent` overloads return the workflow and capture the inferred handle through `out`. |
| Custom component contract | Sample-local and package-consumer contracts prove application-owned custom handles/options. |
| Named typed ports and Events | Shared and custom handles expose typed named ports; all 44 official contracts expose explicit typed `Events`. |
| Direct ConnectTo | Input and signal-input overload triplets implemented; direct calls return the same output handle. |
| Workflow-local Connect | Implemented through the shared connection core and rejects cross-workflow use. |
| Application/cross-workflow Connect | Implemented through the shared connection core for local and cross-workflow links. |
| Fan-out | Declaration-ordered fluent fan-out is covered in authoring and assembled runtime tests. |
| Portable expressions | Exact expression text remains supported through `IFlowExpressionEngine`. |
| Typed C# predicates | Synchronous `Func<T,bool>` links compile directly and need no expression engine. |
| Predicate exception isolation | A thrown predicate reports `LinkConditionFailed`, skips only that route, and permits siblings/later messages. |
| Error-message semantics | Conditional typed predicates are not invoked for error messages; unconditional error propagation is unchanged. |
| Definition/revision predicate ownership | Immutable `ApplicationLinkDefinition` owns executable predicates and opaque revision identity; no global registry exists. |
| Revision comparison and rollback | Planner covers added/removed/expression/predicate changes; rejected activation preserves the old generation. |
| Direct code-first execution without JSON | Engine and isolated package-consumer acceptance execute typed definitions directly in memory. |
| Shared normalization/validation/compilation boundary | JSON declarations and first-class C# links converge in `ApplicationLinkCompiler` and the same `CompiledApplicationLink` matcher. |
| Independent JSON-path compatibility | Existing JSON parsing, round-trip, source loading, hot reload, and runtime acceptance remain green. |
| UI/designer remains JSON-only | Source/convention scans and tests show no Designer dependency on C# authoring contracts or export API. |
| No C# serialization/export surface | Public-surface tests verify no `ToJson`, `Serialize`, `Export`, or designer bridge on the builder. |
| Official 19-family/44-declaration migration | Release matrix test verifies all 19 families and 44 typed contracts, including MQTT and Events. |
| Sample migration | SampleApp, CompositionSample, and MqttCompositionSample use the typed code-first path; obsolete SampleApp expression infrastructure was removed. |
| Fluent boundary documentation | Composition and Fluent documentation describe their separate hostable-definition and live-node graph purposes. |
| Public API baseline | Regenerated through `FLUXFLOW_ACCEPT_PUBLIC_API_BASELINE=1`, then independently reverified green. |
| Focused Composition tests | 150 passed, 0 failed/skipped, 0 warnings. |
| Focused Engine tests | 111 passed, 0 failed/skipped, 0 warnings, including deterministic retired-closure collection. |
| Component Composition tests | Included in the green 2,584-test full-solution run; stale MQTT JSON-link assertion migrated to first-class links. |
| Sample process verification | SampleApp, CompositionSample, MqttCompositionSample, and FluentSample all exited 0 with expected output. |
| Full Release build | 134 projects, 0 errors, 0 warnings. |
| Full solution tests | 2,584 passed across 66 projects, 0 failed/skipped, 0 warnings. |
| Full Release tests | 174 passed, 0 warnings. |
| Package-only consumer acceptance | Real pack mode exited 0 in 111.9 seconds: nine candidate packages, isolated `net8.0` build, candidate-only resolution, all JSON/C#/Fluent/durability/restart markers once, cleanup complete. |
| Documentation verification | 20 focused documentation/sample-documentation tests passed; the full Release suite is green. |
| Format and diff hygiene | Full `dotnet format --verify-no-changes` and full `git diff --check` exited 0; source scans found no new prohibited patterns. |
| Vulnerability audit | `dotnet list FluxFlow.sln package --vulnerable --include-transitive --no-restore` reported no vulnerable packages for every project. |
| Memory 299 and indexes | `memory/299-typed-code-first-application-authoring.md`, `memory/00-index.md`, and `memory/01-current-state.md` reflect the implemented result and exact evidence. |

No commit, push, pull request, package publication, or release was performed.

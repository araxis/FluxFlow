# Typed Code-First Application Authoring

> Historical note: the authoring-only `ComponentAuthoringContract` described
> here was replaced by the complete `ComponentContract` in
> `memory/300-unified-code-first-component-contracts.md`. The workflow, link,
> predicate, JSON-separation, and verification history below remains relevant.

## Problem

The existing C# application builder preserved the JSON document shape, but normal application code still repeated component type identifiers, port names, and string addresses. Connections were written back into component JSON properties, so the builder behaved too much like an alternate JSON syntax. It could not safely own normal C# predicates or give application-owned components a small reusable typed contract.

The agreed boundary is two independent authoring sources:

- portable JSON for files, configuration providers, hot reload, and UI/designer persistence;
- compiled C# for developers using typed handles, ordinary delegates, closures, and direct in-memory hosting.

They converge at normalization, catalog validation, link compilation, revision activation, and runtime routing—not at serialization.

## Public authoring shape

`ApplicationDefinitionBuilder` retains exactly two workflow forms:

```csharp
var main = application.AddWorkflow("main");

var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var main)
    .AddWorkflow("audit", out var audit);
```

No callback-style workflow scope, ambient current workflow, or third DSL entry point was added.

`WorkflowDefinitionBuilder` now accepts:

- `ComponentAuthoringContract<THandle>` for components without an options builder;
- `ComponentAuthoringContract<TOptions,THandle>` for components with component-specific configuration;
- handle-returning and flat `out var` overloads for both forms.

The existing string `AddComponent` surface remains the explicit dynamic/plugin escape hatch.

## Contract and handle architecture

A contract is a sealed value object created through `ComponentAuthoringContract.Create`. It stores only:

- one canonical component type identifier;
- an explicit typed-handle factory;
- for configured components, an explicit options factory and apply delegate.

There is no reflection, scanning, generated code, runtime node activation, service resolution, global registry, or inferred type identity. Name/type validation, duplicate checks, options commit, handle creation, rollback, and immutable snapshots share one component-add core. Failed options or handle factories leave no partial component.

`AuthoredComponentHandle` is externally derivable and exposes its underlying read-only `ComponentHandle`. Shared input/output handle shapes now require the explicit Events port name and expose a non-null `OutputPortHandle<ComponentEvent> Events`. FlowRetry and MQTT feature-specific handles likewise expose Events alongside their data and signal ports.

## Connections

All connection forms delegate to one mutation/validation core:

- `output.ConnectTo(input)` is concise, returns the same output for fan-out, and allows local or same-owner cross-workflow endpoints;
- `workflow.Connect(output, input)` is workflow-local;
- `application.Connect(output, input)` is the explicit application-level form and supports cross-workflow endpoints.

Typed input and signal-input overloads exist for unconditional links, required nonblank portable expression strings, and required synchronous `Func<T,bool>` predicates. Ownership, mutability, duplicate, cardinality, and workflow-scope checks are applied before mutation.

## In-memory links and conditions

`ApplicationDefinition` now owns an immutable read-only list of `ApplicationLinkDefinition`. The public link exposes source, target, message type, optional portable expression text, conditional state, and declaration side. Executable predicate and revision identity remain internal.

Code-first `Build()` no longer writes links into component JSON properties. `ApplicationLinkCompiler` combines portable declarations parsed from JSON component properties with first-class in-memory links, validates both through the same catalog/type/cardinality/cycle pipeline, and produces the same `CompiledApplicationLink` matcher boundary.

Portable expressions still require `IFlowExpressionEngine`. Typed predicates compile directly to a synchronous matcher and require no expression engine. A predicate:

- receives the successful payload value;
- is not invoked for an error `FlowMessage<T>`;
- returns false to skip only its route;
- reports a thrown exception as a route condition failure without stopping sibling fan-out routes or later messages.

Unconditional error propagation and portable-expression semantics are unchanged.

## Revision ownership and lifetime

Each newly authored predicate link receives an opaque definition-owned identity. Reusing the same built definition compares stable; rebuilding a predicate link is intentionally a behavior change, even when a compiler-cached static delegate instance might otherwise be reference-equal. No delegate reflection, hash inference, or process-global storage is used.

The revision planner compares first-class links for every source and target workflow. Added, removed, changed-expression, and changed-predicate links update the affected workflow revision units. Runtime compiled links retain predicates only for the active/prepared revision; normal retirement releases the old graph and its closures. A successful replacement's returned result reports `PreviousRevision`, but the application's retained `LastUpdate` copy removes that retired snapshot so it cannot keep executable predicates alive. Failed revision activation retains the prior working revision.

## Independent JSON boundary

`ApplicationDefinitionJsonConverter` continues to read and write the canonical portable `Resources` and `Workflows` document only. JSON parsing, configuration sources, persistence, hot reload, and Designer behavior remain independent and receive regression coverage.

The C# builder has no `ToJson`, `Serialize`, `Export`, or designer bridge. Code-first build, registration, planning, and execution do not invoke JSON. The UI/designer authors JSON and does not consume compiled C# builder definitions. C#-to-JSON export and portability metadata are explicitly outside this round.

## Official families and samples

All 19 active Composition families expose all 44 component contracts through public `<Family>Components` classes. Property names match retained `AddX` methods without the `Add` prefix. Every retained `AddX` delegates to the same typed contract and component-add core; option application, type IDs, and port names each have one family-owned authority.

`FluxFlow.SampleApp` now uses flat workflow capture, application-owned typed contracts/handles, direct `ConnectTo`, typed priority predicates, and Events fan-in. Its obsolete sample expression engine and Mapping project reference were removed because typed predicates need neither. `FluxFlow.CompositionSample` now demonstrates the same minimal typed custom-component pattern. `FluxFlow.MqttCompositionSample` uses typed source and MQTT contracts and compares JSON/C# by runtime behavior rather than serializing the C# definition.

`FluxFlow.Fluent` remains a separate live node-instance graph API and is documented as such.

## Documentation

The root README, Composition/Fluent READMEs, all 19 component Composition READMEs, public API overview, definitions/links guide, expression guide, migration guide, documentation index, and `docs/39-typed-code-first-authoring.md` describe the implemented boundary and usage.

## Verification evidence

Final verification completed on 2026-08-08:

- full Release solution build: 134 projects, 0 errors, 0 warnings;
- full solution tests: 2,584 passed across 66 projects, 0 failed/skipped, 0 warnings;
- full `FluxFlow.Release.Tests`: 174 passed, 0 warnings;
- focused `FluxFlow.Composition.Tests`: 150 passed, 0 warnings;
- focused `FluxFlow.Engine.Tests`: 111 passed, 0 warnings;
- focused Release authoring/family/package assertions: all 16 selected assertions green;
- documentation boundary and sample-documentation checks: 20 passed, 0 warnings;
- real package-only pack acceptance: exit 0 in 111.9 seconds, nine candidate packages verified, isolated `net8.0` consumer built with 0 errors/warnings, candidate-only closure enforced, JSON/code-first/Fluent/durability/restart markers emitted exactly once, and script-owned directories cleaned;
- `FluxFlow.SampleApp`: three correctly routed orders and six component events;
- `FluxFlow.CompositionSample`: `ALPHA`, `BETA`;
- `FluxFlow.MqttCompositionSample`: configuration and definition paths each published the two expected messages;
- `FluxFlow.FluentSample`: expected linear uppercase and even/odd branched output;
- full `dotnet format --verify-no-changes`: exit 0;
- full `git diff --check`: exit 0;
- package vulnerability audit: every solution project reported no vulnerable direct or transitive packages;
- public API baseline regenerated through the documented acceptance variable and independently reverified green;
- hygiene scans found no new delegate registry, reflection/dynamic invocation, TODO/FIXME, skipped test, C# builder export API, or Designer dependency on the code-first authoring layer.

The first full-solution run exposed two stale integration expectations: the final lifecycle fix changed the public API source hash, and an MQTT test still expected links inside component JSON properties. The baseline was accepted through the repository workflow, the MQTT assertion was migrated to first-class `ApplicationDefinition.Links`, both were individually verified, and the complete 2,584-test rerun passed.

## Intentionally deferred

- async or DI-aware predicates;
- predicate retries, timeouts, or policies;
- delegate/expression-tree translation or serialization;
- C# builder export or Designer integration;
- JSON schema redesign;
- changes to `FluxFlow.Fluent` execution semantics;
- durability/storage-provider changes;
- package version changes or publication.

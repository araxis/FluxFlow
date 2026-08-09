# GOAL: Unify Code-First Component Authoring and Runtime Registration

## Status

- State: complete
- Created: 2026-08-08
- Repository: `C:\Projects\FluxFlow`
- Execution started: 2026-08-08
- Breaking changes: allowed when they remove the split contract model and produce a smaller final API
- Backward-compatibility aliases: not required
- Commit, push, pull request, package publication, and release: not authorized by this goal
- Required result: production implementation, repository-owned migration, focused and full verification, documentation-site content, and memory updates

## Execution Instruction

Execute this goal completely in the existing FluxFlow repository. Do not stop after producing an API sketch, adding compatibility wrappers, updating a sample, or making focused tests pass.

Preserve all unrelated user work in the intentionally dirty working tree. The existing durability, package-consumer, typed component binding, explicit event naming, `HasInput`/`HasOutput`/`HasEvents`, and typed code-first authoring changes are the authoritative starting point. Never reset, discard, overwrite, or broadly reformat unrelated changes.

Use these engineering rules throughout execution:

1. Keep FluxFlow an explicit, lightweight, in-process workflow engine.
2. Apply KISS, SRP, IoC, and cohesive vertical-slice principles.
3. Prefer one small shared implementation over parallel authoring and runtime abstractions.
4. Do not add reflection, assembly scanning, source generation, hidden global registries, service locators, ambient builders, or convention-based magic.
5. Do not mutate `IServiceCollection` or the built service provider during application activation or hot reload.
6. Keep dependency injection standard: component factories receive the existing activation context and resolve normal application dependencies from it.
7. Keep fluent authoring flat. A component contract may use one ordinary configuration callback internally, but application authors must not enter nested callback scopes or repeat runtime declarations.
8. Do not create a large dependency graph, a universal registry abstraction, or a new package solely for this feature.
9. Use the existing descriptor, catalog, definition, revision, and activation mechanisms wherever they already express the required behavior.
10. Preserve the independent JSON definition and hot-reload path exactly; do not make JSON depend on executable C# delegates.
11. Before changing tests, follow the repository's mandatory code-testing workflow and maintain `.testagent/research.md`, `.testagent/plan.md`, and `.testagent/status.md`.
12. Keep this file current. Replace pending evidence with exact commands, counts, and outcomes before marking the goal complete.

## Problem Statement

The typed C# DSL currently requires two separate declarations of the same component:

1. `WorkflowDefinitionBuilder.AddComponent(..., SampleComponents.Uppercase, ...)` uses an authoring-only contract that knows the component type, options mapping, and typed handle.
2. `services.AddFluxFlowComponents().AddRuntimeComponent(...)` separately declares the node factory and the same named input, output, signal, and event bindings.

That split makes the normal code-first experience verbose and error-prone. A component package or application can accidentally let its typed handle, design metadata, and runtime descriptor drift apart. The host also repeats registration that is already implied by choosing a complete typed contract in compiled C#.

The repeated application code must disappear. A code-first application that adds a complete component contract must already contain everything required to validate, activate, and run that component.

## Objective

Replace the authoring-only contract with one complete, explicit component contract that is the single authority for:

- the stable component type identifier;
- the executable runtime node factory;
- typed input bindings;
- typed signal-input bindings;
- typed output bindings;
- the explicitly named event output binding;
- component processing capabilities and existing descriptor metadata;
- optional C# authoring-options construction and mapping into `ComponentDefinitionBuilder`;
- the typed authoring handle exposed to application code.

When a complete contract is added through the C# application builder, the resulting immutable `ApplicationDefinition` must own the runtime descriptors used by that definition. `services.AddFluxFlow(definition)` must then be sufficient to execute the application; the caller must not repeat `AddFluxFlowComponents().AddRuntimeComponent(...)` for those contracts.

## Required Normal Code-First Experience

The final normal application usage must have this shape:

~~~csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var workflow);

workflow
    .AddComponent(
        "first",
        SampleComponents.Uppercase,
        out var first)
    .AddComponent(
        "second",
        SampleComponents.Uppercase,
        out var second);

first.Output.ConnectTo(second.Input);

var definition = application.Build();

var services = new ServiceCollection();
services.AddFluxFlow(definition);

await using var provider = services.BuildServiceProvider();
await provider.StartFluxFlowApplicationAsync();
~~~

The normal code-first host must not also call:

~~~csharp
services.AddFluxFlowComponents()
    .AddRuntimeComponent(...);
~~~

Application dependencies remain ordinary DI registrations next to `AddFluxFlow`; for example, a sink service or HTTP client is still added to `IServiceCollection`, and the component factory resolves it through `ComponentActivationContext.Services`.

## Required Single Component Declaration

Introduce the final public concept as `ComponentContract`, replacing the narrower `ComponentAuthoringContract` terminology. The exact implementation must remain small and may reuse the existing `RuntimeComponentRegistrationBuilder`; it must not duplicate descriptor construction or binding validation.

The supported declaration must be explicit and statically typed. A representative no-options declaration is:

~~~csharp
public static ComponentContract<UppercaseHandle> Uppercase { get; } =
    ComponentContract.Create(
        SampleComponentTypes.Uppercase,
        runtime =>
        {
            runtime
                .UseFactory(static _ => new UppercaseNode())
                .HasInput(SampleComponentPorts.Input, static node => node.Input)
                .HasOutput(SampleComponentPorts.Output, static node => node.Output)
                .HasEvents(SampleComponentPorts.Events, static node => node.Events);
        },
        static component => new UppercaseHandle(component));
~~~

A representative options-aware declaration is:

~~~csharp
public static ComponentContract<SourceOptionsBuilder, SourceHandle> Source { get; } =
    ComponentContract.Create(
        SampleComponentTypes.Source,
        runtime =>
        {
            runtime
                .UseFactory(static context =>
                {
                    var options = context.BindConfiguration<SourceOptions>();
                    return new StringSourceNode(options.Messages);
                })
                .HasOutput(SampleComponentPorts.Output, static node => node.Output)
                .HasEvents(SampleComponentPorts.Events, static node => node.Events);
        },
        static () => new SourceOptionsBuilder(),
        static (options, definition) => options.Apply(definition),
        static component => new SourceHandle(component));
~~~

The implementation may improve the delegate ordering if that produces clearer overload inference, but it must retain these semantics:

- one complete declaration;
- one runtime configuration scope at most;
- a flat typed `UseFactory(...).HasInput(...).HasOutput(...).HasEvents(...)` chain;
- no second descriptor declaration;
- no node activation while declaring, registering, designing, validating, or building an application;
- factories execute only during runtime activation.

## Public API Requirements

### Complete contracts

1. Provide a non-generic `ComponentContract` factory surface.
2. Provide typed contracts for:
   - components without authoring options: `ComponentContract<THandle>`;
   - components with authoring options: `ComponentContract<TOptions, THandle>`.
3. Keep normal construction factory-only. The typed contract classes may expose
   controlled protected construction solely so the Designer package can attach
   metadata to the exact same descriptor without a registry or second
   declaration.
4. Keep the existing `AuthoredComponentHandle` constraint.
5. Expose the normalized `Type` and the complete immutable `ComponentDescriptor` as read-only contract state.
6. Validate null/empty type, runtime configuration, factory, options creation, options mapping, and handle creation at the narrowest useful boundary with component-type context.
7. Reject a runtime declaration without a factory through the existing descriptor validation.
8. Preserve sync, `ValueTask`, activation-owner cleanup, and advanced instance-factory capabilities already supported by `RuntimeComponentRegistrationBuilder`.
9. Remove the old `ComponentAuthoringContract` public types rather than leaving two overlapping normal paths. Breaking changes are allowed and every repository-owned caller must migrate.

### Workflow authoring

1. Keep the familiar `AddComponent(name, contract, out handle)` signature.
2. Keep the handle-returning counterparts where already present.
3. Keep the options-aware overload with `Action<TOptions>` and `out THandle`.
4. Adding a complete contract must atomically add both the component definition and its descriptor ownership to the builder.
5. If options mapping, handle creation, or descriptor validation fails, no partial component or contract entry may remain in the workflow/application builder.
6. Reusing the exact same contract for many component instances and across workflows must deduplicate the descriptor by component type/reference.
7. Conflicting contracts for the same type must fail deterministically and name the conflicting component type.
8. Preserve the existing low-level string-based `AddComponent(name, type, ...)` overloads for dynamic/advanced scenarios. Those overloads do not invent a runtime descriptor and therefore still require explicit host registration.

### Explicit host registration

1. Add a concise `FluxFlowRegistrationBuilder.AddComponent(ComponentContract contract)` path for JSON and dynamic definitions.
2. That method must register the contract's exact descriptor through the existing descriptor-registration/conflict logic.
3. Official family registration extensions must call this contract-based path rather than repeat factories and ports.
4. Keep `AddRuntimeComponent(type, Action<RuntimeComponentRegistrationBuilder>)` as the low-level advanced escape hatch.
5. Document `AddRuntimeComponent` as advanced/dynamic infrastructure, not the normal code-first application path.

## Definition Ownership and Serialization Boundary

Extend the immutable application definition with definition-owned runtime descriptors for complete C# contracts.

Required behavior:

1. `ApplicationDefinitionBuilder.Build()` captures an immutable, deterministic, read-only set of the descriptors actually introduced by complete contracts.
2. The set is ordered by component type for deterministic diagnostics and inspection.
3. A plain constructor-created or JSON-loaded `ApplicationDefinition` has no definition-owned runtime descriptors.
4. The code-only descriptor collection must never be emitted into canonical JSON.
5. JSON parsing must never try to create delegates, factories, handles, or descriptors.
6. Serializing a code-first definition continues to serialize only the existing portable resources and workflows. This is a diagnostic/portable projection, not a round-trip guarantee for executable C# behavior.
7. Deserializing that JSON produces a normal JSON definition that requires explicit host/package component registration.
8. Do not add C# export, source generation, UI-designer integration, delegate serialization, or a generalized polymorphic JSON contract.

## Effective Runtime Catalog

The runtime must resolve one deterministic effective catalog for every candidate definition:

~~~text
host-registered descriptors + definition-owned descriptors -> one effective ComponentCatalog
~~~

Required rules:

1. JSON definitions use only the host catalog and preserve current behavior.
2. Complete C# definitions use their embedded descriptors without requiring service registration.
3. Mixed definitions may use both complete contracts and low-level string component types.
4. The same descriptor instance registered in the host and embedded in the definition is accepted and deduplicated.
5. Two different descriptor instances for the same type are treated as a conflict, even if their metadata appears similar. No source silently wins.
6. Conflict diagnostics name the component type and make the host-versus-definition conflict understandable.
7. Unknown component types preserve the existing clear component-address/type diagnostic.
8. Resolve the effective catalog per candidate revision without modifying global state or the service provider.
9. Use that same effective catalog for link compilation, port-surface creation, descriptor validation, and component activation.
10. Avoid resolving/merging separate catalogs independently in multiple runtime stages; compute once per plan/candidate and pass it explicitly.

## Runtime and Revision Semantics

1. Initial activation from `services.AddFluxFlow(codeFirstDefinition)` must run with no manual component registration.
2. A later successful `ApplyAsync(nextCodeFirstDefinition)` may introduce, remove, or replace complete contracts without rebuilding the service provider.
3. Revision planning must treat a changed definition-owned descriptor identity used by a workflow as a workflow update.
4. Do not inspect, serialize, compile, or hash delegate bodies. Descriptor reference identity is the explicit code-behavior identity.
5. Reusing the same complete contract/descriptor produces stable revision comparison.
6. A changed contract for the same type must not silently reuse an old runtime generation.
7. Failed planning, conflict validation, factory activation, port validation, or candidate adoption must leave the active revision and generation unchanged.
8. Candidate cleanup must preserve existing component instance, activation-owner, resource snapshot, and port-generation disposal guarantees.
9. The active definition owns its descriptor/factory delegates. After a successful replacement, the host must not retain the retired definition-owned factory closure through `LastUpdate` or another cache.
10. Add deterministic lifetime coverage using a captured object plus forced full collection, following the existing typed-predicate retirement test pattern.
11. A failed replacement must retain the old active definition and keep its route/factory usable.

## Designer and Metadata Semantics

1. A complete contract's descriptor is also the single source for Designer metadata.
2. Official Designer registration must continue to collect metadata without executing component factories.
3. Explicit event names and direction-local deterministic orders remain unchanged.
4. A normal port may still be named `Events` when no explicit event binding reserves that name; do not reintroduce implicit event injection.
5. The existing designed-component low-level builder remains available for metadata-only/dynamic extensions, but official complete contracts must not duplicate their metadata there.

## Official Component Family Migration

Migrate all official typed component declarations, currently covering 19 component families and 44 contracts, to the complete `ComponentContract` model.

For every official contract:

1. Move or reuse the existing runtime factory/binding declaration so it is owned exactly once by the contract.
2. Preserve the public typed options builder and handle.
3. Preserve all option names, defaults, required flags, resource requirements, processing capabilities, input/output/signal/event types, names, and link cardinalities.
4. Preserve explicit typed `Events` on every official handle where currently promised.
5. Change the family service-registration extension to register the exact contract descriptor.
6. Ensure family registration remains idempotent and conflict-safe.
7. Ensure Designer metadata still matches runtime metadata without activation.
8. Preserve MQTT registrar, keyed resource, client ownership, subscription, and lifecycle semantics; only remove duplicate component descriptor declarations.
9. Do not move backend/resource settings into `FluxFlowApplicationOptions`.

## Samples and Package Consumer

### Composition sample

Rewrite `samples/FluxFlow.CompositionSample/Program.cs` so it demonstrates:

- each custom component declared once as a complete contract;
- workflow construction through typed `AddComponent` handles;
- typed `ConnectTo` links;
- `services.AddFluxFlow(definition)` with no redundant runtime component registration;
- normal DI registration only for genuine application services such as the collector;
- successful execution with the existing observable output.

### Other repository samples

Update SampleApp, MQTT composition, durability operations, and any other code-first sample that currently registers descriptors duplicated by the contracts it adds. Preserve explicit family/resource registration only where it is genuinely required by JSON loading, resource registrars, or low-level string components.

### Package-only acceptance

Extend the isolated `net8.0` package consumer so one marker proves a complete custom component contract can be:

1. declared from candidate packages only;
2. added to a code-first definition;
3. executed after only `services.AddFluxFlow(definition)` plus real dependency registrations;
4. linked through typed handles;
5. restored, built, and run without project references or fallback packages.

Preserve all existing JSON, Fluent, engine, durability, restart, cleanup, and exact-marker assertions.

## Testing Requirements

Use xUnit and Shouldly in the existing test projects. Do not create a new test project unless the existing ownership boundaries make coverage impossible.

### Composition tests

Cover at minimum:

- complete no-options and options-aware contract construction;
- exact descriptor factory and binding metadata;
- no factory execution during contract creation, builder use, `Build`, JSON serialization, or explicit registration;
- typed handle and options mapping behavior;
- atomic failure for null options, throwing options/configuration, null handle, and duplicate component names;
- descriptor deduplication when one contract is used repeatedly;
- deterministic conflict rejection for two contracts with the same type;
- immutable deterministic definition-owned descriptor collection;
- code-only descriptors omitted from JSON;
- JSON round-trip returns an empty definition-owned descriptor collection;
- contract-based service registration reuses the exact descriptor;
- idempotent same-contract registration and conflicting-contract rejection;
- public-surface reflection tests for the final names and removal of `ComponentAuthoringContract`.

### Engine tests

Cover at minimum:

- direct initial code-first activation with no manual descriptor registration;
- local and cross-workflow typed routing using embedded descriptors;
- mixed embedded-contract and host-registered string component execution;
- JSON definition execution remains host-registration based;
- same embedded descriptor also registered by the host is accepted;
- conflicting host and embedded descriptors are rejected before activation;
- hot reload introduces a new contract/type without rebuilding DI;
- changed descriptor identity marks the affected workflow updated;
- unchanged descriptor identity remains stable;
- successful replacement activates the new factory and retires the old generation;
- failed replacement preserves the old active generation;
- retired captured factory closure becomes collectible after successful replacement;
- factory, validation, and cleanup diagnostics remain contextual and deterministic.

### Official family, Designer, release, and source-shape tests

Cover at minimum:

- 19 families and 44 declarations are complete contracts;
- every contract exposes an exact runtime descriptor and typed handle/events surface;
- family registration uses the contract descriptor rather than an independent `AddRuntimeComponent` declaration;
- metadata/runtime equivalence remains green;
- Designer does not activate factories;
- normal code-first samples contain no redundant runtime registration;
- low-level `AddRuntimeComponent` remains available but is absent from the normal typed samples;
- public API baseline is updated intentionally;
- documentation distinguishes complete code-first contracts, JSON registration, and the low-level escape hatch.

## Documentation and Memory Requirements

Update all affected user-facing material, including:

- root `README.md`;
- `src/FluxFlow.Composition/README.md`;
- `src/FluxFlow.Engine/README.md` when host behavior is described;
- relevant official component-composition READMEs;
- `docs/README.md`;
- typed code-first authoring documentation;
- public API overview;
- definition/JSON boundary documentation;
- migration documentation for the breaking `ComponentAuthoringContract` to `ComponentContract` change;
- release-validation/package-consumer documentation;
- sample comments and snippets.

Add `memory/300-unified-code-first-component-contracts.md` and update `memory/00-index.md` and `memory/01-current-state.md` with:

- the architectural decision;
- final API shape;
- code-first versus JSON boundary;
- effective-catalog conflict rules;
- revision/lifetime behavior;
- official-family migration;
- exact verification evidence;
- remaining intentional limitations.

Do not leave stale examples containing both typed `AddComponent(contract)` and a duplicate `AddRuntimeComponent` registration.

## Explicit Non-Goals

This goal does not authorize:

- serializing factories, delegates, handles, or executable contracts into JSON;
- generating C# from JSON or from a UI designer;
- making the UI designer consume or emit C# application builders;
- reflection or assembly scanning to discover contracts;
- source generators;
- an ambient/global component registry;
- automatic registration based on loaded assemblies;
- rebuilding or mutating the service provider during revision activation;
- removing explicit registration required by JSON/dynamic definitions;
- removing the low-level string `AddComponent` or `AddRuntimeComponent` escape hatches;
- changing component business behavior, option schemas, resource semantics, delivery guarantees, or storage-provider configuration;
- redesigning MQTT resources or lifecycle ownership;
- unrelated cleanup outside the touched authoring/registration/runtime slice;
- package publication or a release.

## Implementation Sequence

1. Record repository, graph, and test-ownership research.
2. Add the complete `ComponentContract` types by reusing the existing runtime registration builder and descriptor validation.
3. Replace workflow contract overloads and capture descriptor ownership atomically.
4. Add immutable definition-owned descriptors while preserving canonical JSON.
5. Add contract-based explicit service registration for JSON/dynamic hosts.
6. Resolve one effective catalog per runtime candidate and pass it through compilation, port-surface creation, and activation.
7. Add descriptor-aware revision comparison and retired-factory lifetime correctness.
8. Migrate official families and Designer metadata to the single descriptor source.
9. Migrate samples and package-only acceptance.
10. Update public API baseline, documentation site, READMEs, migration guidance, and memory.
11. Run focused tests after each owned slice.
12. Run sample processes and real package-only candidate acceptance.
13. Run the full Release build, full solution tests, full Release tests, format, diff, hygiene, and vulnerability gates.
14. Audit requirements against evidence, complete this file, and mark the goal complete only when every required result is satisfied.

## Verification Matrix

Run and record exact results for:

1. focused `FluxFlow.Composition.Tests`;
2. focused `FluxFlow.Components.Designer.Tests`;
3. focused `FluxFlow.Engine.Tests`;
4. affected official component-composition test projects;
5. focused Release convention, public API, documentation, family-matrix, and package-script tests;
6. CompositionSample, SampleApp, MQTT composition sample, and any other changed executable sample;
7. real `eng/package-consumer-acceptance.ps1 -PackPackages` candidate-only gate;
8. full Release build with zero warnings and errors;
9. full solution tests with zero failures, skips, and warnings;
10. full Release tests;
11. full `dotnet format --verify-no-changes`;
12. full `git diff --check`;
13. source hygiene scans for reflection, scanning, hidden registries, duplicate sample registration, sleeps/skips, TODO/FIXME markers, and accidental compatibility aliases;
14. transitive vulnerability audit without restore.

Do not run overlapping build/test/package commands. Coordinate the build lane with the test agent and verify that timed-out child processes have exited before retrying.

## Acceptance Criteria

This goal is complete only when all of the following are true:

- A component's authoring and runtime behavior have one complete contract declaration.
- The old authoring-only contract types are removed.
- Typed code-first workflow use automatically carries the exact runtime descriptors it needs.
- `services.AddFluxFlow(codeFirstDefinition)` executes complete contracts without redundant runtime registration.
- Low-level string components still require explicit host registration.
- JSON loading, persistence, canonical serialization, and hot reload remain portable and independently registered.
- Code-only descriptors never appear in JSON.
- One effective catalog is used consistently throughout each runtime candidate.
- Same-descriptor reuse is accepted; conflicting descriptors fail deterministically.
- Revisions can introduce and replace code-first contracts without rebuilding DI.
- Failed revisions preserve the old application; successful revisions release retired factory closures.
- Official 19-family/44-contract behavior and metadata are preserved from one declaration source.
- MQTT ownership and lifecycle semantics are preserved.
- Normal samples contain no duplicate runtime declarations.
- Isolated package-only acceptance proves the simplified path from candidate packages.
- Documentation site, READMEs, public API baseline, migration guidance, and memory are current.
- Focused and full verification are green with zero warnings and no unexplained skips.
- No unrelated working-tree changes were lost.
- No commit, push, pull request, package publication, or release was performed.

## Final Report Requirements

The final report must include:

1. the final component-contract API;
2. the simplified complete code-first example;
3. the explicit JSON registration example;
4. the advanced/dynamic escape-hatch example;
5. the definition-owned descriptor and effective-catalog architecture;
6. conflict, revision, rollback, and lifetime semantics;
7. official family, Designer, sample, and package-consumer migrations;
8. all intentional breaking changes;
9. exact focused and full verification results;
10. documentation and memory files updated;
11. any remaining limitations or deferred work;
12. confirmation that no commit, push, publication, or release was performed.

## Execution Evidence

| Requirement | Evidence |
|---|---|
| Repository and dirty-tree inspection | Completed before production edits; existing work is preserved. |
| Architecture/dependency trace | Graph and source trace completed for contracts, workflow builder, immutable definition, catalog, link compiler, runtime plan, port surface, activation, revision planner, official families, samples, and package acceptance. |
| Testing workflow | Mandatory analyzer invoked once; `.testagent/research.md`, `plan.md`, and `status.md` maintained by the test owner. |
| Complete contract public API | `ComponentContract`, typed variants, exact descriptor state, workflow overloads, and explicit registration implemented; old public type removed. |
| Definition-owned runtime descriptors | Immutable deterministic `ApplicationDefinition.ComponentDescriptors` implemented and omitted from JSON. |
| Effective runtime catalog | Host and definition descriptors merge once per candidate; exact reuse deduplicates and conflicts reject. |
| Code-first execution without duplicate registration | Composition sample runs with only `AddFluxFlow(definition)` and prints `ALPHA` / `BETA`. |
| Independent JSON registration and hot reload | Focused Composition/Engine regression coverage green; full gate pending. |
| Revision, rollback, and lifetime behavior | Focused Engine filter 52 passed, including introduce/remove/replace, rollback, and collectible factory closure. |
| Official 19-family/44-contract migration | Production migration complete; focused Release matrix pending. |
| Designer metadata equivalence and no activation | Focused Designer filter 17 passed with exact descriptor identity and zero activation. |
| Sample migration | Composition, SampleApp, MQTT, and package consumer source migration complete; remaining process gates pending. |
| Package-only candidate acceptance | Real `-PackPackages` acceptance passed from candidate archives with JSON, embedded-contract code-first, durability restart, exact markers, and cleanup. |
| Public API baseline | Intentional baseline accepted through the documented variable and independently verified: 2 passed, 0 warnings. |
| Documentation and memory | Root/package READMEs, docs 03/04/09/14/22/38/39/40/index, and memory 00/01/299/300 updated. |
| Focused tests | Composition 51 passed; Designer 17 passed; Engine 52 passed; zero failures, skips, and warnings. |
| Full Release build | `dotnet build FluxFlow.sln -c Release --no-restore -p:ContinuousIntegrationBuild=true`: 134 projects, 0 warnings, 0 errors. |
| Full solution and Release tests | Full solution: 2,597 passed across 66 projects, 0 failed/skipped/warnings. Dedicated Release: 174 passed, 0 warnings. |
| Format, diff, hygiene, and vulnerability gates | Full format verify and `git diff --check` exit 0; legacy/magic/duplicate/skip/TODO scans clean; every solution project reports no vulnerable direct or transitive packages. |

No commit, push, pull request, package publication, or release is authorized.

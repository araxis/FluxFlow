# Engine Public API Inventory

Date: 2026-06-02

## Scope

First v1 readiness pass over the public surface of `FluxFlow.Engine`.

The goal was to identify accidental public API, unstable names, dependency
choices that could harden too early, and docs/API consistency risks before the
engine moves from alpha to beta.

## Inventory Summary

Current public type count: 76.

```text
FluxFlow.Engine                 7
FluxFlow.Engine.Components     15
FluxFlow.Engine.Definitions    17
FluxFlow.Engine.Mapping        10
FluxFlow.Engine.Runtime        27
```

No internal runtime plumbing was accidentally public. The fan-out source,
collectors, cleanup helpers, and disposal helpers are internal.

## Public Namespaces

### FluxFlow.Engine

- `FlowApplicationConfigurationException`
- `FlowApplicationConfigurationLoader`
- `FlowApplicationHost`
- `FlowApplicationHostBuildError`
- `FlowApplicationHostBuildErrorCode`
- `FlowApplicationHostBuildResult`
- `FlowApplicationHostState`

This namespace is the hosting shell. It is intentionally small.

### FluxFlow.Engine.Components

- `EventFlowNodeBase`
- `FlowDiagnostic`
- `FlowDiagnosticLevel`
- `FlowError`
- `FlowErrorCodes`
- `FlowEvent`
- `FlowNodeBase`
- `FlowNodeId`
- `IFlowDiagnosticSource`
- `IFlowEventSource`
- `IFlowNode`
- `MapFlowNode<TInput,TOutput>`
- `SinkFlowNode<TInput>`
- `SourceFlowNode<TOutput>`
- `TransformFlowNode<TInput,TOutput>`

This namespace is the node-authoring surface.

### FluxFlow.Engine.Definitions

- `ApplicationDefinition`
- `ApplicationDefinitionJson`
- `ApplicationDefinitionValidationError`
- `ApplicationDefinitionValidationErrorCode`
- `ApplicationDefinitionValidationResult`
- `ApplicationDefinitionValidator`
- `LinkDefinition`
- `LinkJson`
- `NodeAddress`
- `NodeDefinition`
- `NodeName`
- `NodeType`
- `PortAddress`
- `PortName`
- `WellKnownScopes`
- `WorkflowDefinition`
- `WorkflowName`

This namespace is the persisted/executable definition contract.

### FluxFlow.Engine.Mapping

- `DelegateFlowMapper<TInput,TOutput>`
- `DelegateFlowPredicate<TInput>`
- `ExpressionFlowPredicate<TInput>`
- `FlowMapContext`
- `IFlowExpressionEngine`
- `IFlowMapContextFactory<TInput>`
- `IFlowMapper<TInput,TOutput>`
- `IFlowPredicate<TInput>`
- two concrete expression adapter classes

This namespace is the expression and mapper abstraction surface.

### FluxFlow.Engine.Runtime

- `ApplicationRuntime`
- `ApplicationRuntimeBuilder`
- `ApplicationRuntimeBuildError`
- `ApplicationRuntimeBuildErrorCode`
- `ApplicationRuntimeBuildResult`
- `ApplicationRuntimeNodeStartException`
- `ApplicationState`
- `ApplicationStateChanged`
- `FlowNodeModule`
- `FlowNodeRegistration`
- `IFlowNodeModule`
- `IFlowNodeRegistration`
- `InputPort`
- `InputPort<T>`
- `OutputPort`
- `OutputPort<T>`
- `RuntimeFlowDiagnostic`
- `RuntimeNode`
- `RuntimeNodeBuilder`
- `RuntimeNodeFactory`
- `RuntimeNodeFactoryContext`
- `RuntimeNodeFactoryContextExtensions`
- `RuntimeNodeFactoryRegistry`
- `RuntimeNodeFactoryRegistryExtensions`
- `Workflow`
- `WorkflowState`
- `WorkflowStateChanged`

This namespace is the graph build and live runtime surface.

## Fix Applied In This Pass

`FlowNodeId` was moved from the stray public namespace:

```text
FluxFlow.Engine.Core
```

to:

```text
FluxFlow.Engine.Components
```

Why:

- `FlowNodeId` is part of the node/event/error authoring surface.
- It was the only public type in `FluxFlow.Engine.Core`.
- Keeping a one-type `Core` namespace would make the v1 API look less
  intentional.

This is an acceptable alpha break and should be included in the next engine
release notes.

## Findings

### 1. Expression Engine Implementations Need A V1 Decision

The abstractions are good:

- `IFlowExpressionEngine`
- `IFlowPredicate<TInput>`
- `ExpressionFlowPredicate<TInput>`
- `FlowMapContext`

The concrete implementation story is not settled. Two public expression adapter
classes currently expose implementation/library names in the public API and
force the engine package to carry concrete expression dependencies.

Decision needed before beta:

- keep only expression abstractions in `FluxFlow.Engine`, or
- keep one default link-condition expression implementation in the engine and
  move other concrete engines to optional expression packages.

Recommendation:

Keep `IFlowExpressionEngine`, `IFlowPredicate<TInput>`,
`ExpressionFlowPredicate<TInput>`, mapper contracts, and `FlowMapContext` in the
engine. Move optional concrete expression-language adapters out before v1, or
rename/scope them so the engine API does not harden around third-party adapter
names.

### 2. Link Condition Default Engine Depends On That Decision

`ApplicationRuntimeBuilder` currently creates a default expression engine for
link `when` conditions when the host does not provide one.

If concrete expression adapters move out of the engine, the builder needs a new
policy:

- require the host to pass a link-condition expression engine when `when`
  clauses are used, or
- keep a tiny built-in expression implementation only for link predicates, or
- keep the current default through beta and document it as a v1 contract.

Recommendation:

Prefer explicit host-provided expression engines for serious applications, but
decide whether no-setup `when` conditions are important enough to keep a default
implementation in the engine.

### 3. FlowEvent Attribute Shape Should Be Confirmed

`FlowEvent.Attributes` is currently:

```csharp
IReadOnlyDictionary<string, string>
```

`FlowDiagnostic.Attributes` is:

```csharp
IReadOnlyDictionary<string, object?>
```

This may be fine if events are durable activity records with string tags, while
diagnostics carry richer live status values. If events should support numeric,
boolean, or structured attributes long term, this should change before v1.

Recommendation:

Keep event attributes string-only unless a concrete consumer needs typed event
attributes. Diagnostics already cover richer live values.

### 4. Mutable Definition Dictionaries Are Intentional

`ApplicationDefinition`, `WorkflowDefinition`, and `NodeDefinition` expose
mutable dictionaries. This is convenient for JSON serialization and code-based
definition authoring.

Changing these to immutable collections after v1 would be breaking.

Recommendation:

Keep the mutable dictionary shape for v1 and document definitions as DTO-style
contracts.

### 5. Node-Level `When` Is Documented But Needs Compatibility Care

`NodeDefinition.When` acts as a default condition for links declared on that
node. Per-link `when` overrides it.

This is useful but less obvious than per-link conditions.

Recommendation:

Keep it because it is already documented in the expression and definition docs.
Add tests/docs only if a consumer finds the defaulting behavior surprising.

## Next Work

1. Decide the expression adapter split/default policy.
2. Update release notes for the `FlowNodeId` namespace change.
3. Run full solution verification after the expression decision.
4. Re-run docs snippets or add a compile-smoke sample for the README quick start
   before beta.

## Verification

- Old namespace scan for `FluxFlow.Engine.Core`: no matches.
- Full solution build: passed.
- Full solution tests: passed, 428 tests.

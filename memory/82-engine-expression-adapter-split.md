# Engine Expression Adapter Split

Date: 2026-06-02

## Decision

Keep expression abstractions in `FluxFlow.Engine`, but remove concrete
expression-language adapters from the engine package.

The engine owns:

- `IFlowExpressionEngine`
- `IFlowPredicate<TInput>`
- `ExpressionFlowPredicate<TInput>`
- `IFlowMapContextFactory<TInput>`
- `FlowMapContext`
- mapper/predicate helper contracts
- the conditional-link runtime hook

The engine does not own:

- concrete expression language implementations
- expression parser dependencies
- expression package policy
- app-specific expression variables beyond the default `input` and `value`
  context used by `ExpressionFlowPredicate<TInput>`

## Runtime Behavior

Definitions without `when` conditions build without an expression engine.

Definitions with resolved `when` conditions now require the host to pass an
`IFlowExpressionEngine` to `ApplicationRuntimeBuilder` or
`FlowApplicationHost.Create(...)`.

When a resolved `when` condition exists and no expression engine is supplied,
runtime build fails with:

```text
ApplicationRuntimeBuildErrorCode.MissingExpressionEngine
```

## Why

The base engine should stay small, neutral, and dependency-light. Expression
language adapters are useful, but they are optional integration choices and can
be published later as separate packages.

This also avoids hardening the engine v1 API around adapter names that may
change independently from the runtime.

## Future Package Shape

Optional adapter packages can follow this style later:

```text
FluxFlow.Expressions.<AdapterName>
```

or:

```text
FluxFlow.Components.Expressions.<AdapterName>
```

Choose the final package family only when a real consumer needs the adapter.

## Compatibility

This is an alpha breaking change.

Host code that uses link `when` conditions must now pass an expression engine:

```csharp
var builder = new ApplicationRuntimeBuilder(
    registry,
    linkConditionExpressionEngine: expressionEngine);
```

or:

```csharp
var host = FlowApplicationHost.Create(
    definition,
    registry,
    expressionEngine);
```

Component packages are mostly unaffected because they already receive expression
engines from their own package options.

## Verification

- `dotnet build FluxFlow.sln --configuration Release`
- `dotnet test FluxFlow.sln --configuration Release --no-build`

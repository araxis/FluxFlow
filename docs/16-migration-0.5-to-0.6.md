# Migration 0.5 To 0.6

This guide covers application changes from `FluxFlow.Engine` `0.5.0-alpha.1`
to `0.6.0-beta.1` and later stable releases that keep the same public boundary.

## Update The Package

Update the engine package reference:

```text
FluxFlow.Engine 0.6.0-beta.1
```

For a stable v1 upgrade, use:

```text
FluxFlow.Engine 1.0.0
```

Component packages move independently. Do not update a component package unless
the host needs that package's changes.

## FlowNodeId Namespace

`FlowNodeId` moved into the node-authoring namespace.

Old namespace:

```csharp
using FluxFlow.Engine.Core;
```

New namespace:

```csharp
using FluxFlow.Engine.Components;
```

If the host already imports `FluxFlow.Engine.Components`, no additional using
may be required.

## Link Conditions Need A Host Expression Engine

`FluxFlow.Engine` no longer includes concrete expression-language adapters.

If any executable definition uses link `when` conditions, pass an
`IFlowExpressionEngine` from the host.

Direct runtime builder:

```csharp
var builder = new ApplicationRuntimeBuilder(
    registry,
    linkConditionExpressionEngine: expressionEngine);
```

Hosted runtime:

```csharp
var host = FlowApplicationHost.Create(
    definition,
    registry,
    expressionEngine);
```

Definitions without `when` conditions do not need an expression engine.

If the host builds a definition with `when` and no expression engine, runtime
build fails with:

```text
ApplicationRuntimeBuildErrorCode.MissingExpressionEngine
```

## Remove Engine Expression Adapter References

Remove references to concrete expression adapter classes that used to live in
the engine package. Keep the adapter implementation in the host or in a separate
package.

The engine still provides:

- `IFlowExpressionEngine`
- `FlowMapContext`
- `IFlowMapContextFactory<TInput>`
- `ExpressionFlowPredicate<TInput>`
- mapper and predicate helper contracts

## Sample Host Pattern

For a small app-owned expression engine:

```csharp
public sealed class AppExpressionEngine : IFlowExpressionEngine
{
    public string Name => "app";

    public object? Evaluate(
        string expression,
        FlowMapContext context,
        Type resultType)
    {
        // Evaluate only the expressions your app supports.
        // Return a bool for link conditions.
        throw new NotImplementedException();
    }
}
```

For production hosts, keep expression validation and available variables under
application ownership. The engine only needs the evaluator contract.

## Checklist

1. Update `FluxFlow.Engine`.
2. Replace `FluxFlow.Engine.Core` imports for `FlowNodeId`.
3. Add or reuse a host-owned `IFlowExpressionEngine`.
4. Pass the expression engine when building hosted definitions with `when`.
5. Run workflow build/start/stop tests.
6. Run at least one app definition that uses conditional links.
7. Check runtime diagnostics and build errors for unexpected changes.

## Expected Outcome

After migration, executable definitions should keep the same shape except for
the host now owning expression-language selection. Workflows without conditional
links should build as before.

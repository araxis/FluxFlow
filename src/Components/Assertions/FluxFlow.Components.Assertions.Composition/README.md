# FluxFlow.Components.Assertions.Composition

Optional configuration registration and typed C# definition support.

Expression engine is required; optional context factory and clock remain host-owned. Assertion results and errors use Output.

## DI registration

```csharp
services.AddAssertions();
```

The callback constructs the node and declares its actual Input, Output and Events
with context.HasInput/HasOutput/HasEvents. No ComponentContract is constructed.
Standalone nodes remain usable without this package. No UI services are registered.

## C# definition

`AddAssertion` retains typed options and named Input, Output and Events handles.
It embeds the same callback used by DI in ApplicationSetup.ComponentFactories.
Build the setup with application services or pass it to AddFluxFlowUpdates; repeating
AddAssertions is optional. Identical callbacks are idempotent; conflicting factories
for the same type fail. Runtime links validate the constructed ports.

The former AssertionsComponents contract container is removed. Use AddAssertion for
configuration-based C# construction, or construct the standalone node directly.
Executable callbacks remain in process and cannot be exported as portable JSON.

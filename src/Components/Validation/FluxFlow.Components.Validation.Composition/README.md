# FluxFlow.Components.Validation.Composition

Optional configuration registration and typed C# definition support.

Inline schema, schema path/id, selector and runtime options remain flat. The optional selector and clock remain host-owned. Validation results and errors use Output.

## DI registration

```csharp
services.AddValidation();
```

The callback constructs the node and declares its actual Input, Output and Events
with context.HasInput/HasOutput/HasEvents. No ComponentContract is constructed.
Standalone nodes remain usable without this package. No UI services are registered.

## C# definition

`AddJsonSchemaValidator` retains typed options and named Input, Output and Events handles.
It embeds the same callback used by DI in ApplicationSetup.ComponentFactories.
Build the setup with application services or pass it to AddFluxFlowUpdates; repeating
AddValidation is optional. Identical callbacks are idempotent; conflicting factories
for the same type fail. Runtime links validate the constructed ports.

The former ValidationComponents contract container is removed. Use AddJsonSchemaValidator for
configuration-based C# construction, or construct the standalone node directly.
Executable callbacks remain in process and cannot be exported as portable JSON.

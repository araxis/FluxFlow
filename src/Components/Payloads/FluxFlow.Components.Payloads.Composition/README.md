# FluxFlow.Components.Payloads.Composition

Optional configuration registration and typed C# definition support.

Inspection limits, previews, detection and formatting remain component-owned options. The optional clock remains host-owned.

## DI registration

```csharp
services.AddPayloads();
```

The callback constructs the node and declares its actual Input, Output and Events
with context.HasInput/HasOutput/HasEvents. No ComponentContract is constructed.
Standalone nodes remain usable without this package. No UI services are registered.

## C# definition

`AddPayloadInspection` retains typed options and named Input, Output and Events handles.
It embeds the same callback used by DI in ApplicationSetup.ComponentFactories.
Build the setup with application services or pass it to AddFluxFlowUpdates; repeating
AddPayloads is optional. Identical callbacks are idempotent; conflicting factories
for the same type fail. Runtime links validate the constructed ports.

The former PayloadsComponents contract container is removed. Use AddPayloadInspection for
configuration-based C# construction, or construct the standalone node directly.
Executable callbacks remain in process and cannot be exported as portable JSON.

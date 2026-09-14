# FluxFlow.Components.Routing.Composition

Structural JSON registration and portable definition for `flow.window`,
`flow.correlate`, `flow.join`, and `flow.gate`.

The configured nodes expose canonical `FlowValue` ports and materialize JSON locally. Window has one Input;
Correlation has one Input and host-owned key/side selector delegates; Join has
Left and Right inputs with host-owned key selectors. Each exposes one canonical
outcome Output and Events. Match, timeout, and error routing use normal link
conditions; there are no compatibility branch or Errors ports.

Composition does not own selector or clock resources and has no UI dependency.

## DI Registration

This optional application-integration adapter registers direct factory callbacks
through `IServiceCollection`. Each callback declares its actual native ports:

```csharp
services.AddRouting();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddWindow`, `AddCorrelation` and `AddJoin` retain typed options and port handles.
They embed the same callbacks used by AddRouting in ApplicationSetup.ComponentFactories;
there is no RoutingComponents contract container. Build with application services,
or pass the setup to AddFluxFlowUpdates. Repeating AddRouting in DI is optional and
idempotent when it names those same factories. Different factories for the same type
are rejected. Links are validated against the constructed instances.

```csharp
var builder = new ApplicationDefinitionBuilder();
var window = builder.AddWorkflow("main")
    .AddWindow("window", options => options.MaxItems = 10);
window.Events.DiscardUnmatched(); // Explicitly discard events if this app does not consume them.
var setup = builder.Build();
```

The typed convenience methods still describe portable options and addresses, while
their executable factories remain in process and cannot be exported as JSON.



## Gate configuration

`AddRouting()` registers `flow.gate` with a direct node factory. The callback
declares Input, Open, Close, Output and Events through context.HasInput,
context.HasOutput and context.HasEvents. No Gate contract is created. Gate needs no separate
configuration builder or descriptor. Example component entry inside a workflow:

```json
"gate": {
  "Type": "flow.gate",
  "initiallyOpen": false,
  "boundedCapacity": 128,
  "Input": "source.Output",
  "Open": "enable.Output",
  "Close": "disable.Output",
  "Output": "sink.Input"
}
```

Source, enable, disable and sink are other components in that workflow. Open/Close
ignore the control payload. Input drops values while closed; errors remain visible
on Output. The factory binds only the two local options; Gate validates capacity.
For ordinary C#, construct `new GateNode(...)` and add it directly to a workflow.

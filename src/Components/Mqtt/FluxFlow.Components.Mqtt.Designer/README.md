# FluxFlow.Components.Mqtt.Designer

Optional presentation metadata for the four messaging components. Runtime
registration and direct component construction do not require this package.

```csharp
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Designer;
using FluxFlow.Composition;

services.AddFluxFlowComponents().AddMqtt().AddMqttDesign();
```

Register runtime contracts first. AddMqttDesign decorates their structural metadata
with display hints, defaults, enum choices, resource pickers and diagnostic event
descriptions. It does not construct components, start clients, resolve resources,
or register another runtime. Repeated identical registration is idempotent.

Requires the coordinated local package versions. Supports net8.0 and net10.0.

# FluxFlow.Components.Resilience.Designer

Optional palette and inspector metadata for the registered resilience
component contract. Runtime registration and authoring do not require this package.

```csharp
using FluxFlow.Components.Resilience.Composition;
using FluxFlow.Components.Resilience.Designer;

services.AddFluxFlowComponents().AddResilience().AddResilienceDesign();
```

Register the runtime family first. Design registration is idempotent and decorates
the existing structural descriptor without creating components or resolving host
resources. Ports, required options/resources and processing capabilities remain
owned by the runtime contract. Defaults, labels, choices and editor hints are
presentation-only; runtime configuration validation remains in the component.

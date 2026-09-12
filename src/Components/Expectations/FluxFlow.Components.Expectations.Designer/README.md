# FluxFlow.Components.Expectations.Designer

Optional palette and inspector metadata for the registered expectations
component contract. Runtime registration and authoring do not require this package.

```csharp
using FluxFlow.Components.Expectations.Composition;
using FluxFlow.Components.Expectations.Designer;

services.AddFluxFlowComponents().AddExpectations().AddExpectationsDesign();
```

Register the runtime family first. Design registration is idempotent and decorates
the existing structural descriptor without creating components or resolving host
resources. Ports, required options/resources and processing capabilities remain
owned by the runtime contract. Defaults, labels, choices and editor hints are
presentation-only; runtime configuration validation remains in the component.

# FluxFlow.Components.Metrics.Designer

Optional editor metadata for the Metrics component family. Runtime registration and
constructor-based component creation do not require this package.

```csharp
using FluxFlow.Components.Metrics.Composition;
using FluxFlow.Components.Metrics.Designer;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddFluxFlowComponents().AddMetrics().AddMetricsDesign();
```

The design registration decorates existing contracts; it does not activate nodes,
resolve host resources or replace structural facts. Register the runtime family
first. Repeating the same design registration is safe. Labels, defaults, choices,
resource pickers and port hints remain optional. This is unpublished local
development work, not a complete release candidate.


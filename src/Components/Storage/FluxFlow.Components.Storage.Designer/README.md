# FluxFlow.Components.Storage.Designer

Optional editor metadata for the Storage component family. Runtime registration and
constructor-based component creation do not require this package.

```csharp
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Storage.Designer;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddFluxFlowComponents().AddStorage().AddStorageDesign();
```

The design registration decorates existing contracts; it does not activate nodes,
resolve host resources or replace structural facts. Register the runtime family
first. Repeating the same design registration is safe. Labels, defaults, choices,
resource pickers and port hints remain optional. This is unpublished local
development work, not a complete release candidate.


# FluxFlow.Components.Validation.Designer

Optional visual metadata for the validation component family. Runtime-only
consumers use FluxFlow.Components.Validation.Composition without this package.

```csharp
using FluxFlow.Components.Validation.Composition;
using FluxFlow.Components.Validation.Designer;
using FluxFlow.Composition;

services.AddFluxFlowComponents().AddValidation().AddValidationDesign();
```

Call AddValidationDesign after runtime registration. It projects registered descriptors
without creating nodes or resolving host resources. Ports, input shapes, required
options/resources and processing capabilities remain owned by runtime registration;
this package supplies labels, editor hints, grouping, defaults and resource pickers.

Targets: net8.0 and net10.0. Use the coordinated prerelease package set.

# FluxFlow.Components.Mapping.Designer

Optional visual metadata for the mapping component family. Runtime-only
consumers use FluxFlow.Components.Mapping.Composition without this package.

```csharp
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Mapping.Designer;
using FluxFlow.Composition;

services.AddFluxFlowComponents().AddMapping().AddMappingDesign();
```

Call AddMappingDesign after runtime registration. It projects registered descriptors
without creating nodes or resolving host resources. Ports, input shapes, required
options/resources and processing capabilities remain owned by runtime registration;
this package supplies labels, editor hints, grouping, defaults and resource pickers.

Targets: net8.0 and net10.0. Use the coordinated prerelease package set.

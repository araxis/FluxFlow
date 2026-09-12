# FluxFlow.Components.Routing.Designer

Optional visual metadata for window, correlation and join components.

```csharp
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Routing.Designer;
using FluxFlow.Composition;

services.AddFluxFlowComponents().AddRouting().AddRoutingDesign();
```

Call AddRoutingDesign after runtime registration. It preserves editor hints,
defaults, bounds, selector type descriptions and input-port ordering without
creating nodes or resolving host selectors/clocks. Runtime-only consumers use
Routing.Composition without this package. The expression and engine options here
are diagnostic metadata; key/side selection uses required host-owned resources.

Targets net8.0/net10.0. Use the coordinated prerelease package set.

# FluxFlow.Components.Serialization.Designer

Optional editor presentation for the six serialization components. Runtime
registration and direct component construction do not depend on this package.

```csharp
using FluxFlow.Components.Serialization.Composition;
using FluxFlow.Components.Serialization.Designer;

services.AddFluxFlowComponents().AddSerialization().AddSerializationDesign();
```

Port types, input shapes, cardinality, options and resources come from registered
structural descriptors. This package adds labels, editor hints, default values and
resource pickers without constructing runtime components. Register components first.
Repeated identical presentation registration is idempotent; conflicting metadata
is rejected. Existing visual metadata consumers must add this package explicitly.

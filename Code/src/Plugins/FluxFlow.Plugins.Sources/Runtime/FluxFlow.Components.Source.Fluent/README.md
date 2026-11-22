# FluxFlow.Components.Source.Fluent

Use `.Source("samples", "[1,2]"u8.ToArray())` on a `FluxFlow.Application.WorkflowBuilder` with the `FluxFlow.Components.Source` namespace in scope. This package references the Source runtime and includes the build-time C# mapping for the `source` component.

The output capacity defaults to 128. Pass a positive integer to set it explicitly, for example `.Source("samples", "[1,2]"u8.ToArray(), outputCapacity: 1)`.

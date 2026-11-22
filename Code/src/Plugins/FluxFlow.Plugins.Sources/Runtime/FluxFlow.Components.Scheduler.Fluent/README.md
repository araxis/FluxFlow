# FluxFlow.Components.Scheduler.Fluent

Use `.Every("tick", TimeSpan.FromSeconds(1))` on a `FluxFlow.Application.WorkflowBuilder` with the `FluxFlow.Components.Scheduler` namespace in scope. This package references the Scheduler runtime and includes the build-time C# mapping for the `scheduler` component.

The output capacity defaults to 128. To set it explicitly, use a positive integer, for example `.Every("tick", TimeSpan.FromSeconds(1), outputCapacity: 1)`.

# FluxFlow.Components.Logging.Fluent

Use `.Logger("record", "Orders")` on a `FluxFlow.Application.WorkflowBuilder` with the `FluxFlow.Components.Logging` namespace in scope. This package references the Logging runtime and includes the build-time C# mapping for the `logger` component.

The output capacity defaults to 128. Use `.Logger("record", "Orders", outputCapacity: 1)` to set it explicitly. Pass `null` for the category to use the component address.

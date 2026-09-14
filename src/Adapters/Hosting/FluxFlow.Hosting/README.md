# FluxFlow.Hosting

Optional Generic Host lifecycle integration for directly constructed Core applications.

```csharp
services.AddFlowApplication(provider =>
{
    var app = new FlowApplication();
    var workflow = app.AddWorkflow("main");
    // Add component instances and connect their ports here.
    return app;
});
```

Import FluxFlow.Core and FluxFlow.Hosting. The factory runs on host start and can
resolve dependencies from DI. Each registration starts and owns one application;
host stop drains it and asynchronous provider disposal disposes it. For dependencies
or component instances owned by DI, retain their external ownership explicitly.
No Fluent builder or graph wrapper is involved. Use application RequestAsync and
MakeFunction directly. This is an unpublished development preview.
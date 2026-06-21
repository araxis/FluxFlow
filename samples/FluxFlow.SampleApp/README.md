# FluxFlow Sample App

This console sample shows how an application can keep its own workspace model
while using `FluxFlow.Engine` only for the executable workflow.

The sample workspace owns extra sections:

- `Views`: UI-facing metadata that the engine does not need.
- `Checks`: app-owned verification metadata.

Only `Resources` and `Workflows` are projected into `ApplicationDefinition`.
The sample also shows engine-specific package-style component registration
through `RegisterSampleOrderComponents`, backed by an `IFlowNodeModule`.
Because the sample uses conditional links, it also passes a small host-owned
expression engine to `FlowApplicationHost.Create(...)`.

Run it with:

```sh
dotnet run --project samples/FluxFlow.SampleApp/FluxFlow.SampleApp.csproj
```

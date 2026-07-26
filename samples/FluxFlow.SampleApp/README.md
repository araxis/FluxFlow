# FluxFlow Sample App

This console sample shows how an application can keep its own workspace model
while projecting only its executable portion into the canonical FluxFlow
application model.

The sample workspace owns extra sections:

- `Views`: UI-facing metadata that the engine does not need.
- `Checks`: app-owned verification metadata.

Only `Resources` and `Workflows` are projected into
`FluxFlow.Composition.Model.ApplicationDefinition`. The sample registers
standalone nodes explicitly through DI-backed component descriptors, activates them
with canonical revision hosting and `ApplicationRuntimeAssembler`, and gathers
three component Events streams through one fan-in input. Because the sample
uses conditional links, the host registers a small `IFlowExpressionEngine`
through DI.

Run it with:

```sh
dotnet run --project samples/FluxFlow.SampleApp/FluxFlow.SampleApp.csproj
```

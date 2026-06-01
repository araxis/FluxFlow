# Getting Started

This page walks through the smallest useful path: install the package, run the
sample, then build a graph from an application definition.

## Install

```sh
dotnet add package FluxFlow.Engine --prerelease
```

Use a prerelease version while the engine is still in alpha.

## Run The Sample

From the repository root:

```sh
dotnet run --project samples/FluxFlow.SampleApp/FluxFlow.SampleApp.csproj
```

The sample creates three orders, reviews each order, then routes reviewed orders
to either a `priority` or `standard` sink with conditional links.

Expected shape:

```text
Workspace: sample-order-workspace
Views kept outside engine: 1
Checks kept outside engine: 1

priority: A-100 Harbor Market $125.00 priority=True
standard: A-101 Cedar Supply $42.00 priority=False
priority: A-102 Summit Works $230.00 priority=True

Events observed: 3
Diagnostics observed: 6
```

The component composition sample uses host-owned source and sink nodes with
reusable mapping/control nodes:

```sh
dotnet build samples/FluxFlow.MappingControlSample/FluxFlow.MappingControlSample.csproj /nr:false
dotnet run --project samples/FluxFlow.MappingControlSample/FluxFlow.MappingControlSample.csproj --no-build
```

The MQTT composition sample uses an in-memory host adapter, so it does not need
a live broker:

```sh
dotnet build samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj /nr:false
dotnet run --project samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj --no-build
```

## Basic Flow

Every app follows the same core steps:

1. Define node types and node factories.
2. Build an `ApplicationDefinition`.
3. Register node factories in `RuntimeNodeFactoryRegistry`.
4. Create a `FlowApplicationHost`.
5. Start, observe, stop, and dispose the host.

```csharp
var workspace = SampleWorkspaceDefinition.CreateDefault();
var store = new InMemoryOrderStore();
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterSampleOrderComponents(store);

await using var host = FlowApplicationHost.Create(
    workspace.ToEngineDefinition(),
    registry);

var result = await host.StartAsync();
if (!result.IsSuccess)
{
    foreach (var error in result.Errors)
        Console.Error.WriteLine(error.Message);
}
```

## What The Engine Does Not Own

The engine does not need to know about app screens, dashboards, storage, test
scenarios, or external protocol clients. Keep those in the application or a
component package, then project only executable resources and workflows into
`ApplicationDefinition`.

Next: [Definitions And Links](02-definitions-and-links.md).

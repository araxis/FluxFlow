# MQTT Composition Adapter

Date: 2026-06-21

## Decision

`FluxFlow.Components.Mqtt.Composition` is the optional composition adapter for
MQTT standalone nodes.

Core `FluxFlow.Components.Mqtt` remains pure: neutral contracts,
`MqttPublishNode`, `MqttTriggerNode`, options, diagnostics, and validation. The
new package references `FluxFlow.Composition` and `FluxFlow.Composition.Hosting`
so hosts can instantiate MQTT nodes from fluent/config definitions without
putting composition dependencies in the core MQTT package.

## Implemented

- Added `src/Mqtt/FluxFlow.Components.Mqtt.Composition`.
- Added `RegisterMqttNodes()` for explicit factory registration.
- Added constants:
  - `MqttCompositionNodeTypes.Publish = "mqtt.publish"`
  - `MqttCompositionNodeTypes.Trigger = "mqtt.trigger"`
  - `MqttCompositionPortNames.Input`
  - `MqttCompositionPortNames.Output`
  - `MqttCompositionPortNames.Responses`
  - `MqttCompositionResourceNames.Publisher`
  - `MqttCompositionResourceNames.TriggerSource`
  - `MqttCompositionResourceNames.Clock`
- Publish factory:
  - binds `MqttPublishOptions`
  - resolves keyed `IMqttPublisher` from resource `publisher`
  - exposes `Input` and `Output`
- Trigger factory:
  - binds `MqttTriggerOptions`
  - resolves keyed `IMqttTriggerSource` from resource `triggerSource`
  - exposes `Output` and request/reply `Responses`
- Optional keyed `TimeProvider` resource stays host-owned.
- Added focused tests under `tests/FluxFlow.Components.Mqtt.Composition.Tests`.
- Added `samples/FluxFlow.MqttCompositionSample`:
  - in-memory `IMqttTriggerSource` / `IMqttPublisher`
  - `mqtt.trigger -> sample.mqtt.reply -> mqtt.publish`
  - config and fluent definitions over the same factories
- Added package manifest, solution entries, changelog, package README, MQTT
  README composition guidance, and public API overview entry.

## Boundary

The composition adapter does not own broker settings, credentials, client
lifetime, reconnect policy, stores, secrets, or concrete MQTT client features.
Adapters or hosts still register keyed MQTT resources, and definitions only
reference those resource keys.

## Verification

- `dotnet test tests\FluxFlow.Components.Mqtt.Composition.Tests\FluxFlow.Components.Mqtt.Composition.Tests.csproj -v minimal`
  - 4 passed
- `dotnet test tests\FluxFlow.Composition.Hosting.Tests\FluxFlow.Composition.Hosting.Tests.csproj --no-restore -v minimal`
  - 5 passed
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - 33 passed
- `dotnet build FluxFlow.sln -v minimal`
  - 0 warnings, 0 errors
- `dotnet test FluxFlow.sln --no-build -v minimal`
  - passed across the solution
- `graphify update . --force`
  - 8563 nodes, 12734 edges, 831 communities
  - `graph.html` was skipped because the graph exceeds the local HTML
    visualization limit.
- `dotnet build FluxFlow.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true -v minimal`
  - 0 warnings, 0 errors
- `dotnet test FluxFlow.sln --configuration Release --no-build -v minimal`
  - passed across the solution
- `eng\package-release-dry-run.ps1 -Package components-mqtt-composition -Version 1.0.0 -SkipSolutionBuild`
  - `DRY_RUN_OK=FluxFlow.Components.Mqtt.Composition`
  - Solution build/test was skipped only after the exact Release build and
    Release no-build test steps had passed separately. Local dependency
    packages for `FluxFlow.Nodes`, `FluxFlow.Composition`, and
    `FluxFlow.Composition.Hosting` were packed into `artifacts/packages` for
    consumer smoke/feed verification because those packages are not available
    from the public feed yet.
- `graphify update . --force`
  - 8529 nodes, 12682 edges, 824 communities
  - `graph.html` was skipped because the graph exceeds the local HTML
    visualization limit.
- `dotnet run --project samples\FluxFlow.MqttCompositionSample\FluxFlow.MqttCompositionSample.csproj`
  - printed the expected config and fluent published messages.
- `dotnet build FluxFlow.sln -v minimal`
  - 0 warnings, 0 errors
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - 33 passed
- `dotnet test FluxFlow.sln --no-build -v minimal`
  - passed across the solution

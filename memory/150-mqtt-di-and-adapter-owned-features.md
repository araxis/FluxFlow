# 150 - MQTT DI and adapter-owned feature registration

Date: 2026-06-21

Status: implemented locally on `main`; not yet released.

## Decision

Keep dependency injection registration adapter-local. Do not create an umbrella
MQTT registration package that references every concrete MQTT adapter.

The core `FluxFlow.Components.Mqtt` package stays pure. It owns only the neutral
MQTT node contracts and protocol/request types. Feature-specific behavior such
as reconnect, storage, Last Will, transport, TLS, and queueing belongs to each
concrete adapter package and its options.

Each concrete adapter package should register its own client session and expose
that same singleton through the core MQTT contracts it implements. Nodes remain
workflow/composition objects and are still created and linked by the composition
layer, not automatically by adapter DI registration.

No core `MqttClientCapabilities` contract is used. Capability descriptors would
make the core package aware of adapter feature inventory and would add complexity
without changing how a host actually composes a concrete client.

## Adapter registration shape

Both concrete adapter packages now expose `AddFluxFlowMqttClient(...)` from
their adapter namespaces.

Each registration adds one keyed concrete client and projects the same singleton
as keyed:

- `IMqttPublisher`
- `IMqttTriggerSource`
- `IMqttClientHealthSource`

The public registration option type is named `MqttClientRegistrationOptions`
inside each adapter namespace. The namespace is the adapter boundary; the type
name stays neutral.

## MQTTnet adapter

`FluxFlow.Components.Mqtt.MqttNet` now has adapter-local registration for one
keyed `MqttNetClient`.

`MqttClientRegistrationOptions` controls hosted connect/disconnect:

- `ConnectWithHost = false` by default leaves connection lifetime to the
  composition layer.
- `ConnectWithHost = true` registers an `IHostedService` that calls
  `ConnectAsync` during host start and `DisconnectAsync` during host stop.

The MQTTnet adapter does not expose durable message/session store options
because the current adapter surface does not honestly implement that feature.

## Pulse adapter

`FluxFlow.Components.Mqtt.PulseMqtt` has adapter-local registration for one
keyed `PulseMqttClient`.

`MqttClientRegistrationOptions` controls hosted startup:

- `StartWithHost = true` by default registers an `IHostedService` that starts
  and stops the adapter client with the host.
- `WaitForConnectedOnStart = true` makes startup wait for a live connection.
- `StartWithHost = false` leaves startup to the composition layer.

## Adapter-only stores

Durable message and session stores remain adapter-owned. The Pulse adapter now
accepts optional `MessageStore` and `SessionStore` values on
`PulseMqttClientOptions`.

`MessageStore` requires `AllowOfflinePublishQueue = true`; otherwise
configuration fails instead of silently ignoring a store while strict
disconnected publish behavior remains selected. `SessionStore` is passed through
to Pulse MQTT as durable subscription state.

The core MQTT package does not define message-store or session-store contracts.
Other adapters should expose equivalent options only when their underlying
library can honestly implement them.

## Versioning

Unreleased local package version now is:

- `FluxFlow.Components.Mqtt.MqttNet` `1.1.0` for adapter-local DI registration
  and optional hosted connect/disconnect lifetime.
- `FluxFlow.Components.Mqtt.PulseMqtt` `1.1.0` for Pulse MQTT `2.5.0`, manual
  acknowledgement, adapter-local DI registration, and store hooks.

Core `FluxFlow.Components.Mqtt` remains at the already-published `4.0.0`.

## Verification

- `dotnet restore tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --nologo`
  passed.
- `dotnet restore tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --nologo`
  passed.
- `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.MqttNet\FluxFlow.Components.Mqtt.MqttNet.csproj --configuration Release --no-restore --nologo`
  passed for `net8.0` and `net10.0`.
- `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --no-restore --nologo`
  passed for `net8.0` and `net10.0`.
- `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `48` tests.
- `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `12` tests.
- `dotnet test tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `23` tests.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `33` tests.
- `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\eng\package-release-preflight.ps1 -Package components-mqtt-mqttnet`
  passed and resolved `FluxFlow.Components.Mqtt.MqttNet` `1.1.0`.
- `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\eng\package-release-preflight.ps1 -Package components-mqtt-pulsemqtt`
  passed and resolved `FluxFlow.Components.Mqtt.PulseMqtt` `1.1.0`.
- `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`
  passed.
- `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\eng\package-release-dry-run.ps1 -Package components-mqtt-mqttnet -Version 1.1.0`
  passed with `DRY_RUN_OK=FluxFlow.Components.Mqtt.MqttNet`.
- `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\eng\package-release-dry-run.ps1 -Package components-mqtt-pulsemqtt -Version 1.1.0`
  passed with `DRY_RUN_OK=FluxFlow.Components.Mqtt.PulseMqtt`.
- `graphify update . --force` refreshed local code graph output: `7995` nodes,
  `11998` edges, and `755` communities. `graph.html` was skipped because the
  graph exceeds the local HTML visualization limit.

After final review, MQTTnet hosted connect/disconnect was changed to opt-in by
default:

- `MqttClientRegistrationOptions.ConnectWithHost` now defaults to `false`.
- The MQTTnet DI tests now assert no hosted lifetime is registered by default
  and that `ConnectWithHost = true` registers the hosted lifetime.
- `dotnet test tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `23` tests.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed: `33` tests.
- `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\eng\package-release-dry-run.ps1 -Package components-mqtt-mqttnet -Version 1.1.0`
  passed with `DRY_RUN_OK=FluxFlow.Components.Mqtt.MqttNet`.
- `graphify update . --force` refreshed local code graph output after the
  default change: `7996` nodes, `12001` edges, and `749` communities.
  `graph.html` was skipped because the graph exceeds the local HTML
  visualization limit.

## Next

Publish the MQTTnet and Pulse MQTT adapter `1.1.0` updates together.

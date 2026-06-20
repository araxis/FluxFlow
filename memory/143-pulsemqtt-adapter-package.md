# 143 - Pulse MQTT adapter package

Date: 2026-06-20

Status: implemented on `work/mqtt-connection-pilot`; not merged or released.

## Decision

Add a second concrete MQTT client-library adapter beside the neutral MQTT
component package:

- Source project:
  `src/Mqtt/FluxFlow.Components.Mqtt.PulseMqtt/FluxFlow.Components.Mqtt.PulseMqtt.csproj`
- Test project:
  `tests/FluxFlow.Components.Mqtt.PulseMqtt.Tests/FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj`
- Package id: `FluxFlow.Components.Mqtt.PulseMqtt`
- Package version: `1.0.0`
- Release alias/tag prefix: `components-mqtt-pulsemqtt`

The core `FluxFlow.Components.Mqtt` package remains client-library-neutral.
Concrete adapter packages may use the concrete library/provider name because
their public purpose is to expose that integration.

## Adapter Shape

`PulseMqttClient` is the public session object. It implements:

- `IMqttPublisher`
- `IMqttTriggerSource`
- `IMqttClientHealthSource`
- `IAsyncDisposable`

It owns:

- Pulse `ResilientMqttClient` creation;
- default TCP/TLS transport setup, with an optional injected Pulse
  `IMqttTransportFactory` for tests/custom transports;
- explicit `StartAsync` / `StopAsync`;
- `ConnectAsync`, which starts the resilient lifecycle and waits until the
  client reaches `Connected`;
- broker host, port, client id, credentials, TLS flags, connect timeout,
  keep-alive, trace propagation, and user properties;
- MQTT Last Will configuration through `PulseMqttLastWillOptions`;
- publish mapping from `MqttPublishRequest` to Pulse `MqttPublishPacket`;
- trigger subscription streams through Pulse `MqttRouter` / `MqttRouteStream`;
- retained-message subscription flags through Pulse `MqttTopicFilter`;
- health events from Pulse `WatchState`.

By default, `PublishAsync` preserves FluxFlow's strict adapter semantics:
publishing while disconnected throws `MqttClientUnavailableException`. The
adapter exposes `AllowOfflinePublishQueue` as an explicit opt-in for Pulse's
offline publish queue.

## Acknowledgement

Pulse route streams manage MQTT protocol acknowledgement inside the client and
do not expose per-message manual acknowledgement. The adapter therefore supports
`MqttTriggerAcknowledgement.None` and rejects `OnEmit` /
`OnSuccessfulResponse` with `NotSupportedException`.

Request/reply mode remains usable with `Acknowledgement.None`; in that mode the
response signal coordinates graph behavior, not broker-level acknowledgement.

## Verification

- Pulse package version checked from NuGet: `Pulse.Mqtt.Client` `2.0.0`.
- Pulse source/API checked from the upstream repository and restored package
  before implementation.
- Adapter build:
  `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.PulseMqtt\FluxFlow.Components.Mqtt.PulseMqtt.csproj --configuration Release --no-restore --nologo`
  passed for `net8.0` and `net10.0`.
- Focused core MQTT tests:
  `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`48`).
- Focused MQTTnet adapter tests:
  `dotnet test tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`19`).
- Focused Pulse MQTT adapter tests:
  `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`8`), including an in-process `PulseMqttTestBroker` loopback test.
- Release convention tests:
  `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`33`).
- Full solution:
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`
  passed.
- `git diff --check` passed with only existing CRLF normalization warnings.
- `graphify update . --force` refreshed local graph output after code and memory
  updates: 7938 nodes, 11960 edges, 759 communities. `graph.html` was skipped
  because the graph exceeds the local HTML visualization limit.

## Pulse MQTT v2 Adoption

After the upstream Pulse MQTT `2.0.0` release, the adapter package reference was
updated from `Pulse.Mqtt.Client` `1.1.0` to `2.0.0`, and the test package
reference was updated from `Pulse.Mqtt.Testing` `1.1.0` to `2.0.0`. The only
source adjustment required was changing the local route stream attachment from
the old `OpenStream(...)` API to `OpenRouteStream(...)`; broker subscription
ownership already lived on the explicit `SubscribeAsync` call.

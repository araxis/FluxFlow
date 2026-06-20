# 142 - MQTTnet adapter package

Date: 2026-06-20

Status: implemented on `work/mqtt-connection-pilot`; not merged or released.

## Decision

Add the first concrete MQTT client-library adapter beside the neutral MQTT
component package:

- Source project:
  `src/Mqtt/FluxFlow.Components.Mqtt.MqttNet/FluxFlow.Components.Mqtt.MqttNet.csproj`
- Test project:
  `tests/FluxFlow.Components.Mqtt.MqttNet.Tests/FluxFlow.Components.Mqtt.MqttNet.Tests.csproj`
- Package id: `FluxFlow.Components.Mqtt.MqttNet`
- Package version: `1.0.0`
- Release alias/tag prefix: `components-mqtt-mqttnet`

The core `FluxFlow.Components.Mqtt` package remains client-library-neutral.
Concrete adapter packages may use the concrete library/provider name because
their public purpose is to expose that integration.

## Adapter Shape

`MqttNetClient` is the public session object. It implements:

- `IMqttPublisher`
- `IMqttTriggerSource`
- `IMqttClientHealthSource`
- `IAsyncDisposable`

It owns:

- MQTTnet `IMqttClient` creation.
- explicit `ConnectAsync` / `DisconnectAsync`;
- broker host, port, client id, credentials, TLS flags, connect timeout,
  keep-alive, reconnect delay, and automatic reconnect options;
- MQTT Last Will configuration through `MqttNetLastWillOptions`;
- publish mapping from `MqttPublishRequest` to MQTTnet application messages;
- trigger subscription streams from MQTTnet received messages to
  `IMqttReceivedContext`;
- topic-filter dispatch through MQTTnet's public `MqttTopicFilterComparer`
  instead of a package-owned matcher implementation;
- active subscription resubscribe after reconnect;
- health events using `MqttClientHealthEvent`.

Publish and subscribe throw `MqttClientUnavailableException` when the MQTTnet
client is disconnected. The core publish/trigger nodes translate that into their
existing not-connected diagnostics.

## Last Will

Last Will stays adapter-owned because it is registered during MQTT `CONNECT`.
`MqttNetLastWillOptions` mirrors the publish message shape where useful:

- required `Topic`;
- `Payload`;
- `ContentType`;
- `QualityOfService`;
- `Retain`;
- optional `MqttPublishProperties` for correlation data, response topic, and
  user properties.

The adapter validates Last Will topics with the core MQTT publish-topic
validator because the Last Will topic is still a MQTT publish topic.

## Acknowledgement

For trigger subscriptions:

- `MqttTriggerAcknowledgement.None` leaves MQTTnet auto-acknowledgement enabled.
- `OnEmit` and `OnSuccessfulResponse` disable MQTTnet auto acknowledgement and
  expose `AckAsync` / `NackAsync` through `IMqttReceivedContext`.
- `AckAsync` calls MQTTnet `AcknowledgeAsync`.
- `NackAsync` marks MQTTnet processing failed, sets an implementation-specific
  reason code/reason string when possible, and then completes the MQTTnet
  acknowledgement path. MQTT broker retry behavior remains broker/protocol
  dependent; MQTT has no transport-neutral guaranteed NACK behavior.

This keeps MQTT-specific acknowledgement policy inside the adapter and out of
`FluxFlow.Components.RequestReply`.

## Verification

- MQTTnet package version checked from NuGet: `5.1.0.1559`.
- MQTTnet API checked against restored assembly before implementation.
- Adapter build:
  `dotnet build src\Mqtt\FluxFlow.Components.Mqtt.MqttNet\FluxFlow.Components.Mqtt.MqttNet.csproj --configuration Release --no-restore --nologo`
  passed for `net8.0` and `net10.0`.
- Focused core MQTT tests:
  `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`48`).
- Focused adapter tests:
  `dotnet test tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`19`).
- Release convention tests:
  `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo`
  passed (`33`).
- Full solution:
  `dotnet test .\FluxFlow.sln --configuration Release --no-restore --verbosity quiet --nologo`
  initially hit one existing transient `FluxFlow.Nodes.Tests` failure; the
  failed test passed on isolated rerun, and a second full solution run passed.

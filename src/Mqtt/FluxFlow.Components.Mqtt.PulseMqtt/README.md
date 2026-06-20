# FluxFlow.Components.Mqtt.PulseMqtt

Pulse MQTT-backed adapter for `FluxFlow.Components.Mqtt`.

The core MQTT package stays client-library-neutral: `MqttPublishNode` needs only
`IMqttPublisher`, and `MqttTriggerNode` needs only `IMqttTriggerSource`. This
package supplies one concrete session object, `PulseMqttClient`, that implements:

- `IMqttPublisher`
- `IMqttTriggerSource`
- `IMqttClientHealthSource`

`PulseMqttClient` owns Pulse MQTT client creation, transport configuration,
start/stop, publish mapping, route-stream trigger subscriptions, Last Will
configuration, and health events.

## Usage

```csharp
var mqtt = new PulseMqttClient(new PulseMqttClientOptions
{
    Host = "localhost",
    Port = 1883,
    ClientId = "fluxflow-worker",
    LastWill = new PulseMqttLastWillOptions
    {
        Topic = "workers/fluxflow/status",
        Payload = "offline"u8.ToArray(),
        Retain = true,
        QualityOfService = MqttQualityOfService.AtLeastOnce,
        ContentType = "text/plain"
    }
});

await mqtt.ConnectAsync();

var publish = new MqttPublishNode(mqtt);
var trigger = new MqttTriggerNode(mqtt, new MqttTriggerOptions
{
    TopicFilter = "commands/+",
    Mode = MqttTriggerMode.RequestReply,
    Acknowledgement = MqttTriggerAcknowledgement.None
});
```

Use `ConnectAsync` when the caller needs the adapter to wait for a live session.
Use `StartAsync` when the caller wants Pulse MQTT's resilient background
connection lifecycle and will observe health events until the session becomes
connected.

By default, FluxFlow publish semantics stay strict: publishing while disconnected
throws `MqttClientUnavailableException`. Set
`AllowOfflinePublishQueue = true` to opt into Pulse MQTT's offline publish queue.

## Last Will

Last Will is adapter-owned because it is registered during MQTT `CONNECT`. It is
configured through `PulseMqttClientOptions.LastWill`, not through publish or
trigger node options. Use a normal `MqttPublishRequest` for graceful
online/offline status messages.

## Acknowledgement

Pulse MQTT route streams manage protocol acknowledgement inside the client and
do not expose per-message manual acknowledgement to this adapter. For that
reason, `PulseMqttClient` supports `MqttTriggerAcknowledgement.None` and rejects
manual acknowledgement modes. Request/reply mode can still be used with
`Acknowledgement.None`; the response signal coordinates graph behavior, not
broker-level acknowledgement.

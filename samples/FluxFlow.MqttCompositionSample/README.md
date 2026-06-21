# FluxFlow MQTT Composition Sample

Runs a complete MQTT-shaped composition without a broker.

The sample uses:

- `mqtt.trigger` from `FluxFlow.Components.Mqtt.Composition`.
- `sample.mqtt.reply`, a local transform node that maps `MqttReceivedMessage`
  to `MqttPublishRequest`.
- `mqtt.publish` from `FluxFlow.Components.Mqtt.Composition`.
- An in-memory object registered as keyed `IMqttTriggerSource` and
  `IMqttPublisher`.

```text
inbound.Output -> reply.Input -> reply.Output -> outbound.Input
```

Run it from the repository root:

```sh
dotnet run --project samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj
```

Expected output:

```text
configuration:
  devices/pump-01/state/reply -> ACK: online
  devices/pump-02/state/reply -> ACK: offline
fluent:
  devices/pump-01/state/reply -> ACK: online
  devices/pump-02/state/reply -> ACK: offline
```

`appsettings.json` shows the configuration shape. `Program.cs` builds the same
workflow with the fluent builder so both paths share the same node factories and
runtime behavior.

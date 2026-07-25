# FluxFlow MQTT Composition Sample

Runs a complete canonical MQTT publish workflow without a broker.

The sample uses:

- `sample.mqtt.publish-source`, a local source that emits
  `MqttPublishMessage` values with exact `FlowContent` bytes
- `mqtt.publish` from `FluxFlow.Components.Mqtt.Composition`
- an in-memory `IMqttClientController` registered as one host-owned resource
- the same canonical application shape through JSON and direct
  `ApplicationDefinition` construction

```text
source.Output -> outbound.Input
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
definition:
  devices/pump-01/state/reply -> ACK: online
  devices/pump-02/state/reply -> ACK: offline
```

`appsettings.json` shows the flat `Resources` and `Workflows` document.
`Program.cs` builds the same workflow directly so both paths share the same
component registration and canonical runtime behavior.

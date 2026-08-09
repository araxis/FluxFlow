# FluxFlow MQTT Composition Sample

Runs a complete canonical MQTT publish workflow without a broker.

The sample uses:

- `sample.mqtt.publish-source`, a local source that emits
  `MqttPublishMessage` values with exact `FlowContent` bytes
- `mqtt.publish` from `FluxFlow.Components.Mqtt.Composition`
- an in-memory `IMqttClientController` registered as one host-owned resource
- broker, retry-policy, subscription, and client resource declarations that
  remain inactive so the sample needs no network broker
- the same canonical application shape through JSON and the chain-first C#
  authoring style
- the portable `WebSocket` + `UseTls` broker shape for WSS, with no provider
  type in either definition path

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
`Program.cs` captures a resource group and workflow from one application
chain, chains typed MQTT resource declarations with final `out var` handles,
chains workflow component declarations, and then connects typed ports
explicitly. It also serializes the JSON-loaded and C#-authored definitions and
requires their canonical forms to match. The configured MQTT client has
auto-connect disabled and is not
used by the executable workflow; publishing uses the host-owned in-memory
controller, so no real broker is required. The builder produces the same
immutable `ApplicationDefinition`, and both paths share registration,
validation, serialization, and runtime behavior.

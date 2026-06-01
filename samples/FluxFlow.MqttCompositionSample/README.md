# FluxFlow MQTT Composition Sample

This console sample shows how a host can use `FluxFlow.Components.Mqtt` without a
live broker by providing an in-memory `IMqttClientFactory`.

The flow is:

```text
mqtt.subscribe -> flow.mapper -> flow.filter -> flow.mapper -> mqtt.publish -> result sink
```

The host owns:

- the in-memory MQTT adapter and connection resolution
- message type aliases and context factories
- the tiny expression engine used by the mapper and filter nodes
- the result sink used only by this sample

Run it from the repository root:

```sh
dotnet build samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj /nr:false
dotnet run --project samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj --no-build
```

Expected shape:

```text
Sample: mqtt-composition
Factory contexts: 2
Published messages: 2
orders/reviewed/A-100 bytes=69 qos=AtLeastOnce retain=False correlation=c-100
orders/reviewed/A-102 bytes=68 qos=AtLeastOnce retain=False correlation=c-102

Results observed: 2
Diagnostics observed: 15
```

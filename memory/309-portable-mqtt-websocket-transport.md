# Portable MQTT WebSocket Transport

Date: 2026-08-09

## Decision

FluxFlow represents the four portable MQTT broker modes with two orthogonal
configuration facts:

- `MqttBrokerTransport` selects `Tcp` or `WebSocket`;
- the existing `UseTls` selects plain or secure transport.

This produces TCP, TLS over TCP, `ws`, and `wss` without provider types or four
overlapping flags. `Transport` defaults to `Tcp`, preserving existing C# and
JSON behavior. `WebSocketPath` defaults to `/mqtt`.

## Validation boundary

- Undefined transport values are rejected before provider activation.
- WebSocket paths must be rooted and contain no query or fragment.
- A custom WebSocket path is rejected for TCP rather than silently ignored.
- `ServerName` remains a TCP/TLS override and is rejected for WebSocket because
  the providers do not expose equivalent portable SNI override behavior.
- Browser capability rules remain host policy. The neutral core does not use
  platform detection or silently remove native-only settings.

## Adapter mapping

- The Pulse adapter uses `TcpTransportFactory` or
  `WebSocketTransportFactory` from Pulse MQTT `2.29.0` and continues to create
  `RawMqttClient`. FluxFlow remains the reconnect, subscription,
  acknowledgement, and lifecycle owner.
- The MQTTnet adapter selects TCP or WebSocket channel options from the same
  neutral broker record and applies TLS/certificates through MQTTnet's channel
  options.
- The Pulse WebSocket package is referenced by the Pulse adapter only. No
  provider type enters core configuration, composition, portable JSON, or
  component contracts.

## Package line

- `FluxFlow.Components.Mqtt` `7.1.0`
- `FluxFlow.Components.Mqtt.Composition` `7.1.0-rc.1`
- `FluxFlow.Components.Mqtt.MqttNet` `3.1.0`
- `FluxFlow.Components.Mqtt.PulseMqtt` `4.1.0`

The prior local Pulse `4.0.1` dependency-only revision was not published and is
superseded by the additive `4.1.0` transport feature.

## Verification

Initial multi-targeted Release builds for the Pulse adapter, MQTTnet adapter,
MQTT composition package, and MQTT sample completed with zero errors and zero
warnings.

Focused xUnit/Shouldly results:

- MQTT core: 68/68;
- MQTT composition: 33/33, including the strengthened 3/3 invalid string and
  numeric enum cases;
- Pulse adapter: 10/10;
- MQTTnet adapter: 12/12;
- shared adapter conformance: 7/7;
- Release governance: 2/2.

All final runs reported zero warnings. The tests exposed and drove two concrete
corrections: typed C# authoring now emits transport names rather than enum
numbers, and portable MQTT JSON now disables integer enum values.

Release preflight passed for all four changed packages. Isolated `net8.0` and
`net10.0` adapter packages proved exact dependencies:

- Pulse adapter `4.1.0`: MQTT core `7.1.0`, Pulse client `2.29.0`, and Pulse
  WebSocket transport `2.29.0`;
- MQTTnet adapter `3.1.0`: MQTT core `7.1.0` and MQTTnet `5.1.0.1559`.

The temporary package directory was verified inside the system temporary root
and removed after inspection. Scoped formatting, whitespace, and diff checks
passed.

## Publication

Pull request 79 passed the ordinary restore, full build, complete test, and
package-consumer gates at head `f0d66384` and merged as `7ecd5df0`.

All four packages were then published from annotated tags targeting that exact
merge commit:

- `components-mqtt-v7.1.0`;
- `components-mqtt-composition-v7.1.0-rc.1`;
- `components-mqtt-mqttnet-v3.1.0`;
- `components-mqtt-pulsemqtt-v4.1.0`.

Trusted publication runs `31314352269`, `31315268340`, `31315288048`, and
`31315309061` passed the full release workflow, including durable-provider
integration, binary compatibility, archive inspection, isolated consumer
smoke, unpublished-version enforcement, credential-free publication,
fresh-feed restoration, and repository release creation. A separate final
availability check confirmed every exact version is present on the public
package feed.

## Exclusions

This round does not add QUIC, provider discovery, reflection, custom catalogs,
WebSocket headers/proxies/query strings, alternate subprotocols, browser
interop, or a second MQTT lifecycle owner.

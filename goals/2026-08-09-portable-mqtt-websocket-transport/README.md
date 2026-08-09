# Goal: Add portable TCP, TLS, WebSocket, and secure WebSocket MQTT transport selection

Date: 2026-08-09

Status: completed successfully

## Objective

Extend FluxFlow's provider-neutral MQTT broker configuration so the same C#
and portable JSON application definitions can explicitly select ordinary TCP,
TLS over TCP, MQTT over WebSocket (`ws`), or MQTT over secure WebSocket
(`wss`). Map that one neutral configuration through both official adapters:
Pulse MQTT and MQTTnet.

Keep the design small and explicit. Do not expose provider types, add
reflection, create a custom transport catalog, or duplicate lifecycle policy.
FluxFlow remains the sole owner of connection, reconnect, subscription,
acknowledgement, event, and disposal behavior.

## Frozen public configuration contract

Use two independent, readable facts instead of four partially duplicated
flags or provider-specific options:

1. Add public enum `MqttBrokerTransport` with exactly:
   - `Tcp`;
   - `WebSocket`.
2. Add `MqttBrokerConfiguration.Transport`, defaulting to
   `MqttBrokerTransport.Tcp`.
3. Keep the existing `MqttBrokerConfiguration.UseTls` flag:
   - `Tcp` + `false` means plain TCP;
   - `Tcp` + `true` means TLS over TCP;
   - `WebSocket` + `false` means `ws`;
   - `WebSocket` + `true` means `wss`.
4. Add `MqttBrokerConfiguration.WebSocketPath`, defaulting to `/mqtt`.
5. Preserve the existing immutable `sealed record` with `init` properties,
   including `Host`, `Port`, `UseTls`, and `ServerName`.
6. Preserve old C# and JSON definitions: omitting `Transport` must continue to
   produce the existing TCP behavior, and all existing TCP/TLS examples and
   applications must remain valid.
7. The transport enum must serialize and deserialize by its stable string name
   in portable MQTT resource definitions.

## Validation and endpoint rules

Apply transport-neutral validation once in the MQTT core before a provider
session is used:

- `Transport` must be a defined enum value.
- `Host` and `Port` keep their existing validation.
- For WebSocket transport, `WebSocketPath` must be non-empty, begin with `/`,
  and represent a path only; reject fragments and query text in this round.
- `ServerName` remains the TLS target-host override for TCP/TLS.
- Reject a `ServerName` override for WebSocket transport because the two
  providers cannot map an independent WebSocket SNI/target-host override with
  equivalent portable semantics. `wss` uses `Host` as its TLS host.
- Build WebSocket endpoints with `UriBuilder` so host, port, path, IPv6, and
  escaping are handled predictably.
- Keep MQTT's WebSocket subprotocol fixed to the standard `mqtt` value.
- Preserve existing certificate validation/loading. When a provider supports
  WebSocket client certificates, map the already-resolved FluxFlow certificate
  collection without exposing provider configuration callbacks.

## Core and composition changes

1. Add the enum and new immutable broker properties to
   `FluxFlow.Components.Mqtt`.
2. Extend `MqttBrokerResourceBuilder` with `Transport` and `WebSocketPath`.
3. Add the exact portable resource property names `Transport` and
   `WebSocketPath` to `MqttComponentDefinition.ResourceProperties`.
4. Allow those properties in `MqttCompositionResourceRegistrar` and bind them
   through the existing explicit `System.Text.Json` converter.
5. Keep JSON property validation strict: misspelled or provider-specific
   transport properties remain rejected.
6. Update C# authoring and JSON examples for both TCP/TLS and WebSocket/WSS.
7. Do not add another registration method, factory abstraction, resource type,
   or callback layer.

## Pulse MQTT adapter

1. Centrally pin `Pulse.Mqtt.Transport.WebSocket` to stable `2.29.0`, matching
   `Pulse.Mqtt.Client` and `Pulse.Mqtt.Testing`.
2. Reference that package only from `FluxFlow.Components.Mqtt.PulseMqtt`.
3. Keep `RawMqttClient`; do not construct Pulse's resilient or hosted client.
4. Keep the optional advanced provider transport-factory injection unchanged.
5. When no provider factory is injected:
   - create `TcpTransportFactory` for `Tcp`;
   - create `WebSocketTransportFactory` for `WebSocket`;
   - use `ws` or `wss` from `UseTls`;
   - use the configured host, port, and WebSocket path;
   - use subprotocol `mqtt`;
   - apply already-loaded client certificates to WebSocket client options
     when present.
6. Preserve exact message bytes, protocol properties, cancellation,
   acknowledgement mapping, reconnect ownership, and deterministic disposal.

## MQTTnet adapter

1. Continue using the current centrally pinned MQTTnet package.
2. Build TCP channel options for `Tcp` and WebSocket channel options for
   `WebSocket` from the same neutral configuration.
3. Apply TLS for TCP through the existing TLS options; use an explicit `wss`
   endpoint for secure WebSocket and map the same certificate collection into
   the WebSocket TLS options supported by MQTTnet.
4. Preserve credentials, client identity, clean start, keep alive, connect
   timeout, Last Will, MQTT v5 properties, acknowledgement behavior,
   cancellation, and disposal.
5. Do not add MQTTnet types to any core, composition, definition, or public
   authoring surface.

## Package versions and metadata

These are additive public capabilities on already-published packages:

- `FluxFlow.Components.Mqtt`: `7.0.0` -> `7.1.0`;
- `FluxFlow.Components.Mqtt.Composition`: `7.0.0-rc.1` -> `7.1.0-rc.1`;
- `FluxFlow.Components.Mqtt.MqttNet`: `3.0.0` -> `3.1.0`;
- `FluxFlow.Components.Mqtt.PulseMqtt`: supersede the local, unpublished
  `4.0.1` dependency-only revision with `4.1.0`.

Update package release notes, package overview documentation, package
manifests or version assertions that describe the current source versions,
and the repository changelog. Keep binary/API comparison baselines pointing to
the last published versions where those baselines intentionally represent the
release comparison source.

## Documentation and host guidance

Document one compact decision table and examples:

| Transport | UseTls | Result |
| --- | --- | --- |
| `Tcp` | `false` | TCP |
| `Tcp` | `true` | TLS |
| `WebSocket` | `false` | `ws` |
| `WebSocket` | `true` | `wss` |

State clearly:

- browser-hosted applications must use `WebSocket` and normally `wss`;
- desktop, service, and server hosts may use all four combinations;
- WebAssembly hosts must validate or hide unsupported native-only options such
  as client-certificate loading rather than silently ignoring them;
- browser capability policy belongs to the host application, not to provider
  detection or runtime magic in the MQTT core;
- advanced proxy headers, custom WebSocket subprotocols, signed query strings,
  QUIC, and provider callbacks remain advanced provider concerns outside this
  portable round.

Update the MQTT core, composition, Pulse adapter, and MQTTnet adapter READMEs,
the documentation site package overview, repository memory index, and progress
log. Add a new numbered memory record with the exact decisions and verification
results.

## Tests and verification

Use the existing xUnit/Shouldly projects and focused package/release gates.
Tests must prove behavior rather than only source presence.

Required evidence:

1. Core validation covers all four combinations, undefined transport,
   malformed WebSocket paths, and unsupported WebSocket `ServerName` override.
2. Existing configurations without `Transport` still validate and use TCP.
3. Composition C# authoring emits exact `Transport` and `WebSocketPath`
   properties, and JSON binding resolves the exact enum/path values.
4. Portable JSON rejects unknown transport properties and invalid enum values.
5. Pulse adapter construction selects the exact TCP or WebSocket provider
   factory, builds exact `ws`/`wss` URI values, uses `mqtt` subprotocol, and
   maps certificates without running a real broker.
6. MQTTnet adapter builds exact TCP/TLS/ws/wss channel options, including host,
   port, path, TLS target/certificates where supported, without a real broker.
7. Shared adapter conformance remains green and no provider type crosses the
   neutral public boundary.
8. Existing controller, reconnect, subscriptions, acknowledgements, events,
   and lifecycle tests remain green.
9. Focused Release conventions protect the public enum/property shape,
   provider-neutral source boundary, package dependency, versions, and docs.
10. Restore/build all affected projects for `net8.0` and `net10.0` through
    their multi-targeted package projects with zero warnings.
11. Run focused test projects:
    - `FluxFlow.Components.Mqtt.Tests`;
    - `FluxFlow.Components.Mqtt.Composition.Tests`;
    - `FluxFlow.Components.Mqtt.PulseMqtt.Tests`;
    - `FluxFlow.Components.Mqtt.MqttNet.Tests`;
    - `FluxFlow.Components.Mqtt.Adapters.Tests`;
    - focused `FluxFlow.Release.Tests` MQTT/package/documentation facts.
12. Run release preflight for the four changed FluxFlow MQTT packages.
13. Pack the two adapter packages into an isolated temporary output and inspect
    their dependency groups:
    - Pulse adapter requires Pulse client and WebSocket transport `2.29.0`;
    - MQTTnet adapter retains its intended MQTTnet dependency;
    - both require the updated neutral MQTT core version.
14. Run scoped formatting, whitespace, and `git diff --check` checks.

## Explicit exclusions

- No Pulse resilient client, Pulse dependency-injection host, Pulse health
  checks, Pulse router, or Pulse durable store integration.
- No QUIC transport.
- No custom WebSocket headers, proxy model, alternate subprotocol, query-string
  model, or provider callback in portable configuration.
- No reflection, dynamic provider discovery, custom catalog, or service-locator
  behavior.
- No duplicate reconnect, retry, subscription, acknowledgement, or lifecycle
  owner.
- No direct JavaScript, browser interop, or host-platform detection in the MQTT
  packages.
- No package publication, tag, release dispatch, or feed mutation.
- No unrelated framework, Fluent DSL, storage, health, or UI refactoring.

## Completion criteria

The goal is complete when existing TCP/TLS definitions remain compatible, the
new neutral configuration expresses WebSocket/WSS without provider types, both
official adapters map all four transport combinations correctly, FluxFlow
remains the sole lifecycle owner, all focused tests/builds/package inspections
pass with zero warnings, documentation and memory are current, and no excluded
complexity or publication action has been introduced.

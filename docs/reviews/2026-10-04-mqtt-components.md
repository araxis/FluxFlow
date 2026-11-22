# MQTT components

Added FluxFlow.Components.Mqtt and its setup folder in the existing Configuration
project. The runtime library uses MQTTnet 5.2.0.1603 and the existing Core contracts;
no Core or Application implementation changes were needed for this module.

MqttConnection accepts Connect/Disconnect and exposes state events. The shared
MqttClientResource owns automatic initial connection and timed reconnection, configured
through AutoConnect and Reconnect on the resource. The component forwards its
existing start/stop lifecycle to that resource; no additional lifecycle framework or
network activity during construction was introduced. Disconnect honors resource
AutoConnect; its optional DisableReconnect flag suppresses reconnection until Connect.
Shutdown always suppresses reconnection. MqttPublisher takes one publish
request per input and returns the native operation result with input lineage.
MqttSubscriber accepts initial configured topics and runtime Subscribe/Unsubscribe
commands. Broker deliveries start new envelopes, preserving binary payloads and
providing a UTF-8 text projection when valid.

MqttClientResource owns the native client and a semaphore coordinating network
operations with lifecycle changes. DI owns disposal. One connection controller and
one subscriber are allowed per resource; publishers may share it. The subscriber's
dictionary is current subscription state, not a DI lookup. Offline removals are
reconciled with persistent broker sessions. Live changes do not resubscribe unrelated
filters. Invalid commands and broker refusals follow normal processing-error handling.

AddMqtt registers mqtt.client, mqtt.connection, mqtt.publisher and mqtt.subscriber.
Configuration supports topic strings, objects and mixed arrays; QoS defaults to 0.
The registrar binds link properties through existing helpers. Registration and Build
do not connect. Runtime/configuration guides document command payloads and settings.

Reconnect uses Microsoft.Extensions.Resilience (no direct Polly package reference).
Its native retry strategy controls count, delay, backoff, jitter and maximum delay;
Polly is still a transitive dependency of Microsoft's package. ReconnectInterval is
removed in favor of Reconnect.Delay. Zero configured retries means one attempt;
the default preserves effectively unlimited constant five-second retries.
Successful connection loss starts a fresh budget; exhaustion waits for an explicit
Connect command and reports mqtt.reconnect_exhausted. Startup and loss notifications
use a bounded native channel instead of idle polling. The network gate is not held
during resilience backoff. Explicit Disconnect and shutdown cancel pending waits.
Subscription restoration belongs to its connection attempt: deliberate cleanup
disconnects cannot open another retry budget. Each attempt is bounded by the existing
MQTT Timeout; timeout failures are retriable, while application cancellation is not.
The immediate Connect command result and ordinary per-attempt error output remain.

Broker Address selects native MQTTnet URI handling for mqtt/mqtts/tcp/ws/wss. Existing
Host/Port with Tls is retained for TCP; contradictory schemes/settings reject Add.
ClientCertificate loads PFX/P12 or PEM/private-key files; TrustedCertificates loads
PEM/DER CA roots. Each certificate is a deferred native keyed singleton, disposed by
DI after the resource. Certificate loading occurs at Build; Add performs no file I/O.
Windows TLS requires native private-key handles, so PEM keys are reimported through
PKCS12 using the standard temporary key lifetime; key byte buffers are cleared.

MQTTnet's WebSocket implementation does not apply its TCP TrustChain option. Setup
therefore uses the native validation callback with .NET X509Chain for both transports.
It preserves hostname/missing-certificate errors and validates trust, expiry, server
authentication purpose and configured revocation mode. Custom roots replace OS roots.
No accept-all callback is exposed. See upstream
[URI handling](https://github.com/dotnet/MQTTnet/blob/v5.2.0/Source/MQTTnet/Options/MqttClientOptionsBuilder.cs)
and [WebSocket TLS handling](https://github.com/dotnet/MQTTnet/blob/v5.2.0/Source/MQTTnet/Implementations/MqttWebSocketChannel.cs).

## Verification

- 136 MQTT test executions pass across .NET 8/10. Integration tests use real local
  TCP, TLS, WS and WSS connections to MQTTnet servers on isolated ports.
- Coverage includes retained messages, QoS negotiation, binary/UTF-8 publication,
  envelope lineage, configured startup, runtime subscriptions, persistent sessions,
  forced disconnection/reconnection, deliberate disconnect suppression, broker
  refusals and recovery, cancellation of retry waits, duplicate owners and atomic
  located configuration errors, resource-controlled automatic/manual connection and
  connecting/connected/disconnecting/disconnected event ordering. Misplaced component
  connection-policy settings reject registration instead of being silently ignored.
- Mutual TLS tests cover password-protected PFX and encrypted PEM private keys,
  private CA roots, and MQTT username/password over TCP and WebSocket transports.
  Negative cases reject untrusted/expired/wrong-host certificates, missing client
  certificates, incorrect credentials, invalid addresses and malformed certificate files.
- Retry coverage includes finite and zero-retry budgets, recovery after exhaustion,
  fresh budgets after connection loss, native exponential delays and maximum delay,
  Disconnect during backoff, application stop, failed subscription restoration and
  connection-attempt timeouts.
- Disconnect flag coverage includes AutoConnect on/off, typed commands, serialized
  FlowValue commands and plain JSON with an omitted flag. Suppression permits later
  explicit Connect; normal completion keeps the resource disconnected in every case.
- Full Release suite: 2,448 passing test executions; zero build warnings.
- Public API baseline, solution, package manifest and release plan are updated.
- Isolated candidate package consumer passes on .NET 8/10, including all three MQTT
  component registrations and deferred connection behavior. Candidate version:
  0.0.0-local.20261004170830 (before the Disconnect flag addition). The flag was
  verified by the runtime and full Release suites above. No package was published.

## Scope

Configuration targets MQTT 5. TLS and WebSockets are tested against local servers;
external broker interoperability and live revocation infrastructure are not tested. Runtime
construction accepts native client options through MqttClientResourceOptions. Subscriptions are in-memory state for one
application instance; cleaning up unknown filters retained from an earlier process
requires a clean broker session. Output remains ordinary best-effort broadcast;
native MQTT acknowledgements do not assert downstream workflow completion.

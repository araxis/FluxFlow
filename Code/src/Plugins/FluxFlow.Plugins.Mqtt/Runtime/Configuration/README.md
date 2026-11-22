# MQTT setup

```csharp
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Configuration.Application;

services.AddMqtt(); // Optional baseDirectory for relative certificate paths.
services.AddFluxFlowApplication(configuration);
```

AddMqtt registers the resource and all three component types. Configuration reading
validates and registers deferred factories; neither Add nor Build connects to a broker.

```json
{
  "Resources": {
    "broker": {
      "Type": "mqtt.client",
      "Address": "mqtt://localhost:1883",
      "ClientId": "factory-monitor",
      "AutoConnect": true,
      "Reconnect": {
        "MaxRetryAttempts": 10,
        "Delay": "00:00:01",
        "BackoffType": "Exponential",
        "UseJitter": true,
        "MaxDelay": "00:00:30"
      }
    }
  },
  "Workflows": {
    "factory": {
      "connection": {
        "Type": "mqtt.connection",
        "Connection": "broker"
      },
      "receive": {
        "Type": "mqtt.subscriber",
        "Connection": "broker",
        "Topics": [
          "factory/status",
          { "Topic": "factory/+/temperature", "QoS": 1 }
        ]
      },
      "publish": {
        "Type": "mqtt.publisher",
        "Connection": "broker"
      }
    }
  }
}
```

Topics accepts a single string, a single object, or an array containing either form.
Missing Topics starts with no subscriptions and allows commands to add them. QoS
defaults to 0. Topic filters and QoS are validated while reading configuration;
diagnostics include the offending section/array location. Publisher always receives
its topic, payload and publication settings in an input request.

Every component supports ordinary Input/Output/Events/Error references and
OutputCapacity (128 by default). Link fields behave like those of other components.
Connection references use the existing local/global resource resolution rules.
Subscriber optionally accepts `AcknowledgementMode: "Manual"`, `MaxPendingAcknowledgements`
(positive, default 128), and an `ACK` source-port reference using the usual link syntax.
Manual subscriptions must use QoS 1. The default `Automatic` mode preserves existing behavior.
See the [subscriber guide](../../Definitions/MqttSubscriber/README.md)
for acknowledgement IDs, Request–Reply mapping, Stop and reconnect semantics.
Use one connection and one subscriber component per resource; multiple publishers
are supported. Build rejects duplicate connection/subscriber owners.

## Broker address and authentication

Address selects the native MQTTnet transport:

| Address | Transport | Default port |
| --- | --- | --- |
| mqtt://broker or tcp://broker | MQTT over TCP | 1883 |
| mqtts://broker | MQTT over TLS | 8883 |
| ws://broker/mqtt | MQTT over WebSocket | 80 |
| wss://broker/mqtt | MQTT over secure WebSocket | 443 |

WebSocket paths and query strings are preserved. Transport selection uses the scheme,
not probing or falling back to an unencrypted connection. A TCP address cannot have
a path or query. Fragments and embedded credentials are rejected.

Host plus optional Port remains supported for TCP. Tls true selects TLS and defaults
the port to 8883; otherwise the default is 1883. Specify Address or Host/Port, not both.
With Address, Tls is unnecessary and must agree with its scheme if supplied.

Username and Password are MQTT credentials for either transport, supplied separately
from Address. Password requires Username. Values can come from any .NET configuration
provider, including environment variables; there is no separate secret storage mechanism.

## TLS and certificates

Ordinary public certificates use the operating system trust store. For private CAs
and mutual TLS, configure the resource:

```json
{
  "Type": "mqtt.client",
  "Address": "wss://broker.example.com:443/mqtt",
  "ClientId": "factory-monitor",
  "AutoConnect": true,
  "Username": "factory-client",
  "TrustedCertificates": ["certificates/company-root.pem"],
  "ClientCertificate": {
    "Path": "certificates/client.pfx"
  }
}
```

ClientCertificate accepts a PFX/P12 file with an optional Password. For PEM, set Path
to the certificate and PrivateKeyPath to its key file. Password then decrypts that
private key; omit it for an unencrypted key. A `.pem` Path can also hold both the
certificate and key. Client certificates must include a private key.

TrustedCertificates is a list of PEM or DER CA certificate files (one certificate
per file). When supplied, these form the custom trust roots instead of the OS roots.
They work for both TLS TCP and WSS. TLS verifies the hostname, validity period,
certificate chain and server-authentication purpose. Protocol versions are negotiated
by the OS. There is no accept-all certificate setting.

CertificateRevocationMode accepts Online (default), Offline or NoCheck. For a private
test CA without revocation infrastructure, explicitly select NoCheck; hostname, expiry
and trust validation still apply. Certificate settings require a secure transport.

Relative certificate paths resolve against AddMqtt(baseDirectory), defaulting to
AppContext.BaseDirectory. Files are loaded when the resource is built, before connecting.
Native DI owns the certificates and disposes them after the MQTT client. Reading
configuration never opens a network connection or loads private keys.

The resource also accepts CleanSession (true), SessionExpiryInterval (seconds, 0)
and Timeout (`00:00:10`). It uses MQTT 5. Persistent sessions require a stable ClientId,
CleanSession false and a suitable SessionExpiryInterval. Use a unique ClientId per instance.

## Automatic connection and reconnect

AutoConnect and Reconnect belong to the resource, not mqtt.connection. AutoConnect
defaults to true. False leaves initial connection establishment to an input command.
Reconnect uses Microsoft.Extensions.Resilience and its underlying retry strategy:

| Setting | Default | Meaning |
| --- | --- | --- |
| MaxRetryAttempts | 2147483647 | Retries after the first attempt; 0 means one attempt only |
| Delay | 00:00:05 | Initial retry delay |
| BackoffType | Constant | Constant, Linear or Exponential |
| UseJitter | false | Randomize retry delays |
| MaxDelay | omitted | Optional upper bound on retry delay |

The defaults preserve effectively unlimited retries every five seconds. A configured
finite budget stops when exhausted and emits `mqtt.reconnect_exhausted` through Events;
it does not silently restart another budget. Each failed attempt emits an ordinary
`mqtt.connection_failed` error on Output. A later Connect command can recover.
Connection loss after successful connection starts a fresh retry budget and restores
the current subscriptions. Failed subscription restoration is part of the same attempt.

Timeout bounds each connection attempt, including subscription restoration. A timeout
is retried; application cancellation is not. Disconnect cancels pending backoff and
starts a new connection cycle when the resource's AutoConnect is true. Send
`{ "command": "Disconnect", "disableReconnect": true }` to suppress reconnection
until a later Connect (or a plain Disconnect that restores AutoConnect behavior).
The flag defaults to false; with AutoConnect false, Disconnect stays disconnected
either way. Shutdown always suppresses reconnection and cancels delays and attempts.
There is no idle polling loop. Retry applies to connection establishment; publishing
is not automatically repeated. A Connect command retains its immediate, single-attempt
result; if it fails while the application is running, it requests a new automatic cycle.
AutoConnect false does not disable recovery after an explicit Connect.

ReconnectInterval is removed; migrate it to Reconnect.Delay. The package reference is
Microsoft.Extensions.Resilience; Polly remains a transitive dependency of that package.

The connection component still forwards the
application lifecycle to the resource and publishes connecting/connected/disconnecting/
disconnected events. Registration and construction do not open connections. For command
payloads, received data and reconnect behavior, see the
[runtime guide](../README.md).

# FluxFlow.Components.Mqtt.Composition

Canonical MQTT resources and component factories for `FluxFlow.Composition`.
The package maps one or more logical MQTT clients to the provider-neutral
controller in `FluxFlow.Components.Mqtt`; concrete adapters remain responsible
only for the underlying protocol transport.

## Boundary

- `mqtt.broker` describes one broker endpoint. Multiple logical clients may
  share it.
- `mqtt.client` describes one independently identified logical client and owns
  its credentials, certificates, Last Will, desired subscriptions,
  auto-connect mode, and reconnect policy.
- `mqtt.subscription` names reusable subscription settings.
- `retry.policy` names reusable reconnect settings.
- `mqtt.command`, `mqtt.publish`, `mqtt.receive`, and `mqtt.events` share the
  keyed `IMqttClientController` selected by their `Client` property.

Definitions use the exact canonical names above. Retired `resilience.retry`,
`mqtt.control`, and `mqtt.trigger` values are rejected and must be migrated
before load.

The host registers an `IMqttTransportFactory`, credentials, certificates, and
optional clocks. `AddMqttComponents()` adds the MQTT descriptors, Designer
provider, and `IApplicationResourceRegistrar`. During revision
preparation, that registrar validates MQTT resource references and registers
broker, retry, subscription, client configuration, and one host-lifetime
controller per `mqtt.client` address. It does not scan assemblies or choose a
concrete MQTT provider.

## Registration

```csharp
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Engine;
using Microsoft.Extensions.DependencyInjection;

services.AddSingleton<IMqttTransportFactory>(transportFactory);
services
    .AddFluxFlow(definition)
    .AddMqttComponents();
```

For different transports per client, register keyed factories under the full
client resource address. A keyed factory takes precedence over the unkeyed host
default. The same rule applies to an optional keyed `TimeProvider`.

## Canonical Document

The application document remains flat at its two root sections. Resource groups
are namespaces, workflow names are object keys, and component settings and link
declarations sit directly on each component.

```json
{
  "Resources": {
    "Messaging": {
      "Broker1": {
        "Type": "mqtt.broker",
        "Host": "broker.internal",
        "Port": 8883,
        "UseTls": true
      },
      "Reconnect": {
        "Type": "retry.policy",
        "Strategy": "Exponential",
        "InitialDelay": "00:00:01",
        "MaximumDelay": "00:01:00"
      },
      "Commands": {
        "Type": "mqtt.subscription",
        "TopicFilter": "commands/+",
        "Qos": "AtLeastOnce"
      },
      "Client1": {
        "Type": "mqtt.client",
        "ClientId": "application-client-1",
        "Broker": "Resources.Messaging.Broker1",
        "Credentials": "Resources.Security.MqttCredentials",
        "Reconnect": "Resources.Messaging.Reconnect",
        "Subscriptions": "Resources.Messaging.Commands",
        "AutoConnect": "OnStart"
      }
    },
    "Security": {
      "MqttCredentials": {
        "Type": "host.credentials"
      }
    }
  },
  "Workflows": {
    "CommandProcessing": {
      "Receive": {
        "Type": "mqtt.receive",
        "Client": "Resources.Messaging.Client1",
        "Subscription": "Commands",
        "Ack": "Handle.Output",
        "Nak": "Handle.Failure"
      },
      "Handle": {
        "Type": "application.command-handler",
        "Input": "Receive.Output"
      },
      "Publish": {
        "Type": "mqtt.publish",
        "Client": "Resources.Messaging.Client1",
        "Input": "CreateReply.Output"
      },
      "CreateReply": {
        "Type": "application.reply-mapper",
        "Input": "Handle.Output"
      },
      "Control": {
        "Type": "mqtt.command",
        "Client": "Resources.Messaging.Client1"
      },
      "ClientEvents": {
        "Type": "mqtt.events",
        "Client": "Resources.Messaging.Client1"
      }
    }
  }
}
```

A single subscription may be a string or inline object. Multiple subscriptions
use a mixed array of names and inline objects. Client resource `Subscriptions`
accepts one canonical `Resources...` address or an array of addresses.

## Node Contracts

| Type | Input | Output |
|---|---|---|
| `mqtt.command` | `FlowMessage<MqttClientRequest>` | `FlowMessage<MqttClientResult>` |
| `mqtt.publish` | `FlowMessage<MqttPublishMessage>` | `FlowMessage<MqttClientResult>` |
| `mqtt.receive` | `Ack`, `Nak` signals | `FlowMessage<MqttReceivedApplicationMessage>` |
| `mqtt.events` | none | `FlowMessage<MqttClientEvent>` |

Command and publish failures are normal `MqttClientResult` values with
`Kind = "Error"`, `IsError = true`, and a structured `Error`; there is no
universal error port. Workflow links and mappers can inspect those fields like
any other data. Trigger `Ack` and `Nak` accept any `FlowMessage<T>` and match
only its trace identity, so the signal payload type is irrelevant.

Command processing supports sequential or concurrent request processing, independent
result ordering, bounded pending work, and explicit maximum concurrency.
Trigger claims remain exclusive per named or equivalent inline subscription;
duplicate claims fail immediately during controller registration.

## Secrets And Ownership

Hosts should register `MqttCredentialConfiguration` and
`MqttClientCertificate` as keyed services under the referenced canonical
resource addresses. Direct `Username` and `Password` values override a
referenced credential value. Inline passwords and certificate bytes are
rejected unless the host explicitly supplies an `IMqttInlineSecretPolicy` that
allows them.

The service provider owns controllers created by this package. Components share but
never dispose those controllers. Broker connections, client sessions,
subscriptions, reconnect, and desired-state restoration stay in the core
controller; components remain ordinary workflow nodes.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`MqttComponentDefinition` describes all four component types, their
options, fixed ports, signal-port kind, and host-owned `Client`/`Clock` picker
hints. The metadata is descriptive only; hosts still own resource catalogs,
secret entry, rendering, persistence, and lifecycle policy.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit MqttComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddMqttComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

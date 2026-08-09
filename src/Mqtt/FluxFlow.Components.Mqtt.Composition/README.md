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

### Broker transport

Broker resources use one portable transport choice plus the existing TLS flag:

| `Transport` | `UseTls` | Connection |
| --- | --- | --- |
| omitted or `Tcp` | `false` | TCP |
| omitted or `Tcp` | `true` | TLS over TCP |
| `WebSocket` | `false` | `ws` |
| `WebSocket` | `true` | `wss` |

WebSocket resources may set `WebSocketPath`; it defaults to `/mqtt`. The same
properties are available through `MqttBrokerResourceBuilder`. They remain
provider-neutral and work with both official adapters.

The host registers an `IMqttTransportFactory`, credentials, certificates, and
optional clocks. The four code-first resource contracts carry one shared MQTT
registrar into the built definition. `AddMqtt()` registers that exact registrar,
the official component contracts, and Designer metadata for JSON/configuration
hosts. During revision preparation, the effective registrar validates MQTT
resource references and registers
broker, retry, subscription, client configuration, and one revision-owned
controller per `mqtt.client` address. Host transports, credentials,
certificates, clocks, and inline-secret policy are resolved explicitly from the
host provider and are never transferred to revision ownership. It does not scan
assemblies or choose a concrete MQTT provider.

## JSON/configuration registration

```csharp
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Engine;
using Microsoft.Extensions.DependencyInjection;

services.AddSingleton<IMqttTransportFactory>(transportFactory);
services
    .AddFluxFlow(definition)
    .AddMqtt();
```

This explicit package registration is required when `definition` came from
JSON/configuration because portable JSON contains no CLR factory, registrar, or
contract delegates.

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

## C# Authoring

MQTT resource and workflow extensions support the existing handle-returning
style and an opt-in chain-first style. The latter appends a typed `out` handle
and returns the same resource container or workflow:

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddResourceGroup("Messaging", out var messaging)
    .AddWorkflow("Orders", out var orders);

messaging
    .AddMqttBroker(
        "Broker1",
        options =>
        {
            options.Host = "broker.internal";
            options.Port = 443;
            options.Transport = MqttBrokerTransport.WebSocket;
            options.UseTls = true;
            options.WebSocketPath = "/mqtt";
        },
        out var broker)
    .AddMqttSubscription(
        "Commands",
        options =>
        {
            options.TopicFilter = "commands/+";
            options.Qos = MqttQos.AtLeastOnce;
        },
        out var commands)
    .AddMqttRetryPolicy("Reconnect", out var reconnect)
    .AddMqttClient(
        "Client1",
        options =>
        {
            options.ClientId = "application-client-1";
            options.Broker = broker;
            options.UseReconnect(reconnect);
            options.AddSubscription(commands);
        },
        out var client);

orders.AddMqttPublish(
    "Publish",
    options => options.Client = client,
    out var publish);
```

The direct form remains valid, for example
`var client = messaging.AddMqttClient(...)`. Both forms call the same
configuration and validation implementation and build the same canonical
resource properties. Those calls also capture the exact executable application
resource contracts, so the compiled-C# host needs only:

```csharp
services.AddSingleton<IMqttTransportFactory>(transportFactory);
services.AddFluxFlow(application.Build());
```

Do not repeat `.AddMqtt()` for this code-first definition. Capturing a component
does not connect it: call
`orders.Connect(...)` with real typed ports, or `application.Connect(...)` for
an intentional cross-workflow link.

If an external host already owns a controller, bind it by the typed resource
handle instead of rebuilding its address:

```csharp
services.AddExternalFluxFlowResource(client, controller);
```

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

Each revision provider owns the controllers created for that revision.
Components in that revision share but never dispose those controllers. A failed
candidate, replaced revision, or application stop disposes the revision
provider and its controllers while leaving host-provided transports,
credentials, certificates, clocks, and policy host-owned. Broker connections,
client sessions, subscriptions, reconnect, and desired-state restoration stay
in the core controller; components remain ordinary workflow nodes.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, exposes the domain-specific pending request,
message, and event capacities as advanced runtime controls, and omits legacy
`name`, `maxDegreeOfParallelism`, and `ensureOrdered` options from normal
editing. Default execution requires no processing profile.


`MqttComponentDefinition` describes all four component types, their
options, fixed ports, signal-port kind, and host-owned `Client`/`Clock` picker
hints. The metadata is descriptive only; hosts still own resource catalogs,
secret entry, rendering, persistence, and lifecycle policy.

## JSON and dynamic-host registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit MqttComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddMqtt();
```

The resulting host registrations supply descriptors and the registrar for
definitions that did not originate from complete code-first contracts.
Standalone runtime nodes remain usable without this package, and referenced
external resources remain host-owned.

## Code-first authoring

`MqttComponents` exposes `MqttCommand`, `MqttPublish`, `MqttReceive`, and `MqttEvents` typed contracts. The retained `AddX` methods use those same contracts; named handles expose data or signal ports plus explicit `Events`. See [typed code-first authoring](../../../docs/39-typed-code-first-authoring.md).

A definition built from these component and resource contracts retains its
executable descriptors and registrar. Normal code-first hosting therefore calls
only `AddFluxFlow(definition)` and does not repeat the family registration
above. Use that service registration for JSON/configuration, catalog, or dynamic
definitions that do not carry the complete contracts.

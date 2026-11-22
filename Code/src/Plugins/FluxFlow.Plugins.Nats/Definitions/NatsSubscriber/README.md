# NATS Subscriber

`nats.subscriber` is a source for a configured NATS subject.

## Ports

- **Output:** `Subject`, optional `ReplyTo`, `DataBase64` and `Headers`.
- **Events:** standard component lifecycle events. There is no Input port.

`Subject` may contain `*` for one token or a final `>` for remaining tokens.
Set an optional queue group to distribute messages among members; this is load
balancing, not a durable consumer. Leave it absent for independent subscriptions.
Startup sends a subscription and a protocol round trip; asynchronous permission
errors may arrive immediately afterward and fault the session.

```json
{"Type":"nats.subscriber","Connection":"broker","Subject":"readings.*","QueueGroup":"workers","Capacity":128}
```

## Configuration

Choose a `nats.connection` resource. The companion resolves authentication and
TLS file references from its own environment; credentials do not belong in
project files. Components using that resource share a run-owned official NATS
client. Configure finite timeouts, buffer capacity and maximum payload bytes in
the docked Property Grid. Advanced JSON remains optional, with synchronized
Visual/JSON modes. Unknown JSON fields survive editing.

## Delivery and lifetime

Core NATS is at-most-once messaging, without durable storage or guaranteed
redelivery. A successful send is not a broker acknowledgement. A disconnect or
cancellation can leave the publication outcome unknown; components do not retry
ambiguous commands. Native connection recovery can restore subscriptions, but
cannot replay Core messages missed while disconnected. Permission errors fail
the shared session because the server cannot reliably identify a concurrent
publication that caused an asynchronous error.

Buffers are bounded. A slow consumer applies backpressure and can be disconnected
by the server. Graceful Stop drains pending work within the configured command
deadline; execution cancellation aborts it. Subscriptions are disposed on Stop,
and fresh runs create fresh subscriptions and sessions. External services are
never removed.

Original dependency diagnostics use the execution host's logger factory. Workflow
spans use the existing OpenTelemetry instrumentation. Payload bytes are always
Base64, never implicitly decoded as text.

## Platform and environment

The companion supports Core NATS on .NET 8 and 10. Use an existing NATS/TLS
endpoint or a managed local NATS container. The managed profile exposes plaintext
port 4222 on loopback, optional host-owned authentication and a startup
configuration file for permissions. Use an existing-service connection for a
custom TLS listener. Docker is needed only for managed containers.


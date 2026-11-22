# NATS Reply

`nats.reply` responds to the inbox on a received request.

## Ports

- **Input:** a received message with `ReplyTo`, `DataBase64` and optional
  `Headers`. Change the bytes before forwarding to compose a response.
- **Output:** the original input after the send completes, or a workflow error.
- **Events:** standard component lifecycle events.

Connect a Subscriber to Reply for an echo service, or insert mapping/processing
between them. Preserve `ReplyTo` when mapping. A missing inbox is an error.
Replies are Core publications and do not provide durable delivery guarantees.

```json
{"ReplyTo":"_INBOX.example","DataBase64":"AQID","Headers":{"Content-Type":["application/octet-stream"]}}
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


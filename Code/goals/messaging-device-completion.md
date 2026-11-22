# Messaging and device completion

Baseline inspected 2026-11-01. Complete each protocol through runtime, Studio and
verification before beginning the next: RabbitMQ AMQP, Serial, NATS Core, JetStream.

| Slice | Existing coverage | Work remaining | Explicit limits |
| --- | --- | --- | --- |
| RabbitMQ | Native AMQP connection, publisher, consumer and explicit settlement; AMQP provider; shared Visual/JSON editing, connections, profiles, catalog bindings and sample | Maintain regression coverage as subsequent protocols are added | AMQP 0.9.1. Explicit acknowledgements do not guarantee exactly-once downstream processing. Permissions can be configured through RabbitMQ definitions files. |
| Serial | Compatible `serial.text`; shared run-owned session, Reader/Writer, bounded raw/delimiter/fixed-length framing, companion discovery, shared editing, device bindings and sample | Physical loopback verification when an identified device is available | No identified loopback hardware. Browser authoring reports runner-only resource validation. Desktop supports manual port names; its discovery adapter is not yet registered. |
| NATS Core | Official client, shared connection, Publisher/Subscriber/Request/Reply, Visual/JSON editor, service profiles, catalog bindings and sample | Maintain regression coverage while adding JetStream | Core messages are not durable; no guaranteed redelivery. Managed containers expose local plain TCP; custom TLS services use existing-service bindings. |
| JetStream | Publisher, durable pull consumer and explicit settlement; shared Visual/JSON editing, NATS connection/service profiles and runnable sample | Maintain regression coverage | Existing entities are never reconciled or deleted. Managed container storage is temporary. No exactly-once downstream guarantee. |

## Implementation and acceptance gates

- Reuse `ResourceRegistration`, `ComponentRegistration`, `FlowLifetime`,
  `FlowMessage`, the companion execution host and registered environment providers.
- Keep protocol, server implementation, physical device, execution host, network
  and project binding distinct. Catalog placements remain locked snapshots.
- Preserve existing MQTT, HTTP, Serial Text and BLE behavior. Do not modify Core
  or WFENG. Use existing OpenTelemetry/OTLP; retain original diagnostic records.
- Every protocol requires typed validation, runtime/companion registration,
  component guides, runnable samples, shared Visual/JSON editing, Connections,
  compatible topology/catalog bindings, profiles, effective endpoints and checks.
- Secrets must be supplied at execution from host-owned configuration, not saved
  into project connection definitions. Existing registry credentials remain in
  their current device-owned store; they are not broker credentials.
- Verify .NET 8 and 10: payload/metadata fidelity, permission failures, bounded
  traffic, startup rollback, cancellation, disconnect, Stop, late records, reruns,
  multi-instance ownership, snapshots, save/reopen and Undo/Redo. Exercise original
  log contracts including real RabbitMQ disconnect/shutdown.
- Use disposable RabbitMQ/NATS containers and deterministic serial adapters.
  Browser trials use temporary projects and preserve user tabs/configuration.

## RabbitMQ delivery contract

AMQP 0.9.1 only. Default consumption uses manual acknowledgement. A delivery has
an opaque, run/channel-scoped settlement token. The consumer's command input
accepts Ack, Reject or Requeue explicitly; downstream logic decides when to send
that command. A local enqueue never acknowledges the broker. Unsettled deliveries
are left for broker redelivery when their channel closes. Tokens from a prior
channel/run are invalid. This is not an end-to-end transaction or exactly-once
processing guarantee.

Publishing awaits broker confirms and treats mandatory returns as failures.
Cancellation/disconnection may leave publication outcome unknown; no automatic
application-level publication retry is allowed. Native connection recovery may
restore subscriptions, but must not make stale settlement tokens valid again.
Entity declarations are opt-in; existing exchanges, queues and bindings remain
usable without provisioning permissions. Never delete external entities on Stop.

## Status

- Baseline: inspected; coverage matrix recorded.
- RabbitMQ: delivery and lifecycle gate passed on .NET 8 and 10. Runtime tests
  cover explicit settlement, redelivery, permissions, rejected credentials,
  mandatory returns and recovery. Companion tests cover authenticated virtual
  hosts, repeated delivery, original telemetry, Stop/container removal and reruns.
  Studio tests cover locked profiles, catalog snapshots, save/reopen and Undo/Redo.
  A temporary browser project verified Visual/JSON editing, Save/Cancel, Undo/Redo,
  repeated logs, trace hierarchy and retained observations after Stop.
- Serial: automated and browser integration gate passed, with hardware delivery
  explicitly unverified. Runtime tests: 118 across .NET 8 and 10, including existing
  Serial Text, shared sessions, framing, bounded output, partial writes, disconnect,
  access-denied startup, outstanding I/O, exclusive ownership and reruns. Focused
  Studio tests: 218 across both targets; companion/physical-device and original
  RabbitMQ shutdown/disconnect log contracts: 10 across both targets. Additional
  environment ownership/runtime checks passed earlier in the phase.
  The temporary browser project verified Visual/JSON synchronization, exact
  delimiter Undo/Redo, Cancel, port discovery and selected-port field refresh.
  Discovery listed COM4 through COM9 without opening them; no port was run because
  none was identified as a loopback device. Saved sample format and Logger payload
  were corrected. Canvas ports now refresh after container changes, verified after
  dialogs, navigation and Undo/Redo. Preview is available at localhost:5403.
- NATS Core: integration gate passed on .NET 8 and 10. Runtime delivery/TLS tests:
  20; connection configuration tests: 20; environment tests including startup
  failure diagnostics and cleanup: 16; palette/port/sample contracts: 80.
  Shared original RabbitMQ disconnect/shutdown and registration contracts passed.
  Native bytes and repeated headers, authentication/permissions, queue groups,
  request deadlines, exhausted reconnects, cancellation, bounded buffering,
  Stop retention and rerun isolation are covered. Locked profile/catalog
  snapshots and save/reopen remain intact. The temporary browser trial verified
  Visual/JSON editing, Save/Cancel and Undo/Redo, repeated request/reply delivery,
  original native logs, workflow traces and container removal after Stop.
  Early container exits now retain native startup diagnostics even when the
  library's initial port-mapping wait reports a timeout. Preview: localhost:5404.
- JetStream: integration gate passed on .NET 8 and 10. Combined Core/JetStream
  runtime tests: 36; configuration tests: 32; Studio palette/ports/sample tests: 88.
  Companion delivery, authenticated execution, original RabbitMQ log contracts
  and registration checks: 16. Existing lifecycle/control tests: 104.
  Coverage includes confirmed publishing, duplicate detection, explicit Ack/Nak/
  Progress/Terminate, delivery limits, bounded consumption, restricted existing
  entities, cancellation, disconnects, retained observations and rerun isolation.
  Application shutdown now closes terminal controls after business outputs drain,
  without circularly waiting on event bridges that depend on those controls.
  The temporary browser trial verified Visual/JSON synchronization, Save/Cancel,
  Undo/Redo, repeated exact-byte delivery and confirmed acknowledgements. Stop
  removed the managed container while retaining logs, outputs and trace hierarchy.
  Existing streams and consumers are not updated or deleted. Preview: localhost:5405.
- Queued after these phases: audit the BLE completion checklist against the
  existing Visual/JSON, GATT, advertising, codec and device-binding implementations.
  Implement missing requirements and retain explicit hardware/Stop limitations.

## Primary references

- [RabbitMQ .NET client guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide)
- [RabbitMQ consumer acknowledgements and publisher confirms](https://www.rabbitmq.com/docs/confirms)
- [NATS official .NET client](https://github.com/nats-io/nats.net)

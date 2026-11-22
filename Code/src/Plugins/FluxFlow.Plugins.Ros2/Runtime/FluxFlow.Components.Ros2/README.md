# FluxFlow.Components.Ros2

ROS 2 topic components using Rcl.NET 2.1.2 and host-generated message types.

- RosPublisher<T> has Input, Output and Events. It converts a value to a generated
  ROS message, awaits native publication, then forwards the original FlowMessage.
  Input errors bypass publication. Conversion and publish errors become normal
  processing-error output; later messages can still be processed.
- RosSubscriber<T> has Output and Events and implements IFlowSource. Start creates
  its subscription, and Stop cancels reading and disposes it. Every received message
  starts a new FlowMessage trace. It does not propagate an envelope across ROS wires.
- Both borrow an IRclNode. The host owns the node/context. Publisher owns and disposes
  its native publisher after accepted work drains; Subscriber owns its subscription.
- Options accept native PublisherOptions / SubscriptionOptions, including QoS.
  Subscription's native delivery queue and DDS history are separate from FluxFlow
  output capacity. No durable delivery or consumer acknowledgment is implied.

Default JSON conversion preserves the generated C# property names, with case-insensitive reads,
and rejects unmapped fields. Subscriber snapshots each managed message immediately;
it never publishes a borrowed native buffer or a mutable ROS message as FlowValue.
Publisher rejects JSON null. ToMessage / ToValue delegates can override conversion
for custom immutable FlowValue types; no expression engine belongs in this package.
Byte arrays follow System.Text.Json's Base64 representation.

For example, a C# `SequenceNumber` property is JSON `SequenceNumber`, not `sequence_number`.
Use the existing conversion delegates when a host needs a different JSON representation.

Rcl.NET PublishAsync has no cancellation token. Shutdown waits for an in-flight
native publication before disposing its endpoint; it cannot forcibly interrupt a
blocked middleware call. QoS/discovery remain native ROS responsibilities.

Generated .msg bindings and corresponding native ROS type-support libraries must
be supplied by the host. This package does not ship robot-specific messages.
Run from a configured ROS environment. Build this generic library without ROS installed.

See the [ROS 2 plugin](../../README.md) for configuration and typed host registration.
Services and actions are subsequent integration steps; this version implements topics.

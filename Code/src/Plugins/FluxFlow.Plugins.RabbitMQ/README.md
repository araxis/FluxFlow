# RabbitMQ plugin

Adds RabbitMQ Publisher, Consumer, Acknowledge and the AMQP connection to FluxFlow Studio. Workflows keep their existing `amqp.publisher`, `amqp.consumer`, `amqp.settlement` and `amqp.connection` identifiers.

Install the package from **Plugins > Manage plugins**, restart the companion, then reload Studio. Removing the plugin disables AMQP execution but leaves saved settings, links and environment bindings available for reinstall. MQTT connections to RabbitMQ use the separate MQTT plugin.

The plugin contains its runtime and RabbitMQ.Client. Its separate design and settings assemblies describe broker declarations, nested typed arguments, connection settings and ports without a dependency on a UI framework or AMQP driver. A consumer uses explicit settlement: its ACK and NACK inputs are always available. Requeue on NACK controls the broker action, not whether the NACK input exists.

Use the matching Studio and companion release for managed environment support. When upgrading, replace the package and restart the companion. No workflow conversion is required.

For C# integrations that convert native field values, use `AmqpWireField.FromNative(value)` in place of `AmqpField.FromNative(value)`.

The plugin supplies RabbitMQ AMQP container setup, startup definitions and execution-host credential references. Removing it makes these environment features unavailable. Saved projects retain their settings for reinstall.

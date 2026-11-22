# MQTT plugin

Provides the MQTT Publisher, Subscriber, Connection Controller and the MQTT client connection for FluxFlow Studio. The subscriber supports workflow-controlled acknowledgements for QoS 1. Existing MQTT configuration, connection names and port identifiers are unchanged.

Install the package from **Plugins > Manage plugins**, then restart the companion and reload Studio. Removing the plugin disables MQTT execution on that companion; installing it again restores the components for existing projects.

The package owns the three component implementations, their registrations, MQTTnet and the portable design assembly. The runtime and editor share the same broker, topic and QoS settings library; neither references the other. Studio and other design hosts render the topic fields, broker settings and conditional ACK port through the shared contract without loading the MQTT driver. Automatic mode hides unused ACK ports and keeps existing links inactive; Manual enables them and defaults new topics to QoS 1.

Use the matching Studio and companion release for managed environment support. On an updated Studio, packages without the user-editing contract still display saved users and permissions read-only. When upgrading, replace the package and restart the companion. Saved projects do not require a conversion.

The plugin supplies Mosquitto and RabbitMQ MQTT container setup, users and permissions, and existing-service TLS settings. A bound host service supplies the endpoint and its TLS scheme. “Use connection” keeps the connection’s credentials and certificates; “Service settings” replaces them, including clearing values left empty. The Inspector shows the source for each field without displaying passwords. Removing the plugin makes these environment features unavailable. Saved projects retain their settings for reinstall.

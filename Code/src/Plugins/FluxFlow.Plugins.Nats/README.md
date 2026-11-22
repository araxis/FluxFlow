# NATS and JetStream

Install this package under **Plugins > Manage plugins**, restart the companion, then reload Studio. It provides NATS Publisher, Subscriber, Request and Reply; JetStream Publisher, Consumer and Settlement; and the NATS connection. The same package supplies the runtime, client libraries, portable inspector definitions and documentation for all seven components.

Existing projects keep their component types, connection references, links and settings. Without the plugin Studio preserves those records on save; reinstall the plugin to edit or run them again. JetStream Settlement targets a consumer in the same workflow. Its commands use the outstanding delivery token from that consumer and run.

The plugin supplies NATS container setup (including JetStream configuration and execution-host credential references). Removing it makes these environment features unavailable. Saved projects retain their settings for reinstall.

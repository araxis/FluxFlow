# MQTT-to-HTTP container integration tests

This deterministic suite provisions a disposable MQTT broker, runs the real
MQTT adapter, JSONata mapper, workflow runtime, and HTTP component, then sends
the request to a real loopback HTTP socket.

The broker image is pinned, the host port is assigned dynamically, readiness is
checked before the test starts, container logs are included in startup
failures, and every run uses unique MQTT identities and topics.

Run ./eng/test.ps1 -Suite Integration from the repository root. A compatible
container runtime must be available to the test process.

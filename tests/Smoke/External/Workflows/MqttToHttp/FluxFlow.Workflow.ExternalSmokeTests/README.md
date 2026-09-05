# MQTT-to-HTTP external smoke tests

This opt-in suite verifies a complete dynamically authored workflow against an
externally managed MQTT broker and a real HTTP socket. It is not part of the
deterministic CI suite because its availability and credentials belong to the
deployment environment.

Set FLUXFLOW_EXTERNAL_SMOKE=1 and configure FLUXFLOW_EXTERNAL_MQTT_HOST.
Optional settings are FLUXFLOW_EXTERNAL_MQTT_PORT,
FLUXFLOW_EXTERNAL_MQTT_TLS, FLUXFLOW_EXTERNAL_MQTT_USERNAME, and
FLUXFLOW_EXTERNAL_MQTT_PASSWORD.

Run ./eng/test.ps1 -Suite Smoke from the repository root.

Each future transport scenario owns its settings and probes. Do not extend a
global settings object with protocol-specific configuration.

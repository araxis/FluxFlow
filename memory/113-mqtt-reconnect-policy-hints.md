# MQTT Reconnect Policy Hints

Date: 2026-06-03

## Package

- Package: `FluxFlow.Components.Mqtt`
- Version: `0.5.0-alpha.1`
- Tag: `components-mqtt-v0.5.0-alpha.1`

## Goal

Let hosts express node-level reconnect intent to MQTT adapters without moving
connection recovery into the package.

The package already forwards adapter health events. The remaining useful
boundary improvement was to pass optional reconnect policy hints to the
host-provided adapter factory.

## Decision

Add a nullable `MqttReconnectPolicy` on publish and subscribe options and pass
it through `MqttClientFactoryContext.Reconnect`.

The package validates the shape and copies the policy into the factory context.
Adapters still own connection state, retry loops, shared clients, and
broker-specific recovery behavior.

If `reconnect` is not configured, the factory context keeps `Reconnect` null so
host adapters can keep their existing defaults.

## Changes

- Added `MqttReconnectPolicy`.
- Added optional `reconnect` settings to `mqtt.publish`.
- Added optional `reconnect` settings to `mqtt.subscribe`.
- Added `MqttClientFactoryContext.Reconnect`.
- Added reconnect policy validation.
- Copied reconnect policy attributes before exposing them to adapter factories.
- Documented reconnect policy hints in the MQTT package README.

## Verification

- Ran the MQTT test project in Release mode.
- Ran the full solution build in Release mode.
- Ran the full solution test suite in Release mode.
- Packed `FluxFlow.Components.Mqtt` `0.5.0-alpha.1`.
- Published `components-mqtt-v0.5.0-alpha.1`.
- Verified a clean public-feed restore/build smoke test with
  `MqttReconnectPolicy`.

## Result

MQTT adapters can now receive explicit reconnect intent from workflow nodes
without the package owning reconnect execution. This keeps the package boundary
honest while making host adapters easier to configure consistently.

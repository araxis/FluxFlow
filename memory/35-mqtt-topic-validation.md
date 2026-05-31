# MQTT Topic Validation

Date: 2026-05-31

## Goal

Tighten the MQTT package contract before the next package release by making
topic validation package-owned and reusable by hosts.

## Decision

Add public validation helpers in `FluxFlow.Components.Mqtt.Validation`:

- `MqttTopicValidator.ValidatePublishTopic`
- `MqttTopicValidator.ValidateSubscriptionFilter`
- `MqttTopicValidationResult`

The package remains adapter-based. Validation does not introduce a concrete
network client or host-specific schema.

## Behavior

- Publish topics are required, cannot contain `+` or `#`, cannot contain null
  characters, and cannot exceed the MQTT encoded string length.
- Subscription filters are required, may use `+` only as a full topic level,
  may use `#` only as the final full topic level, cannot contain null
  characters, and cannot exceed the MQTT encoded string length.
- Invalid publish request topics emit `FlowError` and allow later messages to
  continue.
- Invalid static node options fail node creation clearly.

## Version

Prepared as `FluxFlow.Components.Mqtt` `0.2.1-alpha.1`.

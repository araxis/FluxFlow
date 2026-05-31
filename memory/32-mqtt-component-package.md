# MQTT Component Package

Date: 2026-05-31

## Goal

Start the first reusable component family as a separate source project and
separate package artifact.

## Decisions

- Package project: `src/FluxFlow.Components.Mqtt`.
- Test project: `tests/FluxFlow.Components.Mqtt.Tests`.
- Package identity: `FluxFlow.Components.Mqtt`.
- Package version: `0.2.0-alpha.1`.
- The package references `FluxFlow.Engine`; the engine does not reference this
  package.
- The package does not include a concrete network client.
- Applications provide an `IMqttClientFactory`.
- The first nodes are adapter-backed, deterministic, and testable without a live
  broker.

## Contract Shape

- `mqtt.publish`
  - input port: `Input`
  - input type: `MqttPublishRequest`
  - result port: `Result`
  - result type: `MqttPublishResult`
  - static options: `MqttPublishOptions`
- `mqtt.subscribe`
  - output port: `Output`
  - output type: `MqttReceivedMessage`
  - static options: `MqttSubscriptionOptions`

Options stay static per node. Requests and messages stay per item.

## Implementation Status

Implemented:

- node type constants
- common port name constants
- request/result/message records
- connection, publish, and subscription options
- retained subscription options
- client adapter and factory contracts
- client factory context and explicit client ownership leases
- subscription leases for clear startup failure behavior
- module and registration extension
- adapter-backed publish and subscribe nodes
- background subscribe lifecycle handling for long-lived subscriptions
- diagnostics and event name constants with richer publish/subscribe metadata
- deterministic in-memory adapter tests
- package-scoped release support for publishing this project independently

Deferred:

- concrete MQTT client adapter package
- topic filter helper
- payload encoder/decoder nodes
- connection probe node
- mapper helper decisions after first consumer migration

## Review Notes

The package boundary is clean: protocol-specific nodes live outside the engine,
registration stays explicit, and app-specific workspace or scenario concepts do
not enter package contracts.

Next suggested step: run one migration spike in the first consumer using
`FluxFlow.Components.Mqtt` `0.2.0-alpha.1` and record any missing adapter or
options surface.

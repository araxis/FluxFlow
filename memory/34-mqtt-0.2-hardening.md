# MQTT 0.2 Hardening

Date: 2026-05-31

## Goal

Make `FluxFlow.Components.Mqtt` easier to use from host applications that own
connection resources, shared client lifetimes, dashboards, and stable app-level
node contracts.

## Changes

- Added `MqttClientFactoryContext` with node address, connection name, and
  connection profile.
- Added `MqttClientLease` so a host can choose owned or shared adapter
  lifetime.
- Changed subscriptions to return `IMqttSubscription`, allowing startup to fail
  before `StartAsync` returns when a subscription cannot be established.
- Added `ReceiveRetainedMessages` and `RetainAsPublished` subscription options.
- Added publish payload preview propagation.
- Added subscribe received events.
- Added richer diagnostic and event metadata for topic, payload size, quality
  setting, retain flag, and correlation id.
- Split MQTT error codes for more precise host handling.

## Verification

- Build: passed.
- Engine tests: passed.
- MQTT package tests: passed with 17 tests.

## Remaining

- Publish `FluxFlow.Components.Mqtt` `0.2.0-alpha.1`.
- Migrate the first consumer using the new factory context and lease contracts.
- Decide mapper helper shape after the consumer adapter is written.

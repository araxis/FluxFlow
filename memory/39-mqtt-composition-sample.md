# MQTT Composition Sample

Date: 2026-06-01

## Goal

Add a runnable MQTT package composition sample that does not require a live
broker.

## Decisions

- Sample project: `samples/FluxFlow.MqttCompositionSample`.
- The host provides an in-memory `IMqttClientFactory` and shared adapter.
- The sample composes:
  - `mqtt.subscribe`
  - `flow.mapper`
  - `flow.filter`
  - `flow.mapper`
  - `mqtt.publish`
- The host keeps message aliases, context factories, expression behavior, and
  result storage outside the component packages.
- The subscription node starts after the publisher has opened its adapter so the
  finite message source cannot race publisher startup.

## Flow

```text
mqtt.subscribe -> flow.mapper -> flow.filter -> flow.mapper -> mqtt.publish -> result sink
```

## Status

Implemented as a deterministic command-line sample with seeded input messages,
an in-memory adapter, package registration, typed aliases, context factories,
and diagnostic collection.
